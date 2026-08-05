using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record BuildFrozenPlanRequest
{
    public string FrozenPlanPath { get; init; } = string.Empty;
    public string OutputRootDirectory { get; init; } = string.Empty;
    public string? BlenderExecutablePath { get; init; }
    public TimeSpan? BlenderTimeout { get; init; }
}

public sealed record SceneBuildResult
{
    public string ContractVersion { get; init; } = "1.0";
    public SceneOperationStatus Status { get; init; }
    public string BuildJobId { get; init; } = string.Empty;
    public string BuildContentId { get; init; } = string.Empty;
    public string FrozenPlanId { get; init; } = string.Empty;
    public IReadOnlyList<SceneArtifactDescriptor> Artifacts { get; init; } = Array.Empty<SceneArtifactDescriptor>();
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
    public IReadOnlyList<SceneBuildOutputResult> Outputs { get; init; } = Array.Empty<SceneBuildOutputResult>();
}

public enum SceneBuildOutputKind { SceneDraft = 0, SingleGlb = 1, ScenePackage = 2, ThreeDTiles = 3 }
public enum SceneBuildOutputStatus { NotRequested = 0, Succeeded = 1, PartiallySucceeded = 2, Failed = 3, SkippedDependencyFailed = 4, Cancelled = 5, NotConfigured = 6 }

public sealed record SceneBuildOutputResult
{
    public SceneBuildOutputKind Kind { get; init; }
    public SceneBuildOutputStatus Status { get; init; }
    public string? ArtifactRelativePath { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record ScenePackageBuildRequest
{
    public SceneDraft Draft { get; init; } = new();
    public string OutputRootDirectory { get; init; } = string.Empty;
    public BlenderToolOptions BlenderTool { get; init; } = new();
    public BlenderAssetGenerationContext? AssetGeneration { get; init; }
    public ConversionPlanPartitionConfiguration Partition { get; init; } = new();
}

public sealed record ScenePackageBuildResult
{
    public SceneBuildOutputStatus Status { get; init; } = SceneBuildOutputStatus.Failed;
    public string? PackageDirectory { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface IScenePackageBuildGenerator
{
    Task<ScenePackageBuildResult> GenerateAsync(ScenePackageBuildRequest request, CancellationToken cancellationToken);
}

public sealed record TilesetBuildRequest
{
    public string ScenePackageDirectory { get; init; } = string.Empty;
    public ConversionPlanTilesConfiguration Configuration { get; init; } = new();
}

public sealed record TilesetBuildResult
{
    public SceneBuildOutputStatus Status { get; init; } = SceneBuildOutputStatus.Failed;
    public string? TilesetPath { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface ITilesetBuildGenerator
{
    Task<TilesetBuildResult> GenerateAsync(TilesetBuildRequest request, CancellationToken cancellationToken);
}
