using SceneBuilder.Application;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class CadImportAnalysisHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_WithPublicDxf_PublishesValidatedAnalysisArtifact()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");

        try
        {
            var result = await SceneBuilderComposition.CreateDefault().CadImportAnalysisHandler!.ExecuteAsync(
                new CadImportAnalysisRequest
                {
                    InputPath = sourcePath,
                    OutputRootDirectory = outputRoot
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Succeeded, result.Status);
            var artifact = Assert.Single(result.Artifacts.Where(item => item.Kind is SceneArtifactKind.Analysis));
            Assert.Equal(SceneArtifactKind.Analysis, artifact.Kind);
            Assert.Equal("analysis/cad-analysis.json", artifact.RelativePath);
            Assert.True(artifact.IsValidated);
            Assert.True(File.Exists(Path.Combine(outputRoot, "input", "source.dxf")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "analysis", "cad-analysis.json")));
            Assert.DoesNotContain(outputRoot, await File.ReadAllTextAsync(Path.Combine(outputRoot, "analysis", "cad-analysis.json")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithSameDxf_ProducesByteStableArtifacts()
    {
        var firstOutput = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var secondOutput = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        try
        {
            var handler = SceneBuilderComposition.CreateDefault().CadImportAnalysisHandler!;
            var first = await handler.ExecuteAsync(new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = firstOutput }, null, CancellationToken.None);
            var second = await handler.ExecuteAsync(new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = secondOutput }, null, CancellationToken.None);

            Assert.Equal(first.AnalysisId, second.AnalysisId);
            Assert.Equal(first.SourceFingerprint, second.SourceFingerprint);
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(firstOutput, "analysis", "cad-analysis.json")),
                await File.ReadAllBytesAsync(Path.Combine(secondOutput, "analysis", "cad-analysis.json")));
        }
        finally
        {
            DeleteIfExists(firstOutput);
            DeleteIfExists(secondOutput);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithDwg_ReturnsUnsupportedWithoutAnalysisArtifact()
    {
        var inputDirectory = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inputDirectory);
        var inputPath = Path.Combine(inputDirectory, "synthetic.dwg");
        await File.WriteAllBytesAsync(inputPath, [0, 1, 2, 3]);
        try
        {
            var result = await SceneBuilderComposition.CreateDefault().CadImportAnalysisHandler!.ExecuteAsync(
                new CadImportAnalysisRequest { InputPath = inputPath, OutputRootDirectory = outputRoot }, null, CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Unsupported, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "DWG_UNSUPPORTED");
            Assert.Empty(result.Artifacts);
            Assert.False(File.Exists(Path.Combine(outputRoot, "analysis", "cad-analysis.json")));
        }
        finally
        {
            DeleteIfExists(inputDirectory);
            DeleteIfExists(outputRoot);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
