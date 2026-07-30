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
}
