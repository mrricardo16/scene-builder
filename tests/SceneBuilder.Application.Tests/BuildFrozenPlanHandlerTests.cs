using SceneBuilder.Application;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class BuildFrozenPlanHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_WithLegacyFrozenPlan_RejectsBuildReadiness()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-build-tests", Guid.NewGuid().ToString("N"));
        var frozenPath = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(frozenPath)!);
            await File.WriteAllTextAsync(frozenPath, "{\"contractVersion\":\"1.0\",\"frozenPlanId\":\"frozen-legacy\"}");

            var result = await SceneBuilderComposition.CreateDefault().BuildFrozenPlanHandler!.ExecuteAsync(
                new BuildFrozenPlanRequest { FrozenPlanPath = frozenPath, OutputRootDirectory = outputRoot },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Failed, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FROZEN_PLAN_NOT_BUILD_READY");
            Assert.Empty(result.Artifacts);
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
    public async Task ExecuteAsync_WithMissingFrozenPlan_ReturnsStableFailureWithoutArtifacts()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-build-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await SceneBuilderComposition.CreateDefault().BuildFrozenPlanHandler!.ExecuteAsync(
                new BuildFrozenPlanRequest
                {
                    FrozenPlanPath = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json"),
                    OutputRootDirectory = outputRoot
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Failed, result.Status);
            Assert.Empty(result.Artifacts);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BUILD_FROZEN_PLAN_NOT_FOUND");
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }
}
