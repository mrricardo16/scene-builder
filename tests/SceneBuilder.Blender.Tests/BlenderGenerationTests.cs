using System.Buffers.Binary;
using System.Text;
using SceneBuilder.Application;
using SceneBuilder.Domain;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class BlenderGenerationTests
{
    [Fact]
    public async Task Generate_publishes_valid_glb_only_after_a_success_status_line()
    {
        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            var result = await new BlenderSceneGenerator(new GlbWritingRunner()).GenerateAsync(Request(outputDirectory), CancellationToken.None);

            Assert.Equal(BlenderGenerationStatus.Succeeded, result.Status);
            Assert.True(File.Exists(Assert.IsType<string>(result.ArtifactPath)));
            Assert.Equal(1, result.GeneratedObjectCount);
            Assert.Equal(0, result.SkippedObjectCount);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_rejects_process_success_without_the_trusted_status_line()
    {
        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            var result = await new BlenderSceneGenerator(new GlbWritingRunner("unexpected")).GenerateAsync(Request(outputDirectory), CancellationToken.None);

            Assert.Equal(BlenderGenerationStatus.Failed, result.Status);
            Assert.Null(result.ArtifactPath);
            Assert.Contains(result.Diagnostics, item => item.Code == "BLENDER_PROCESS_STATUS_INVALID");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_uses_short_external_staging_for_deep_output_paths()
    {
        var testRoot = CreateTemporaryDirectory();
        var outputDirectory = Path.Combine(testRoot, new string('x', 120), "builds", ".staging-build-0001", ".scene-package.staging", "partitions", ".scene-builder-staging", "partition");
        Directory.CreateDirectory(outputDirectory);
        var runner = new RecordingGlbWritingRunner();
        try
        {
            var result = await new BlenderSceneGenerator(runner).GenerateAsync(Request(outputDirectory), CancellationToken.None);

            Assert.Equal(BlenderGenerationStatus.Succeeded, result.Status);
            Assert.True(File.Exists(Assert.IsType<string>(result.ArtifactPath)));
            Assert.False(runner.WorkingDirectory.StartsWith(outputDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.False(runner.Arguments[^1].StartsWith(outputDirectory, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Command_builder_uses_argument_list_without_shell_and_rejects_unsafe_output_names()
    {
        var command = BlenderCommandBuilder.Create("C:\\tool path\\blender.exe", "C:\\script path\\generate_scene.py", "C:\\manifest path\\manifest.json", "C:\\output path\\scene.glb", "C:\\work path", TimeSpan.FromSeconds(1), 100);
        var info = BlenderCommandBuilder.CreateStartInfo(command);

        Assert.False(info.UseShellExecute);
        Assert.Equal(["--background", "--factory-startup", "--python", "C:\\script path\\generate_scene.py", "--", "--manifest", "C:\\manifest path\\manifest.json", "--output", "C:\\output path\\scene.glb"], info.ArgumentList);
    }

    [Fact]
    public void Validator_accepts_minimal_glb_and_rejects_wrong_magic_without_modifying_the_file()
    {
        var path = Path.Combine(CreateTemporaryDirectory(), "scene.glb");
        try
        {
            WriteMinimalGlb(path);
            var original = File.ReadAllBytes(path);
            Assert.True(new BinaryGlbValidator().Validate(path).IsValid);
            var malformedTail = original.Concat(new byte[4]).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(malformedTail.AsSpan(8), (uint)malformedTail.Length);
            File.WriteAllBytes(path, malformedTail);
            Assert.False(new BinaryGlbValidator().Validate(path).IsValid);
            File.WriteAllBytes(path, [0, 1, 2, 3]);
            Assert.False(new BinaryGlbValidator().Validate(path).IsValid);
            Assert.NotEmpty(original);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static BlenderGenerationRequest Request(string outputDirectory) => new()
    {
        Draft = Draft(),
        OutputDirectory = outputDirectory,
        OutputFileName = "factory.glb",
        Tool = new BlenderToolOptions
        {
            ExecutablePath = typeof(BlenderSceneGenerator).Assembly.Location,
            Timeout = TimeSpan.FromSeconds(5),
            MaximumProcessOutputCharacters = 128
        }
    };

    private static SceneDraft Draft()
    {
        var contour = Assert.IsType<CadSegmentContour>(new CadContourValidator().Validate(new CadSegmentContour("contour:000001",
        [
            Line(0, 0, 0, 0, 2, 0), Line(0, 1, 2, 0, 2, 2), Line(0, 2, 2, 2, 0, 2), Line(0, 3, 0, 2, 0, 0)
        ], true)));
        var wall = new CadWallObject("semantic:wall:001", "contour:000001", CadClassificationSubjectKind.Contour, CadBounds.Computed(0, 0, 0, 2, 2, 0), new CadRuleGeometryDefaults { HeightMeters = 3 }, contour, null, 3);
        return new SceneDraft
        {
            Id = "draft:public:001",
            SemanticObjects = [wall],
            Nodes = [new SceneNode { Id = "node:semantic:wall:001", SemanticObjectId = wall.Id, Classification = wall.Classification, ContentKind = SceneNodeContentKind.ProceduralStaticGeometry, Bounds = wall.Bounds }]
        };
    }

    private static CadLineSegment2 Line(int order, int segment, double x1, double y1, double x2, double y2) =>
        new(order, segment, "SYN", "LINE", new CadPoint3(x1, y1, 0), new CadPoint3(x2, y2, 0));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteMinimalGlb(string path)
    {
        var json = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"nodes\":[{}]}");
        var paddedLength = (json.Length + 3) & ~3;
        var bytes = new byte[20 + paddedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)paddedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4E4F534A);
        json.CopyTo(bytes, 20);
        Array.Fill(bytes, (byte)0x20, 20 + json.Length, paddedLength - json.Length);
        File.WriteAllBytes(path, bytes);
    }

    private sealed class GlbWritingRunner(string standardOutput = "SCENEBUILDER_STATUS:SUCCEEDED") : IBlenderProcessRunner
    {
        public Task<BlenderProcessResult> RunAsync(BlenderProcessRequest request, CancellationToken cancellationToken)
        {
            WriteMinimalGlb(request.Arguments[^1]);
            return Task.FromResult(new BlenderProcessResult { Status = BlenderProcessStatus.Succeeded, ExitCode = 0, StandardOutput = standardOutput });
        }
    }

    private sealed class RecordingGlbWritingRunner : IBlenderProcessRunner
    {
        public string WorkingDirectory { get; private set; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();

        public Task<BlenderProcessResult> RunAsync(BlenderProcessRequest request, CancellationToken cancellationToken)
        {
            WorkingDirectory = request.WorkingDirectory;
            Arguments = request.Arguments;
            WriteMinimalGlb(request.Arguments[^1]);
            return Task.FromResult(new BlenderProcessResult { Status = BlenderProcessStatus.Succeeded, ExitCode = 0, StandardOutput = "SCENEBUILDER_STATUS:SUCCEEDED" });
        }
    }
}
