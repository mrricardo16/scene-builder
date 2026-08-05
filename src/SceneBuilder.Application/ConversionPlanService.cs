using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed class ConversionPlanService : IConversionPlanService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly IOutputRootPolicy _outputRootPolicy;
    private readonly ConversionPlanRuleSetSnapshotter _ruleSetSnapshotter;
    private readonly ConversionPlanDefaultProfileV2 _defaultProfile;
    private readonly FrozenBuildConfigurationResolver _configurationResolver;
    private readonly FrozenPlanBuildReadinessValidator _readinessValidator;
    private readonly FrozenPlanV2Serializer _frozenSerializer;

    public ConversionPlanService(
        IOutputRootPolicy outputRootPolicy,
        ConversionPlanRuleSetSnapshotter? ruleSetSnapshotter = null,
        ConversionPlanDefaultProfileV2? defaultProfile = null,
        FrozenBuildConfigurationResolver? configurationResolver = null,
        FrozenPlanBuildReadinessValidator? readinessValidator = null,
        FrozenPlanV2Serializer? frozenSerializer = null)
    {
        _outputRootPolicy = outputRootPolicy ?? throw new ArgumentNullException(nameof(outputRootPolicy));
        _ruleSetSnapshotter = ruleSetSnapshotter ?? new ConversionPlanRuleSetSnapshotter();
        _defaultProfile = defaultProfile ?? new ConversionPlanDefaultProfileV2(_ruleSetSnapshotter);
        _configurationResolver = configurationResolver ?? new FrozenBuildConfigurationResolver();
        _readinessValidator = readinessValidator ?? new FrozenPlanBuildReadinessValidator(_ruleSetSnapshotter);
        _frozenSerializer = frozenSerializer ?? new FrozenPlanV2Serializer();
    }

    public async Task<ConversionPlanDraftResult> CreateDraftAsync(CreateConversionPlanDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryRoot(request.OutputRootDirectory, out var root) || !TryControlledPath(request.AnalysisPath, root, "analysis/cad-analysis.json", out var analysisPath)) return FailedDraft("PLAN_ANALYSIS_PATH_INVALID");
            var analysis = await ReadAnalysisAsync(analysisPath, cancellationToken);
            if (analysis is null) return FailedDraft("PLAN_ANALYSIS_INVALID");
            var contractVersion = analysis.ContractVersion == "2.0" && analysis.Snapshot is { Status: CadBuildInputSnapshotStatus.Available } ? "2.0" : "1.0";
            var planId = Hash($"{analysis.AnalysisId}|{analysis.SourceFingerprint}|{contractVersion}")[..24];
            ConversionPlanDraft draft;
            if (contractVersion == "2.0")
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshotPath = Path.Combine(root, "analysis", "build-input-snapshot.json");
                var snapshot = await CadBuildInputSnapshotSerializer.ReadValidatedAsync(snapshotPath, cancellationToken);
                if (!SnapshotMatches(analysis, snapshot)) return FailedDraft("PLAN_BUILD_SNAPSHOT_MISMATCH");
                var binding = new ConversionPlanBuildInputBinding
                {
                    AnalysisContractVersion = "2.0", AnalysisId = analysis.AnalysisId, SourceFingerprint = analysis.SourceFingerprint,
                    AnalysisArtifactRelativePath = "analysis/cad-analysis.json", SnapshotContractVersion = snapshot.ContractVersion,
                    SnapshotId = snapshot.SnapshotId, SnapshotContentHash = snapshot.ContentHash, SnapshotArtifactRelativePath = "analysis/build-input-snapshot.json"
                };
                draft = FinalizeContent(_defaultProfile.Create("plan-" + planId, analysis.AnalysisId, analysis.SourceFingerprint, analysis.Unit, binding));
            }
            else
            {
                draft = FinalizeContent(new ConversionPlanDraft { PlanId = "plan-" + planId, Revision = 1, SourceAnalysisId = analysis.AnalysisId, SourceFingerprint = analysis.SourceFingerprint, SourceUnit = analysis.Unit });
            }
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
            var diagnostics = draft.ContractVersion == "2.0" ? await ValidateV2Async(draft, root, cancellationToken) : Validate(draft);
            var status = diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error) ? ConversionPlanValidationStatus.Invalid : ConversionPlanValidationStatus.Valid;
            var validation = FinalizeValidation(new ConversionPlanValidationArtifact
            {
                ContractVersion = draft.ContractVersion, PlanId = draft.PlanId, Revision = draft.Revision, PlanContentId = draft.PlanContentId,
                AnalysisId = draft.SourceAnalysisId, SnapshotId = draft.BuildInput?.SnapshotId, SnapshotContentHash = draft.BuildInput?.SnapshotContentHash,
                ValidationStatus = status, Diagnostics = diagnostics
            });
            var relative = ValidationRelativePath(draft.Revision);
            await PublishAsync(root, relative, validation, cancellationToken, allowExistingSameContent: true);
            return new ConversionPlanValidationResult { ContractVersion = draft.ContractVersion, Status = status is ConversionPlanValidationStatus.Valid ? SceneOperationStatus.Succeeded : SceneOperationStatus.Failed, PlanId = draft.PlanId, Revision = draft.Revision, ValidationStatus = status, PlanContentId = draft.PlanContentId, ValidationContentHash = validation.ValidationContentHash, SnapshotId = validation.SnapshotId, SnapshotContentHash = validation.SnapshotContentHash, Artifacts = [Artifact(SceneArtifactKind.PlanValidation, relative)], Diagnostics = diagnostics };
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
            var relative = $"plans/frozen/revision-{draft.Revision:D4}.json";
            if (draft.ContractVersion == "1.0")
            {
                var legacy = new FrozenConversionPlan { FrozenPlanId = "frozen-" + Hash(draft.PlanId + "|" + draft.PlanContentId)[..24], Draft = Copy(draft) };
                await PublishAsync(root, relative, legacy, cancellationToken, allowExistingSameContent: true);
                return new FrozenConversionPlanResult { Status = SceneOperationStatus.Succeeded, FrozenPlan = legacy, Artifacts = [Artifact(SceneArtifactKind.FrozenPlan, relative)], BuildReadiness = FrozenPlanBuildReadinessStatus.NotReady };
            }
            if (validation.ContractVersion != "2.0" || validation.ValidationContentHash != ComputeValidationHash(validation)) return FailedFrozen("PLAN_VALIDATION_REQUIRED");
            var diagnostics = await ValidateV2Async(draft, root, cancellationToken);
            if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)) return FailedFrozen("PLAN_VALIDATION_STALE");
            var snapshot = await CadBuildInputSnapshotSerializer.ReadValidatedAsync(Path.Combine(root, "analysis", "build-input-snapshot.json"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var frozen = new FrozenConversionPlan
            {
                ContractVersion = "2.0",
                Identity = new FrozenPlanIdentity { PlanId = draft.PlanId, Revision = draft.Revision, DraftContentHash = draft.PlanContentId, ValidationContentHash = validation.ValidationContentHash, ValidationArtifactRelativePath = ValidationRelativePath(draft.Revision) },
                BuildInput = draft.BuildInput! with { },
                BuildConfiguration = _configurationResolver.Resolve(draft, snapshot)
            };
            var contentHash = FrozenPlanCanonicalHasher.Compute(frozen);
            frozen = frozen with { FrozenPlanContentHash = contentHash, FrozenPlanId = "frozen-plan-" + contentHash };
            var readiness = await PublishFrozenV2Async(root, relative, frozen, cancellationToken);
            if (readiness.Status != FrozenPlanBuildReadinessStatus.Ready) return new FrozenConversionPlanResult { Status = SceneOperationStatus.Failed, FrozenPlan = frozen, Diagnostics = readiness.Diagnostics, BuildReadiness = readiness.Status };
            return new FrozenConversionPlanResult { Status = SceneOperationStatus.Succeeded, FrozenPlan = frozen, Artifacts = [Artifact(SceneArtifactKind.FrozenPlan, relative)], BuildReadiness = readiness.Status };
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

    private async Task<IReadOnlyList<SceneDiagnostic>> ValidateV2Async(ConversionPlanDraft draft, string root, CancellationToken cancellationToken)
    {
        var diagnostics = new List<SceneDiagnostic>(Validate(draft).Where(item => item.Code != "PLAN_TILES_PACKAGE_REQUIRED"));
        if (draft.BuildInput is null || draft.RuleSet is null || draft.Assets is null || draft.Partition is null || draft.Tiles is null) diagnostics.Add(Diagnostic("PLAN_BUILD_CONFIGURATION_MISSING", DiagnosticSeverity.Error));
        if (draft.BuildInput is null) return Sort(diagnostics.Append(Diagnostic("PLAN_BUILD_SNAPSHOT_MISSING", DiagnosticSeverity.Error)));
        try
        {
            var analysis = await ReadAnalysisAsync(Path.Combine(root, "analysis", "cad-analysis.json"), cancellationToken);
            var snapshot = await CadBuildInputSnapshotSerializer.ReadValidatedAsync(Path.Combine(root, "analysis", "build-input-snapshot.json"), cancellationToken);
            if (analysis is null || !SnapshotMatches(analysis, snapshot) || draft.BuildInput.AnalysisContractVersion != "2.0" || draft.BuildInput.AnalysisId != analysis.AnalysisId || draft.BuildInput.SourceFingerprint != analysis.SourceFingerprint || draft.BuildInput.AnalysisArtifactRelativePath != "analysis/cad-analysis.json" || draft.BuildInput.SnapshotContractVersion != "1.0" || draft.BuildInput.SnapshotId != snapshot.SnapshotId || draft.BuildInput.SnapshotContentHash != snapshot.ContentHash || draft.BuildInput.SnapshotArtifactRelativePath != "analysis/build-input-snapshot.json") diagnostics.Add(Diagnostic("PLAN_BUILD_SNAPSHOT_MISMATCH", DiagnosticSeverity.Error));
            if (draft.Repair.EnabledActionIds.Any(id => snapshot.RepairCandidates.All(candidate => candidate.RepairActionId != id))) diagnostics.Add(Diagnostic("PLAN_REPAIR_INVALID", DiagnosticSeverity.Error));
            if (draft.RuleSet is null || !_ruleSetSnapshotter.IsValid(draft.RuleSet, snapshot.AnalyzeTimeClassifications.Count == 0)) diagnostics.Add(Diagnostic("PLAN_RULE_SNAPSHOT_INVALID", DiagnosticSeverity.Error));
            else if (draft.RuleSet.RuleSet.Rules.Count > 0 && new CadRuleEngine().Classify(draft.RuleSet.RuleSet, snapshot.ClassificationSubjects.Select(value => value.Subject).ToArray()).Diagnostics.Any(item => item.Code == "RULE_CONFLICT")) diagnostics.Add(Diagnostic("PLAN_RULE_CONFLICT", DiagnosticSeverity.Error));
            if (draft.Assets is null || !await FrozenPlanBuildReadinessValidator.ValidAssetsAsync(draft.Assets, root, snapshot, cancellationToken)) diagnostics.Add(Diagnostic("PLAN_ASSET_BINDING_INVALID", DiagnosticSeverity.Error));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { diagnostics.Add(Diagnostic("PLAN_BUILD_SNAPSHOT_MISMATCH", DiagnosticSeverity.Error)); }
        if (!double.IsFinite(draft.InputInterpretation.LocalOriginXMeters) || !double.IsFinite(draft.InputInterpretation.LocalOriginYMeters) || !double.IsFinite(draft.InputInterpretation.LocalOriginZMeters) || draft.InputInterpretation.YawDegrees is < -360d or > 360d) diagnostics.Add(Diagnostic("PLAN_INPUT_INTERPRETATION_INVALID", DiagnosticSeverity.Error));
        if (draft.Partition is null || !double.IsFinite(draft.Partition.CellSizeMeters) || draft.Partition.CellSizeMeters <= 0 || !double.IsFinite(draft.Partition.OriginXMeters) || !double.IsFinite(draft.Partition.OriginYMeters) || draft.Partition.MaximumIntersectedCellsPerObject <= 0 || !Enum.IsDefined(draft.Partition.LargeObjectBehavior) || !Enum.IsDefined(draft.Partition.InvalidBoundsBehavior)) diagnostics.Add(Diagnostic("PLAN_PARTITION_INVALID", DiagnosticSeverity.Error));
        if (draft.Tiles is null || !double.IsFinite(draft.Tiles.RootGeometricErrorMeters) || draft.Tiles.RootGeometricErrorMeters <= 0 || !double.IsFinite(draft.Tiles.MinimumBoundingHalfExtentMeters) || draft.Tiles.MinimumBoundingHalfExtentMeters <= 0 || draft.Tiles.Refine != "ADD" || draft.Tiles.CoordinateMode != "localCartesian" || draft.Tiles.Unit != "meters" || draft.Tiles.UpAxis != "zUp" || draft.Tiles.ContentUriStrategy != "scenePackagePartitionGlb") diagnostics.Add(Diagnostic("PLAN_TILES_INVALID", DiagnosticSeverity.Error));
        return Sort(diagnostics);
    }

    private static ConversionPlanDraft FinalizeContent(ConversionPlanDraft draft)
    {
        var canonical = draft with { PlanContentId = string.Empty, ValidationStatus = ConversionPlanValidationStatus.NotValidated, Repair = draft.Repair with { EnabledActionIds = draft.Repair.EnabledActionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray() } };
        return canonical with { PlanContentId = "plan-content-" + Hash(JsonSerializer.Serialize(canonical, JsonOptions))[..24] };
    }

    private static ConversionPlanDraft Copy(ConversionPlanDraft draft) => draft with
    {
        Repair = draft.Repair with { EnabledActionIds = draft.Repair.EnabledActionIds.ToArray() },
        RuleSet = draft.RuleSet is null ? null : draft.RuleSet with { RuleSet = draft.RuleSet.RuleSet with { Rules = draft.RuleSet.RuleSet.Rules.ToArray() } },
        Assets = draft.Assets is null ? null : draft.Assets with { Catalog = draft.Assets.Catalog.ToArray(), Bindings = draft.Assets.Bindings.ToArray() }
    };
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
        if (root.ValueKind is not JsonValueKind.Object) return null;
        var version = root.GetProperty("contractVersion").GetString();
        if (version is not ("1.0" or "2.0") || root.GetProperty("analysisId").GetString() is not { Length: > 0 } id || root.GetProperty("sourceFingerprint").GetString() is not { Length: > 0 } fingerprint || root.GetProperty("status").GetString() is not "succeeded" || root.GetProperty("artifacts").ValueKind is not JsonValueKind.Array) return null;
        var artifact = root.GetProperty("artifacts").EnumerateArray().FirstOrDefault(item => item.ValueKind is JsonValueKind.Object && item.GetProperty("kind").GetString() == "analysis");
        if (artifact.ValueKind is not JsonValueKind.Object || artifact.GetProperty("relativePath").GetString() != "analysis/cad-analysis.json" || !artifact.GetProperty("isValidated").GetBoolean()) return null;
        var unit = root.GetProperty("input").GetProperty("unit").GetString();
        if (!Enum.TryParse<CadUnit>(unit, true, out var sourceUnit)) return null;
        CadBuildInputSnapshotDescriptor? descriptor = null;
        if (version == "2.0" && root.TryGetProperty("buildInputSnapshot", out var descriptorJson)) descriptor = descriptorJson.Deserialize<CadBuildInputSnapshotDescriptor>(JsonOptions);
        return new AnalysisIdentity(version, id, fingerprint, sourceUnit, descriptor);
    }

    private static async Task<ConversionPlanDraft?> ReadDraftAsync(string path, CancellationToken token)
    {
        var draft = JsonSerializer.Deserialize<ConversionPlanDraft>(await File.ReadAllTextAsync(path, Utf8, token), JsonOptions);
        if (draft is null || draft.ContractVersion is not ("1.0" or "2.0") || string.IsNullOrWhiteSpace(draft.PlanContentId)) return null;
        return draft.ContractVersion == "1.0" || FinalizeContent(draft).PlanContentId == draft.PlanContentId ? Copy(draft) : null;
    }

    private static async Task<ConversionPlanValidationArtifact?> ReadValidationAsync(string path, CancellationToken token) => File.Exists(path) ? JsonSerializer.Deserialize<ConversionPlanValidationArtifact>(await File.ReadAllTextAsync(path, Utf8, token), JsonOptions) : null;

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

    private static bool SnapshotMatches(AnalysisIdentity analysis, CadBuildInputSnapshot snapshot) => analysis.Snapshot is { Status: CadBuildInputSnapshotStatus.Available, ContractVersion: "1.0" } descriptor && descriptor.SnapshotId == snapshot.SnapshotId && descriptor.ContentHash == snapshot.ContentHash && descriptor.RelativePath == "analysis/build-input-snapshot.json" && snapshot.AnalysisId == analysis.AnalysisId && snapshot.SourceFingerprint == analysis.SourceFingerprint;

    private static ConversionPlanValidationArtifact FinalizeValidation(ConversionPlanValidationArtifact document) => document with { ValidationContentHash = ConversionPlanValidationArtifactHasher.Compute(document) };
    private static string ComputeValidationHash(ConversionPlanValidationArtifact document) => ConversionPlanValidationArtifactHasher.Compute(document);
    private static IReadOnlyList<SceneDiagnostic> Sort(IEnumerable<SceneDiagnostic> diagnostics) => diagnostics.GroupBy(item => item.Code, StringComparer.Ordinal).Select(group => group.First()).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();

    private async Task<FrozenPlanBuildReadinessResult> PublishFrozenV2Async(string root, string relative, FrozenConversionPlan frozen, CancellationToken token)
    {
        var destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var json = JsonSerializer.Serialize(frozen, BuildReadyPlanJson.Options);
        if (File.Exists(destination))
        {
            if (await File.ReadAllTextAsync(destination, BuildReadyPlanJson.Utf8, token) != json) return new FrozenPlanBuildReadinessResult { Diagnostics = [Diagnostic("PLAN_FROZEN_CONFLICT", DiagnosticSeverity.Error)] };
            var existing = await _frozenSerializer.ReadValidatedAsync(destination, token);
            return await _readinessValidator.ValidateAsync(existing, root, token);
        }
        var staging = destination + ".staging";
        try
        {
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(staging, json, BuildReadyPlanJson.Utf8, token);
            var roundTrip = await _frozenSerializer.ReadValidatedAsync(staging, token);
            var readiness = await _readinessValidator.ValidateAsync(roundTrip, root, token);
            if (readiness.Status != FrozenPlanBuildReadinessStatus.Ready) return readiness;
            token.ThrowIfCancellationRequested();
            File.Move(staging, destination, false);
            return readiness;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private sealed record AnalysisIdentity(string ContractVersion, string AnalysisId, string SourceFingerprint, CadUnit Unit, CadBuildInputSnapshotDescriptor? Snapshot);
}
