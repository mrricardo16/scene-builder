using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed class ConversionPlanService(IOutputRootPolicy outputRootPolicy) : IConversionPlanService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly IOutputRootPolicy _outputRootPolicy = outputRootPolicy ?? throw new ArgumentNullException(nameof(outputRootPolicy));

    public async Task<ConversionPlanDraftResult> CreateDraftAsync(CreateConversionPlanDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryRoot(request.OutputRootDirectory, out var root) || !TryControlledPath(request.AnalysisPath, root, "analysis/cad-analysis.json", out var analysisPath)) return FailedDraft("PLAN_ANALYSIS_PATH_INVALID");
            var analysis = await ReadAnalysisAsync(analysisPath, cancellationToken);
            if (analysis is null) return FailedDraft("PLAN_ANALYSIS_INVALID");
            var planId = Hash($"{analysis.AnalysisId}|{analysis.SourceFingerprint}|1.0")[..24];
            var draft = FinalizeContent(new ConversionPlanDraft
            {
                PlanId = "plan-" + planId,
                Revision = 1,
                SourceAnalysisId = analysis.AnalysisId,
                SourceFingerprint = analysis.SourceFingerprint,
                SourceUnit = analysis.Unit
            });
            var relative = DraftRelativePath(draft.Revision);
            if (File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))) return FailedDraft("PLAN_REVISION_EXISTS");
            await PublishAsync(root, relative, draft, cancellationToken);
            return new ConversionPlanDraftResult { Status = SceneOperationStatus.Succeeded, Draft = draft, Artifacts = [Artifact(SceneArtifactKind.PlanDraft, relative)] };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new ConversionPlanDraftResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("PLAN_CANCELLED", DiagnosticSeverity.Warning)] }; }
        catch (Exception) { return FailedDraft("PLAN_CREATE_FAILED"); }
    }

    public async Task<ConversionPlanDraftResult> SaveRevisionAsync(SaveConversionPlanRevisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            if (!TryRoot(request.OutputRootDirectory, out var root) || !TryPlanPath(request.PreviousPlanPath, root, out var previousPath)) return FailedDraft("PLAN_PATH_INVALID");
            var previous = await ReadDraftAsync(previousPath, cancellationToken);
            if (previous is null || !SameIdentity(previous, request.Draft)) return FailedDraft("PLAN_IDENTITY_CHANGED");
            var candidate = FinalizeContent(request.Draft with { Revision = previous.Revision + 1, ValidationStatus = ConversionPlanValidationStatus.NotValidated });
            if (candidate.PlanContentId == previous.PlanContentId) return new ConversionPlanDraftResult { Status = SceneOperationStatus.NotConfigured, Draft = previous, Diagnostics = [Diagnostic("PLAN_NO_CHANGE", DiagnosticSeverity.Information)] };
            var relative = DraftRelativePath(candidate.Revision);
            if (File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))) return FailedDraft("PLAN_REVISION_EXISTS");
            await PublishAsync(root, relative, candidate, cancellationToken);
            return new ConversionPlanDraftResult { Status = SceneOperationStatus.Succeeded, Draft = candidate, Artifacts = [Artifact(SceneArtifactKind.PlanDraft, relative)] };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new ConversionPlanDraftResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("PLAN_CANCELLED", DiagnosticSeverity.Warning)] }; }
        catch (Exception) { return FailedDraft("PLAN_SAVE_FAILED"); }
    }

    public async Task<ConversionPlanValidationResult> ValidateAsync(ValidateConversionPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            if (!TryRoot(request.OutputRootDirectory, out var root) || !TryPlanPath(request.PlanPath, root, out var path)) return FailedValidation("PLAN_PATH_INVALID");
            var draft = await ReadDraftAsync(path, cancellationToken);
            if (draft is null) return FailedValidation("PLAN_JSON_INVALID");
            var diagnostics = Validate(draft);
            var status = diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error) ? ConversionPlanValidationStatus.Invalid : ConversionPlanValidationStatus.Valid;
            var validation = new PlanValidationDocument { PlanId = draft.PlanId, Revision = draft.Revision, PlanContentId = draft.PlanContentId, ValidationStatus = status, Diagnostics = diagnostics };
            var relative = ValidationRelativePath(draft.Revision);
            await PublishAsync(root, relative, validation, cancellationToken, allowExistingSameContent: true);
            return new ConversionPlanValidationResult { Status = status is ConversionPlanValidationStatus.Valid ? SceneOperationStatus.Succeeded : SceneOperationStatus.Failed, PlanId = draft.PlanId, Revision = draft.Revision, ValidationStatus = status, PlanContentId = draft.PlanContentId, Artifacts = [Artifact(SceneArtifactKind.PlanValidation, relative)], Diagnostics = diagnostics };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new ConversionPlanValidationResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("PLAN_CANCELLED", DiagnosticSeverity.Warning)] }; }
        catch (Exception) { return FailedValidation("PLAN_VALIDATE_FAILED"); }
    }

    public async Task<FrozenConversionPlanResult> FreezeAsync(FreezeConversionPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            if (!TryRoot(request.OutputRootDirectory, out var root) || !TryPlanPath(request.PlanPath, root, out var path)) return FailedFrozen("PLAN_PATH_INVALID");
            var draft = await ReadDraftAsync(path, cancellationToken);
            if (draft is null) return FailedFrozen("PLAN_JSON_INVALID");
            var validationPath = Path.Combine(root, ValidationRelativePath(draft.Revision).Replace('/', Path.DirectorySeparatorChar));
            var validation = await ReadValidationAsync(validationPath, cancellationToken);
            if (validation is null || validation.ValidationStatus is not ConversionPlanValidationStatus.Valid || validation.PlanContentId != draft.PlanContentId || validation.PlanId != draft.PlanId) return FailedFrozen("PLAN_VALIDATION_REQUIRED");
            var frozen = new FrozenConversionPlan { FrozenPlanId = "frozen-" + Hash(draft.PlanId + "|" + draft.PlanContentId)[..24], Draft = Copy(draft) };
            var relative = $"plans/frozen/revision-{draft.Revision:D4}.json";
            await PublishAsync(root, relative, frozen, cancellationToken, allowExistingSameContent: true);
            return new FrozenConversionPlanResult { Status = SceneOperationStatus.Succeeded, FrozenPlan = frozen, Artifacts = [Artifact(SceneArtifactKind.FrozenPlan, relative)] };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new FrozenConversionPlanResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("PLAN_CANCELLED", DiagnosticSeverity.Warning)] }; }
        catch (Exception) { return FailedFrozen("PLAN_FREEZE_FAILED"); }
    }

    private static IReadOnlyList<SceneDiagnostic> Validate(ConversionPlanDraft draft)
    {
        var diagnostics = new List<SceneDiagnostic>();
        if (string.IsNullOrWhiteSpace(draft.PlanId) || string.IsNullOrWhiteSpace(draft.PlanContentId) || draft.Revision < 1 || string.IsNullOrWhiteSpace(draft.SourceAnalysisId) || string.IsNullOrWhiteSpace(draft.SourceFingerprint)) diagnostics.Add(Diagnostic("PLAN_IDENTITY_INVALID", DiagnosticSeverity.Error));
        if (draft.SourceUnit is CadUnit.Unknown or CadUnit.Unitless && draft.InputInterpretation.UnitConfirmation is ConversionPlanUnitConfirmation.UseSourceUnit) diagnostics.Add(Diagnostic("PLAN_UNIT_CONFIRMATION_REQUIRED", DiagnosticSeverity.Error));
        if (!double.IsFinite(draft.InputInterpretation.ZOffsetMeters) || !double.IsFinite(draft.InputInterpretation.YawDegrees) || !double.IsFinite(draft.Geometry.WallHeightMeters ?? 1d) || !double.IsFinite(draft.Geometry.ColumnHeightMeters ?? 1d) || draft.Geometry.WallHeightMeters is <= 0d || draft.Geometry.ColumnHeightMeters is <= 0d) diagnostics.Add(Diagnostic("PLAN_GEOMETRY_INVALID", DiagnosticSeverity.Error));
        if (draft.Repair.EnabledActionIds.Any(string.IsNullOrWhiteSpace) || draft.Repair.EnabledActionIds.Distinct(StringComparer.Ordinal).Count() != draft.Repair.EnabledActionIds.Count) diagnostics.Add(Diagnostic("PLAN_REPAIR_INVALID", DiagnosticSeverity.Error));
        if (!draft.Outputs.GenerateSingleGlb && !draft.Outputs.GenerateScenePackage && !draft.Outputs.Generate3DTiles) diagnostics.Add(Diagnostic("PLAN_OUTPUT_REQUIRED", DiagnosticSeverity.Error));
        if (draft.Outputs.Generate3DTiles && !draft.Outputs.GenerateScenePackage) diagnostics.Add(Diagnostic("PLAN_TILES_PACKAGE_REQUIRED", DiagnosticSeverity.Error));
        return diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
    }

    private static ConversionPlanDraft FinalizeContent(ConversionPlanDraft draft)
    {
        var canonical = draft with { PlanContentId = string.Empty, ValidationStatus = ConversionPlanValidationStatus.NotValidated, Repair = draft.Repair with { EnabledActionIds = draft.Repair.EnabledActionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray() } };
        return canonical with { PlanContentId = "plan-content-" + Hash(JsonSerializer.Serialize(canonical, JsonOptions))[..24] };
    }

    private static ConversionPlanDraft Copy(ConversionPlanDraft draft) => draft with { Repair = draft.Repair with { EnabledActionIds = draft.Repair.EnabledActionIds.ToArray() } };
    private static bool SameIdentity(ConversionPlanDraft left, ConversionPlanDraft right) => left.ContractVersion == right.ContractVersion && left.PlanId == right.PlanId && left.SourceAnalysisId == right.SourceAnalysisId && left.SourceFingerprint == right.SourceFingerprint;
    private static string DraftRelativePath(int revision) => $"plans/revision-{revision:D4}/plan-draft.json";
    private static string ValidationRelativePath(int revision) => $"plans/revision-{revision:D4}/validation.json";
    private static SceneArtifactDescriptor Artifact(SceneArtifactKind kind, string path) => new() { Kind = kind, RelativePath = path, IsValidated = true };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(value))).ToLowerInvariant();
    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Conversion plan did not complete normally." };
    private static ConversionPlanDraftResult FailedDraft(string code) => new() { Status = SceneOperationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static ConversionPlanValidationResult FailedValidation(string code) => new() { Status = SceneOperationStatus.Failed, ValidationStatus = ConversionPlanValidationStatus.Invalid, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static FrozenConversionPlanResult FailedFrozen(string code) => new() { Status = SceneOperationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };

    private bool TryRoot(string candidate, out string root)
    {
        root = string.Empty;
        var result = _outputRootPolicy.Validate(candidate);
        if (!result.IsValid || result.NormalizedPath is null) return false;
        root = result.NormalizedPath;
        return true;
    }

    private static bool TryControlledPath(string candidate, string root, string expectedRelativePath, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains("://", StringComparison.Ordinal) || candidate.StartsWith("\\\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(candidate)) return false;
        var normalized = Path.GetFullPath(candidate);
        var expected = Path.GetFullPath(Path.Combine(root, expectedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(normalized)) return false;
        path = normalized;
        return true;
    }

    private static bool TryPlanPath(string candidate, string root, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate)) return false;
        var normalized = Path.GetFullPath(candidate);
        var prefix = Path.GetFullPath(Path.Combine(root, "plans")) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !normalized.EndsWith("plan-draft.json", StringComparison.OrdinalIgnoreCase) || !File.Exists(normalized)) return false;
        path = normalized;
        return true;
    }

    private static async Task<AnalysisIdentity?> ReadAnalysisAsync(string path, CancellationToken token)
    {
        var text = await File.ReadAllTextAsync(path, Utf8, token);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind is not JsonValueKind.Object || root.GetProperty("contractVersion").GetString() != "1.0" || root.GetProperty("analysisId").GetString() is not { Length: > 0 } id || root.GetProperty("sourceFingerprint").GetString() is not { Length: > 0 } fingerprint || root.GetProperty("status").GetString() is not "succeeded" || root.GetProperty("artifacts").ValueKind is not JsonValueKind.Array) return null;
        var artifact = root.GetProperty("artifacts").EnumerateArray().SingleOrDefault();
        if (artifact.ValueKind is not JsonValueKind.Object || artifact.GetProperty("kind").GetString() != "analysis" || artifact.GetProperty("relativePath").GetString() != "analysis/cad-analysis.json" || !artifact.GetProperty("isValidated").GetBoolean()) return null;
        var unit = root.GetProperty("input").GetProperty("unit").GetString();
        if (!Enum.TryParse<CadUnit>(unit, true, out var sourceUnit)) return null;
        return new AnalysisIdentity(id, fingerprint, sourceUnit);
    }

    private static async Task<ConversionPlanDraft?> ReadDraftAsync(string path, CancellationToken token)
    {
        var draft = JsonSerializer.Deserialize<ConversionPlanDraft>(await File.ReadAllTextAsync(path, Utf8, token), JsonOptions);
        return draft is null || draft.ContractVersion != "1.0" || string.IsNullOrWhiteSpace(draft.PlanContentId) ? null : Copy(draft);
    }

    private static async Task<PlanValidationDocument?> ReadValidationAsync(string path, CancellationToken token) => File.Exists(path) ? JsonSerializer.Deserialize<PlanValidationDocument>(await File.ReadAllTextAsync(path, Utf8, token), JsonOptions) : null;

    private static async Task PublishAsync<T>(string root, string relative, T value, CancellationToken token, bool allowExistingSameContent = false)
    {
        var destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (File.Exists(destination))
        {
            if (allowExistingSameContent && string.Equals(await File.ReadAllTextAsync(destination, Utf8, token), json, StringComparison.Ordinal)) return;
            throw new IOException("The plan artifact already exists.");
        }
        var staging = destination + ".staging";
        try
        {
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(staging, json, Utf8, token);
            using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(staging, Utf8, token));
            token.ThrowIfCancellationRequested();
            File.Move(staging, destination, false);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private sealed record AnalysisIdentity(string AnalysisId, string SourceFingerprint, CadUnit Unit);
    private sealed record PlanValidationDocument
    {
        public string ContractVersion { get; init; } = "1.0";
        public string PlanId { get; init; } = string.Empty;
        public int Revision { get; init; }
        public string PlanContentId { get; init; } = string.Empty;
        public ConversionPlanValidationStatus ValidationStatus { get; init; }
        public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
    }
}
