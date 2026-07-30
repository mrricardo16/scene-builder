using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public enum BlenderGenerationStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2,
    Cancelled = 3,
    TimedOut = 4
}

public sealed record BlenderToolOptions
{
    public string ExecutablePath { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    public int MaximumProcessOutputCharacters { get; init; } = 16_384;
}

public sealed record BlenderGenerationRequest
{
    public SceneDraft Draft { get; init; } = new();

    public BlenderToolOptions Tool { get; init; } = new();

    public string OutputDirectory { get; init; } = string.Empty;

    public string OutputFileName { get; init; } = string.Empty;

    public bool AllowOverwrite { get; init; }

    public BlenderAssetGenerationContext? AssetGeneration { get; init; }
}

public sealed record BlenderAssetGenerationContext
{
    public string AssetRootDirectory { get; init; } = string.Empty;

    public CadAssetConfiguration Configuration { get; init; } = new();

    public BlenderAssetGenerationPolicy Policy { get; init; } = new();
}

public sealed record BlenderGenerationResult
{
    public BlenderGenerationStatus Status { get; init; } = BlenderGenerationStatus.Failed;

    public string? ArtifactPath { get; init; }

    public int GeneratedObjectCount { get; init; }

    public int SkippedObjectCount { get; init; }

    public IReadOnlyList<string> SkippedSemanticObjectIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();

    public int? ProcessExitCode { get; init; }
}

public interface IBlenderSceneGenerator
{
    Task<BlenderGenerationResult> GenerateAsync(
        BlenderGenerationRequest request,
        CancellationToken cancellationToken);
}
