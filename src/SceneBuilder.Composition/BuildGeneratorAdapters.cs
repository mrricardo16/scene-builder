using SceneBuilder.Application;
using SceneBuilder.Domain;
using SceneBuilder.Pipeline;
using SceneBuilder.Tiles;

namespace SceneBuilder.Composition;

internal sealed class ScenePackageBuildGeneratorAdapter(IBlenderSceneGenerator blender) : IScenePackageBuildGenerator
{
    private readonly ScenePackageGenerator _generator = new(blender);

    public async Task<ScenePackageBuildResult> GenerateAsync(ScenePackageBuildRequest request, CancellationToken cancellationToken)
    {
        var result = await _generator.GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = request.Draft,
            OutputRootDirectory = request.OutputRootDirectory,
            PackageName = "scene-package",
            BlenderTool = request.BlenderTool,
            AssetGeneration = request.AssetGeneration,
            PartitionPolicy = new ScenePartitionPolicy
            {
                CellSizeMeters = request.Partition.CellSizeMeters,
                OriginXMeters = request.Partition.OriginXMeters,
                OriginYMeters = request.Partition.OriginYMeters,
                MaximumIntersectedCellsPerObject = request.Partition.MaximumIntersectedCellsPerObject,
                LargeObjectBehavior = request.Partition.LargeObjectBehavior,
                InvalidBoundsBehavior = request.Partition.InvalidBoundsBehavior
            },
            PublicationPolicy = new ScenePackagePublicationPolicy
            {
                ContinueAfterPartitionFailure = request.Partition.ContinueAfterPartitionFailure,
                PublishPartialPackage = request.Partition.PublishPartialPackage
            }
        }, cancellationToken);
        return new ScenePackageBuildResult { Status = Map(result.Status), PackageDirectory = result.PackagePath, Diagnostics = result.Diagnostics };
    }

    private static SceneBuildOutputStatus Map(ScenePackageGenerationStatus status) => status switch
    {
        ScenePackageGenerationStatus.Succeeded => SceneBuildOutputStatus.Succeeded,
        ScenePackageGenerationStatus.PartiallySucceeded => SceneBuildOutputStatus.PartiallySucceeded,
        ScenePackageGenerationStatus.Cancelled => SceneBuildOutputStatus.Cancelled,
        _ => SceneBuildOutputStatus.Failed
    };
}

internal sealed class TilesetBuildGeneratorAdapter : ITilesetBuildGenerator
{
    private readonly TilesetGenerator _generator = new();

    public async Task<TilesetBuildResult> GenerateAsync(TilesetBuildRequest request, CancellationToken cancellationToken)
    {
        var result = await _generator.GenerateAsync(new TilesetGenerationRequest
        {
            ScenePackageDirectory = request.ScenePackageDirectory,
            Policy = new TilesetGenerationPolicy
            {
                RootGeometricErrorMeters = request.Configuration.RootGeometricErrorMeters,
                MinimumBoundingHalfExtentMeters = request.Configuration.MinimumBoundingHalfExtentMeters,
                AllowPartialScenePackage = request.Configuration.AllowPartialScenePackage
            }
        }, cancellationToken);
        return new TilesetBuildResult
        {
            Status = result.Status switch { TilesetGenerationStatus.Succeeded => SceneBuildOutputStatus.Succeeded, TilesetGenerationStatus.PartiallySucceeded => SceneBuildOutputStatus.PartiallySucceeded, TilesetGenerationStatus.Cancelled => SceneBuildOutputStatus.Cancelled, _ => SceneBuildOutputStatus.Failed },
            TilesetPath = result.TilesetPath,
            Diagnostics = result.Diagnostics
        };
    }
}
