using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Tiles;

public enum TilesetGenerationStatus { Succeeded = 0, PartiallySucceeded = 1, Failed = 2, Cancelled = 3 }

public sealed record TilesetGenerationPolicy
{
    public double RootGeometricErrorMeters { get; init; } = 100d;
    public double MinimumBoundingHalfExtentMeters { get; init; } = 0.001d;
    public bool AllowPartialScenePackage { get; init; } = true;
    public bool OverwriteExistingTileset { get; init; }
}

public sealed record TilesetGenerationRequest
{
    public string ScenePackageDirectory { get; init; } = string.Empty;
    public TilesetGenerationPolicy Policy { get; init; } = new();
}

public sealed record TilesetGenerationResult
{
    public TilesetGenerationStatus Status { get; init; } = TilesetGenerationStatus.Failed;
    public string? TilesetPath { get; init; }
    public int TileCount { get; init; }
    public int IncludedPartitionCount { get; init; }
    public int ExcludedPartitionCount { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record TilesetValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface ITilesetValidator
{
    Task<TilesetValidationResult> ValidateAsync(string scenePackageDirectory, string tilesetPath, CancellationToken cancellationToken);
}

internal sealed record TilesetDocument
{
    [JsonPropertyName("asset")] public TilesetAsset Asset { get; init; } = new();
    [JsonPropertyName("geometricError")] public double GeometricError { get; init; }
    [JsonPropertyName("root")] public TilesetTile Root { get; init; } = new();
}

internal sealed record TilesetAsset
{
    [JsonPropertyName("version")] public string Version { get; init; } = "1.1";
}

internal sealed record TilesetTile
{
    [JsonPropertyName("boundingVolume")] public TilesetBoundingVolume BoundingVolume { get; init; } = new();
    [JsonPropertyName("geometricError")] public double GeometricError { get; init; }
    [JsonPropertyName("refine")] public string? Refine { get; init; }
    [JsonPropertyName("content")] public TilesetContent? Content { get; init; }
    [JsonPropertyName("children")] public IReadOnlyList<TilesetTile>? Children { get; init; }
}

internal sealed record TilesetBoundingVolume
{
    [JsonPropertyName("box")] public IReadOnlyList<double> Box { get; init; } = Array.Empty<double>();
}

internal sealed record TilesetContent
{
    [JsonPropertyName("uri")] public string Uri { get; init; } = string.Empty;
}
