using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public enum SceneApplicationOperation
{
    Analyze = 0,
    ValidatePlan = 1,
    FreezePlan = 2,
    Build = 3
}

public enum SceneOperationStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2,
    Cancelled = 3,
    NotConfigured = 4,
    Unsupported = 5
}

public enum SceneWorkflowPhase
{
    Analyze = 0,
    Plan = 1,
    Build = 2
}

public enum SceneArtifactKind
{
    Analysis = 0,
    PlanDraft = 1,
    FrozenPlan = 2,
    Glb = 3,
    ScenePackage = 4,
    Tileset = 5,
    Report = 6,
    Log = 7,
    PlanValidation = 8,
    BuildInputSnapshot = 9
}

public sealed record SceneOperationProgress
{
    public SceneWorkflowPhase Phase { get; init; }

    public string StageCode { get; init; } = string.Empty;

    public double? Percent { get; init; }

    public int? Current { get; init; }

    public int? Total { get; init; }
}

public sealed record SceneArtifactDescriptor
{
    public SceneArtifactKind Kind { get; init; }

    public string RelativePath { get; init; } = string.Empty;

    public bool IsValidated { get; init; }
}

public sealed record SceneOperationResult
{
    public SceneOperationStatus Status { get; init; }

    public IReadOnlyList<SceneArtifactDescriptor> Artifacts { get; init; } = Array.Empty<SceneArtifactDescriptor>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface ISceneOperationHandler<in TRequest, TResult>
{
    Task<TResult> ExecuteAsync(
        TRequest request,
        IProgress<SceneOperationProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class SceneOperationExecutor<TRequest>(ISceneOperationHandler<TRequest, SceneOperationResult> handler)
{
    private readonly ISceneOperationHandler<TRequest, SceneOperationResult> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public async Task<SceneOperationResult> ExecuteAsync(
        TRequest request,
        IProgress<SceneOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _handler.ExecuteAsync(request, progress, cancellationToken);
            return SceneOperationContractValidator.ValidateResult(result).IsValid
                ? result
                : new SceneOperationResult
                {
                    Status = SceneOperationStatus.Failed,
                    Diagnostics = [Diagnostic("SCENE_OPERATION_RESULT_INVALID", DiagnosticSeverity.Error)]
                };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SceneOperationResult
            {
                Status = SceneOperationStatus.Cancelled,
                Diagnostics = [Diagnostic("SCENE_OPERATION_CANCELLED", DiagnosticSeverity.Warning)]
            };
        }
        catch (Exception)
        {
            return new SceneOperationResult
            {
                Status = SceneOperationStatus.Failed,
                Diagnostics = [Diagnostic("SCENE_OPERATION_FAILED", DiagnosticSeverity.Error)]
            };
        }
    }

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new()
    {
        Code = code,
        Severity = severity,
        Message = "Scene operation did not complete normally."
    };
}
