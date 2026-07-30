using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record SceneOperationContractValidationResult
{
    public bool IsValid { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public static class SceneOperationContractValidator
{
    public static SceneOperationContractValidationResult ValidateProgress(SceneOperationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (string.IsNullOrWhiteSpace(progress.StageCode) || !IsUppercaseAsciiCode(progress.StageCode))
        {
            return Invalid("SCENE_PROGRESS_STAGE_CODE_INVALID");
        }

        if (progress.Percent is { } percent && (!double.IsFinite(percent) || percent < 0d || percent > 100d))
        {
            return Invalid("SCENE_PROGRESS_PERCENT_INVALID");
        }

        if (progress.Current.HasValue != progress.Total.HasValue ||
            progress.Current is { } current && progress.Total is { } total && (current < 0 || total < 0 || current > total))
        {
            return Invalid("SCENE_PROGRESS_COUNT_INVALID");
        }

        return Valid();
    }

    public static SceneOperationContractValidationResult ValidateArtifact(SceneArtifactDescriptor artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return IsControlledRelativePath(artifact.RelativePath)
            ? Valid()
            : Invalid("SCENE_ARTIFACT_PATH_INVALID");
    }

    public static SceneOperationContractValidationResult ValidateResult(SceneOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Artifacts is null || result.Diagnostics is null)
        {
            return Invalid("SCENE_OPERATION_COLLECTION_INVALID");
        }

        if (result.Artifacts.Any(artifact => !ValidateArtifact(artifact).IsValid))
        {
            return Invalid("SCENE_ARTIFACT_PATH_INVALID");
        }

        var artifactsAllowed = result.Status is SceneOperationStatus.Succeeded or SceneOperationStatus.PartiallySucceeded;
        if (!artifactsAllowed && result.Artifacts.Count > 0)
        {
            return Invalid("SCENE_OPERATION_ARTIFACT_STATUS_INVALID");
        }

        if (artifactsAllowed && result.Artifacts.Any(artifact => !artifact.IsValidated))
        {
            return Invalid("SCENE_OPERATION_ARTIFACT_VALIDATION_REQUIRED");
        }

        return Valid();
    }

    internal static bool IsUppercaseAsciiCode(string value) => !string.IsNullOrWhiteSpace(value) && value.All(character =>
        character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    internal static bool IsControlledRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains('\\') || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

        var segments = value.Split('/');
        return segments.All(segment => !string.IsNullOrWhiteSpace(segment) && segment is not "." and not "..");
    }

    private static SceneOperationContractValidationResult Valid() => new() { IsValid = true };

    private static SceneOperationContractValidationResult Invalid(string code) => new()
    {
        Diagnostics =
        [
            new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = code,
                Message = "The scene operation contract is invalid."
            }
        ]
    };
}
