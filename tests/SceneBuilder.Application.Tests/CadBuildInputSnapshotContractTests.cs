using SceneBuilder.Application;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class CadBuildInputSnapshotContractTests
{
    [Theory]
    [InlineData("../snapshot.json")]
    [InlineData("C:\\outside.json")]
    [InlineData("\\\\server\\share\\snapshot.json")]
    [InlineData("file://snapshot.json")]
    [InlineData("http://example.test/snapshot.json")]
    public void Validate_RejectsUnsafeSnapshotDescriptorPaths(string relativePath)
    {
        var descriptor = new CadBuildInputSnapshotDescriptor
        {
            Status = CadBuildInputSnapshotStatus.Available,
            ContractVersion = "1.0",
            SnapshotId = "snapshot-test",
            ContentHash = "test",
            RelativePath = relativePath
        };

        Assert.Throws<InvalidDataException>(() => CadBuildInputSnapshotDescriptorValidator.Validate(descriptor));
    }

    [Fact]
    public async Task ExecuteAsync_WithSameDxf_ProducesByteStableSnapshotAndV2PlanInput()
    {
        var firstOutput = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var secondOutput = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        try
        {
            var handler = SceneBuilderComposition.CreateDefault().CadImportAnalysisHandler!;
            var first = await handler.ExecuteAsync(new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = firstOutput }, null, CancellationToken.None);
            var second = await handler.ExecuteAsync(new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = secondOutput }, null, CancellationToken.None);

            Assert.Equal(first.BuildInputSnapshot.SnapshotId, second.BuildInputSnapshot.SnapshotId);
            Assert.Equal(first.BuildInputSnapshot.ContentHash, second.BuildInputSnapshot.ContentHash);
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(firstOutput, "analysis", "build-input-snapshot.json")),
                await File.ReadAllBytesAsync(Path.Combine(secondOutput, "analysis", "build-input-snapshot.json")));

            var plan = await SceneBuilderComposition.CreateDefault().ConversionPlanService!.CreateDraftAsync(
                new CreateConversionPlanDraftRequest { AnalysisPath = Path.Combine(firstOutput, "analysis", "cad-analysis.json"), OutputRootDirectory = firstOutput },
                CancellationToken.None);
            Assert.Equal(SceneOperationStatus.Succeeded, plan.Status);
        }
        finally
        {
            DeleteIfExists(firstOutput);
            DeleteIfExists(secondOutput);
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
