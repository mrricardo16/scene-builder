using System.Text.Json.Serialization;
using SceneBuilder.Application;
using SceneBuilder.Domain;

namespace SceneBuilder.Pipeline;

public enum ScenePackageGenerationStatus { Succeeded = 0, PartiallySucceeded = 1, Failed = 2, Cancelled = 3, TimedOut = 4 }
public enum ScenePackagePartitionStatus { Succeeded = 0, Failed = 1, TimedOut = 2, Cancelled = 3 }

public sealed record ScenePackagePublicationPolicy
{
    public bool ContinueAfterPartitionFailure { get; init; } = true;
    public bool PublishPartialPackage { get; init; }
    public bool OverwriteExistingPackage { get; init; }
}

public sealed record ScenePackageGenerationRequest
{
    public SceneDraft Draft { get; init; } = new();
    public ScenePartitionPolicy PartitionPolicy { get; init; } = new();
    public string OutputRootDirectory { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public BlenderToolOptions BlenderTool { get; init; } = new();
    public BlenderAssetGenerationContext? AssetGeneration { get; init; }
    public ScenePackagePublicationPolicy PublicationPolicy { get; init; } = new();
}

public sealed record ScenePackageGenerationResult
{
    public ScenePackageGenerationStatus Status { get; init; } = ScenePackageGenerationStatus.Failed;
    public string? PackagePath { get; init; }
    public ScenePackageIndex? Index { get; init; }
    public IReadOnlyList<ScenePackagePartitionResult> Partitions { get; init; } = Array.Empty<ScenePackagePartitionResult>();
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record ScenePackagePartitionResult
{
    public string PartitionId { get; init; } = string.Empty;
    public ScenePackagePartitionStatus Status { get; init; }
    public string? ArtifactPath { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record ScenePackageIndex
{
    [JsonPropertyName("contractVersion")] public string ContractVersion { get; init; } = "1.0";
    [JsonPropertyName("unit")] public string Unit { get; init; } = "meters";
    [JsonPropertyName("sceneBounds")] public CadBounds SceneBounds { get; init; } = CadBounds.NotEvaluated;
    [JsonPropertyName("partitions")] public IReadOnlyList<ScenePackagePartitionIndex> Partitions { get; init; } = Array.Empty<ScenePackagePartitionIndex>();
    [JsonPropertyName("dynamicNodes")] public IReadOnlyList<ScenePackageDynamicNodeIndex> DynamicNodes { get; init; } = Array.Empty<ScenePackageDynamicNodeIndex>();
}

public sealed record ScenePackagePartitionIndex
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public ScenePackagePartitionStatus Status { get; init; }
    [JsonPropertyName("xIndex")] public int? XIndex { get; init; }
    [JsonPropertyName("yIndex")] public int? YIndex { get; init; }
    [JsonPropertyName("cellBounds")] public CadBounds CellBounds { get; init; } = CadBounds.NotEvaluated;
    [JsonPropertyName("contentBounds")] public CadBounds ContentBounds { get; init; } = CadBounds.NotEvaluated;
    [JsonPropertyName("artifactPath")] public string? ArtifactPath { get; init; }
    [JsonPropertyName("proceduralCount")] public int ProceduralCount { get; init; }
    [JsonPropertyName("staticAssetCount")] public int StaticAssetCount { get; init; }
    [JsonPropertyName("dynamicAssetCount")] public int DynamicAssetCount { get; init; }
}

public sealed record ScenePackageDynamicNodeIndex
{
    [JsonPropertyName("semanticObjectId")] public string SemanticObjectId { get; init; } = string.Empty;
    [JsonPropertyName("partitionId")] public string PartitionId { get; init; } = string.Empty;
    [JsonPropertyName("position")] public CadPoint3 Position { get; init; } = new(0, 0, 0);
    [JsonPropertyName("rotationDegrees")] public double RotationDegrees { get; init; }
    [JsonPropertyName("scale")] public CadScale3 Scale { get; init; } = CadScale3.Identity;
}
