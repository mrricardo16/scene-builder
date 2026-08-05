using SceneBuilder.Application;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class CadBuildInputSnapshotSerializationTests
{
    [Fact]
    public async Task ExecuteAsync_WithPublicDxf_PublishesValidatedVersionedSnapshot()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");

        try
        {
            var result = await SceneBuilderComposition.CreateDefault().CadImportAnalysisHandler!.ExecuteAsync(
                new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = outputRoot },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Succeeded, result.Status);
            Assert.Equal("2.0", result.ContractVersion);
            Assert.Equal(CadBuildInputSnapshotStatus.Available, result.BuildInputSnapshot.Status);
            Assert.StartsWith("snapshot-", result.BuildInputSnapshot.SnapshotId, StringComparison.Ordinal);
            Assert.Contains(result.Artifacts, artifact => artifact.RelativePath == "analysis/build-input-snapshot.json" && artifact.IsValidated);
            var snapshotPath = Path.Combine(outputRoot, "analysis", "build-input-snapshot.json");
            Assert.True(File.Exists(snapshotPath));
            var roundTrip = await CadBuildInputSnapshotSerializer.ReadValidatedAsync(snapshotPath, CancellationToken.None);
            Assert.Equal(result.BuildInputSnapshot.SnapshotId, roundTrip.SnapshotId);
            Assert.Equal(result.BuildInputSnapshot.ContentHash, roundTrip.ContentHash);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }
}
