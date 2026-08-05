using System.Text;
using System.Text.Json;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed class BuildFrozenPlanHandler : ISceneOperationHandler<BuildFrozenPlanRequest, SceneBuildResult>
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly IOutputRootPolicy _outputRootPolicy;
    private readonly FrozenPlanV2Serializer _serializer;
    private readonly FrozenPlanBuildReadinessValidator _readinessValidator;

    public BuildFrozenPlanHandler(IOutputRootPolicy outputRootPolicy, FrozenPlanV2Serializer? serializer = null, FrozenPlanBuildReadinessValidator? readinessValidator = null)
    {
        _outputRootPolicy = outputRootPolicy ?? throw new ArgumentNullException(nameof(outputRootPolicy));
        _serializer = serializer ?? new FrozenPlanV2Serializer();
        _readinessValidator = readinessValidator ?? new FrozenPlanBuildReadinessValidator(new ConversionPlanRuleSetSnapshotter());
    }

    public async Task<SceneBuildResult> ExecuteAsync(BuildFrozenPlanRequest request, IProgress<SceneOperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            Report(progress, "BUILD_VALIDATE_REQUEST");
            cancellationToken.ThrowIfCancellationRequested();
            var rootValidation = _outputRootPolicy.Validate(request.OutputRootDirectory);
            if (!rootValidation.IsValid || rootValidation.NormalizedPath is null || !TryFrozenPath(request.FrozenPlanPath, rootValidation.NormalizedPath, out var frozenPath)) return Failed("BUILD_FROZEN_PLAN_NOT_FOUND");
            Report(progress, "BUILD_READ_FROZEN_PLAN");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(frozenPath, Utf8, cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || root.GetProperty("contractVersion").GetString() != "2.0" || root.GetProperty("frozenPlanId").GetString() is not { Length: > 0 }) return Failed("FROZEN_PLAN_NOT_BUILD_READY");
            if (!root.TryGetProperty("buildInput", out _)) return Failed("FROZEN_PLAN_BUILD_SNAPSHOT_MISSING");
            if (!root.TryGetProperty("buildConfiguration", out _)) return Failed("FROZEN_PLAN_BUILD_CONFIGURATION_MISSING");
            var plan = await _serializer.ReadValidatedAsync(frozenPath, cancellationToken);
            var readiness = await _readinessValidator.ValidateAsync(plan, rootValidation.NormalizedPath, cancellationToken);
            return readiness.Status is FrozenPlanBuildReadinessStatus.Ready ? Failed("BUILD_NOT_IMPLEMENTED") : new SceneBuildResult { Status = SceneOperationStatus.Failed, Diagnostics = readiness.Diagnostics };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SceneBuildResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("BUILD_CANCELLED", DiagnosticSeverity.Warning)] };
        }
        catch (Exception)
        {
            return Failed("BUILD_FROZEN_PLAN_INVALID");
        }
    }

    private static bool TryFrozenPath(string candidate, string outputRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) || candidate.Contains("://", StringComparison.Ordinal) || candidate.StartsWith("\\\\", StringComparison.Ordinal)) return false;
        var normalized = Path.GetFullPath(candidate);
        var frozenRoot = Path.GetFullPath(Path.Combine(outputRoot, "plans", "frozen")) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(frozenRoot, StringComparison.OrdinalIgnoreCase) || !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(normalized)) return false;
        path = normalized;
        return true;
    }

    private static SceneBuildResult Failed(string code) => new() { Status = SceneOperationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Frozen plan build did not complete normally." };
    private static void Report(IProgress<SceneOperationProgress>? progress, string stageCode) => progress?.Report(new SceneOperationProgress { Phase = SceneWorkflowPhase.Build, StageCode = stageCode });
}
