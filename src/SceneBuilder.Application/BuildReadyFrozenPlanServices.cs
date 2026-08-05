using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public static class BuildReadyPlanJson
{
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Sha256(string value) => Sha256(Utf8.GetBytes(value));
    public static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));
}

public static class ConversionPlanValidationArtifactHasher
{
    public static string Compute(ConversionPlanValidationArtifact artifact) =>
        BuildReadyPlanJson.Sha256(JsonSerializer.Serialize(artifact with { ValidationContentHash = string.Empty }, BuildReadyPlanJson.Options));
}

public sealed class ConversionPlanRuleSetSnapshotter
{
    public ConversionPlanRuleSetSnapshot Create(CadRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        var normalized = ruleSet with
        {
            Rules = ruleSet.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => rule with { Match = rule.Match with { EntityTypes = rule.Match.EntityTypes.OrderBy(value => value, StringComparer.Ordinal).ToArray() } })
                .ToArray()
        };
        var hash = BuildReadyPlanJson.Sha256(JsonSerializer.Serialize(normalized, BuildReadyPlanJson.Options));
        return new ConversionPlanRuleSetSnapshot { ContractVersion = normalized.ContractVersion, RuleSet = normalized, ContentHash = hash };
    }

    public bool IsValid(ConversionPlanRuleSetSnapshot snapshot, bool allowEmpty)
    {
        if (snapshot is null || snapshot.RuleSet is null || snapshot.RuleSet.Rules is null || snapshot.RuleSet.Rules.Any(rule => rule is null) || snapshot.ContractVersion != "1.0" || snapshot.RuleSet.ContractVersion != "1.0") return false;
        if (!BuildReadyPlanJson.IsSha256(snapshot.ContentHash)) return false;
        if (snapshot.RuleSet.Rules.Count == 0) return allowEmpty && Create(snapshot.RuleSet).ContentHash == snapshot.ContentHash;
        return new CadRuleSetValidator().TryValidate(snapshot.RuleSet, out _) && Create(snapshot.RuleSet).ContentHash == snapshot.ContentHash;
    }
}

public sealed class ConversionPlanDefaultProfileV2(ConversionPlanRuleSetSnapshotter ruleSetSnapshotter)
{
    private readonly ConversionPlanRuleSetSnapshotter _ruleSetSnapshotter = ruleSetSnapshotter;

    public ConversionPlanDraft Create(string planId, string analysisId, string sourceFingerprint, CadUnit sourceUnit, ConversionPlanBuildInputBinding buildInput) => new()
    {
        ContractVersion = "2.0",
        PlanId = planId,
        Revision = 1,
        SourceAnalysisId = analysisId,
        SourceFingerprint = sourceFingerprint,
        SourceUnit = sourceUnit,
        Geometry = new GeometryAdjustmentPlan { WallHeightMeters = 3d, ColumnHeightMeters = 3d },
        Outputs = new OutputConfigurationPlan { GenerateSingleGlb = true },
        BuildInput = buildInput,
        RuleSet = _ruleSetSnapshotter.Create(new CadRuleSet { ContractVersion = "1.0", Rules = Array.Empty<CadClassificationRule>() }),
        Assets = new ConversionPlanAssetConfiguration(),
        Partition = new ConversionPlanPartitionConfiguration(),
        Tiles = new ConversionPlanTilesConfiguration()
    };
}

public sealed record PlanAssetImportRequest(string OutputRootDirectory, string AssetId, CadAssetKind Kind, string SourceGlbPath);
public sealed record PlanAssetImportResult(ConversionPlanAssetResource? Resource, IReadOnlyList<SceneDiagnostic> Diagnostics);

public sealed class PlanAssetResourceImporter(IOutputRootPolicy outputRootPolicy)
{
    private const long MaximumAssetBytes = 512L * 1024L * 1024L;
    private readonly IOutputRootPolicy _outputRootPolicy = outputRootPolicy;

    public async Task<PlanAssetImportResult> ImportAsync(PlanAssetImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = _outputRootPolicy.Validate(request.OutputRootDirectory);
            if (!root.IsValid || root.NormalizedPath is null || string.IsNullOrWhiteSpace(request.AssetId) || !Path.IsPathFullyQualified(request.SourceGlbPath) || !File.Exists(request.SourceGlbPath) || Directory.Exists(request.SourceGlbPath) || IsReparsePoint(request.SourceGlbPath)) return Failed("PLAN_ASSET_RESOURCE_INVALID");
            var source = Path.GetFullPath(request.SourceGlbPath);
            var info = new FileInfo(source);
            if (info.Length is < 20 or > MaximumAssetBytes || !await IsBasicGlbAsync(source, cancellationToken)) return Failed("PLAN_ASSET_RESOURCE_INVALID");
            var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
            var hash = BuildReadyPlanJson.Sha256(bytes);
            var relative = $"plans/resources/assets/{hash}/asset.glb";
            var destination = Path.Combine(root.NormalizedPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                return BuildReadyPlanJson.Sha256(await File.ReadAllBytesAsync(destination, cancellationToken)) == hash && await IsBasicGlbAsync(destination, cancellationToken)
                    ? Succeeded(request, relative, hash, bytes.LongLength)
                    : Failed("PLAN_ASSET_RESOURCE_CONFLICT");
            }
            var staging = destination + ".staging";
            try
            {
                await File.WriteAllBytesAsync(staging, bytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!await IsBasicGlbAsync(staging, cancellationToken) || BuildReadyPlanJson.Sha256(await File.ReadAllBytesAsync(staging, cancellationToken)) != hash) return Failed("PLAN_ASSET_RESOURCE_INVALID");
                File.Move(staging, destination, false);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            return Succeeded(request, relative, hash, bytes.LongLength);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { return Failed("PLAN_ASSET_RESOURCE_INVALID"); }
    }

    internal static async Task<bool> IsBasicGlbAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is < 20 or > int.MaxValue || info.Length % 4 != 0) return false;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546C67 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != bytes.Length) return false;
        var offset = 12;
        if (!TryReadChunk(bytes, ref offset, out var jsonLength, out var jsonType) || jsonType != 0x4E4F534A) return false;
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 20, checked((int)jsonLength)));
            var root = document.RootElement;
            if (!root.TryGetProperty("asset", out var asset) || !asset.TryGetProperty("version", out var version) || version.GetString() != "2.0" ||
                !root.TryGetProperty("scene", out _) || !root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() == 0) return false;
        }
        catch (JsonException) { return false; }
        while (offset < bytes.Length)
        {
            if (!TryReadChunk(bytes, ref offset, out _, out _)) return false;
        }
        return offset == bytes.Length;
    }

    private static bool TryReadChunk(byte[] bytes, ref int offset, out uint length, out uint type)
    {
        length = 0;
        type = 0;
        if (offset > bytes.Length - 8) return false;
        length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
        if (length % 4 != 0 || length > bytes.Length - offset - 8) return false;
        offset += checked(8 + (int)length);
        return true;
    }

    private static bool IsReparsePoint(string path)
    {
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current switch { FileInfo file => file.Directory, DirectoryInfo directory => directory.Parent, _ => null };
        }
        return false;
    }

    private static PlanAssetImportResult Succeeded(PlanAssetImportRequest request, string relative, string hash, long size) => new(new ConversionPlanAssetResource { AssetId = request.AssetId, Kind = request.Kind, ResourceRelativePath = relative, ContentHash = hash, SizeBytes = size }, Array.Empty<SceneDiagnostic>());
    private static PlanAssetImportResult Failed(string code) => new(null, [Diagnostic(code)]);
    private static SceneDiagnostic Diagnostic(string code) => new() { Code = code, Severity = DiagnosticSeverity.Error, Message = "Plan asset import did not complete normally." };
}

public sealed class FrozenBuildConfigurationResolver
{
    public FrozenBuildConfiguration Resolve(ConversionPlanDraft draft, CadBuildInputSnapshot snapshot)
    {
        var selected = draft.Repair.EnabledActionIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => snapshot.RepairCandidates.Single(candidate => candidate.RepairActionId == id)).ToArray();
        var yaw = draft.InputInterpretation.YawDegrees % 360d;
        if (yaw < 0) yaw += 360d;
        return new FrozenBuildConfiguration
        {
            InputInterpretation = new FrozenInputInterpretation
            {
                SourceUnit = draft.SourceUnit,
                TargetUnit = draft.InputInterpretation.UnitConfirmation switch
                {
                    ConversionPlanUnitConfirmation.UseSourceUnit => draft.SourceUnit,
                    ConversionPlanUnitConfirmation.ExplicitMeters => CadUnit.Meters,
                    ConversionPlanUnitConfirmation.ExplicitMillimeters => CadUnit.Millimeters,
                    ConversionPlanUnitConfirmation.ExplicitCentimeters => CadUnit.Centimeters,
                    _ => CadUnit.Unknown
                },
                UnitConfirmation = draft.InputInterpretation.UnitConfirmation,
                LocalOriginStrategy = draft.InputInterpretation.LocalOriginStrategy,
                LocalOriginMeters = new CadPoint3(draft.InputInterpretation.LocalOriginXMeters, draft.InputInterpretation.LocalOriginYMeters, draft.InputInterpretation.LocalOriginZMeters),
                ZOffsetMeters = draft.InputInterpretation.ZOffsetMeters,
                YawDegrees = yaw
            },
            Geometry = draft.Geometry with { },
            Repair = new FrozenRepairConfiguration { EnabledActions = selected },
            Classification = draft.RuleSet! with { RuleSet = draft.RuleSet.RuleSet with { Rules = draft.RuleSet.RuleSet.Rules.ToArray() } },
            Assets = new FrozenAssetConfiguration
            {
                ContractVersion = draft.Assets!.ContractVersion,
                MissingAssetBehavior = draft.Assets.MissingAssetBehavior,
                Catalog = draft.Assets.Catalog.ToArray(),
                Bindings = draft.Assets.Bindings.Select(binding =>
                {
                    var candidate = snapshot.AssetCandidates.Single(item => item.AssetCandidateId == binding.AssetCandidateId);
                    var asset = draft.Assets.Catalog.Single(item => item.AssetId == binding.AssetId);
                    return new FrozenAssetBinding
                    {
                        AssetCandidateId = binding.AssetCandidateId,
                        AssetId = binding.AssetId,
                        Kind = asset.Kind,
                        Position = candidate.Position,
                        RotationDegrees = candidate.RotationDegrees,
                        Scale = candidate.Scale
                    };
                }).ToArray()
            },
            Outputs = new FrozenOutputConfiguration
            {
                GenerateSingleGlb = draft.Outputs.GenerateSingleGlb,
                PublishScenePackageArtifact = draft.Outputs.GenerateScenePackage,
                Generate3DTiles = draft.Outputs.Generate3DTiles,
                GenerateScenePackageAsDependency = draft.Outputs.Generate3DTiles && !draft.Outputs.GenerateScenePackage,
                PrimaryOutput = draft.Outputs.GenerateSingleGlb ? "singleGlb" : draft.Outputs.GenerateScenePackage ? "scenePackage" : "threeDTiles"
            },
            Partition = draft.Partition! with { },
            ThreeDTiles = draft.Tiles! with { }
        };
    }
}

public static class FrozenPlanCanonicalHasher
{
    public static string Compute(FrozenConversionPlan plan)
    {
        var payload = plan with { FrozenPlanId = string.Empty, FrozenPlanContentHash = string.Empty };
        return BuildReadyPlanJson.Sha256(JsonSerializer.Serialize(payload, BuildReadyPlanJson.Options));
    }

}

public sealed class FrozenPlanV2Serializer
{
    public async Task<FrozenConversionPlan> ReadValidatedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var plan = await JsonSerializer.DeserializeAsync<FrozenConversionPlan>(stream, BuildReadyPlanJson.Options, cancellationToken) ?? throw new InvalidDataException("Frozen plan JSON is empty.");
        if (plan.ContractVersion is not ("1.0" or "2.0")) throw new InvalidDataException("Frozen plan contract is unsupported.");
        if (plan.ContractVersion == "2.0" && (FrozenPlanCanonicalHasher.Compute(plan) != plan.FrozenPlanContentHash || plan.FrozenPlanId != "frozen-plan-" + plan.FrozenPlanContentHash)) throw new InvalidDataException("Frozen plan hash is invalid.");
        return plan;
    }
}

public sealed class FrozenPlanBuildReadinessValidator(ConversionPlanRuleSetSnapshotter ruleSetSnapshotter)
{
    private readonly ConversionPlanRuleSetSnapshotter _ruleSetSnapshotter = ruleSetSnapshotter;

    public async Task<FrozenPlanBuildReadinessResult> ValidateAsync(FrozenConversionPlan plan, string outputRoot, CancellationToken cancellationToken)
    {
        if (plan.ContractVersion == "1.0") return NotReady("FROZEN_PLAN_NOT_BUILD_READY", "PLAN_REFREEZE_REQUIRED");
        if (plan.ContractVersion != "2.0" || plan.BuildInput is null) return NotReady("FROZEN_PLAN_BUILD_SNAPSHOT_MISSING");
        if (plan.Identity is null || plan.BuildConfiguration is null || plan.Identity.Revision < 1 || string.IsNullOrWhiteSpace(plan.Identity.PlanId) || string.IsNullOrWhiteSpace(plan.Identity.DraftContentHash) || !BuildReadyPlanJson.IsSha256(plan.Identity.ValidationContentHash) || plan.Identity.ValidationArtifactRelativePath != $"plans/revision-{plan.Identity.Revision:D4}/validation.json" || FrozenPlanCanonicalHasher.Compute(plan) != plan.FrozenPlanContentHash || plan.FrozenPlanId != "frozen-plan-" + plan.FrozenPlanContentHash) return NotReady("FROZEN_PLAN_BUILD_CONFIGURATION_MISSING");
        try
        {
            var analysisPath = ResolveControlled(outputRoot, plan.BuildInput.AnalysisArtifactRelativePath, "analysis/cad-analysis.json");
            var snapshotPath = ResolveControlled(outputRoot, plan.BuildInput.SnapshotArtifactRelativePath, "analysis/build-input-snapshot.json");
            var validationPath = ResolveControlled(outputRoot, plan.Identity.ValidationArtifactRelativePath, plan.Identity.ValidationArtifactRelativePath);
            using var analysis = JsonDocument.Parse(await File.ReadAllTextAsync(analysisPath, BuildReadyPlanJson.Utf8, cancellationToken));
            using var validation = JsonDocument.Parse(await File.ReadAllTextAsync(validationPath, BuildReadyPlanJson.Utf8, cancellationToken));
            var snapshot = await CadBuildInputSnapshotSerializer.ReadValidatedAsync(snapshotPath, cancellationToken);
            if (!ValidationMatches(validation.RootElement, plan) || analysis.RootElement.GetProperty("contractVersion").GetString() != "2.0" || analysis.RootElement.GetProperty("analysisId").GetString() != plan.BuildInput.AnalysisId || analysis.RootElement.GetProperty("sourceFingerprint").GetString() != plan.BuildInput.SourceFingerprint || snapshot.AnalysisId != plan.BuildInput.AnalysisId || snapshot.SourceFingerprint != plan.BuildInput.SourceFingerprint || snapshot.SnapshotId != plan.BuildInput.SnapshotId || snapshot.ContentHash != plan.BuildInput.SnapshotContentHash) return NotReady("FROZEN_PLAN_BUILD_SNAPSHOT_MISMATCH");
            if (!_ruleSetSnapshotter.IsValid(plan.BuildConfiguration.Classification, snapshot.AnalyzeTimeClassifications.Count == 0) || !ValidConfiguration(plan.BuildConfiguration) || !await ValidAssetsAsync(plan.BuildConfiguration.Assets, outputRoot, snapshot, cancellationToken)) return NotReady("FROZEN_PLAN_BUILD_CONFIGURATION_MISSING");
            return new FrozenPlanBuildReadinessResult { Status = FrozenPlanBuildReadinessStatus.Ready };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException) { return NotReady("FROZEN_PLAN_BUILD_SNAPSHOT_MISMATCH"); }
    }

    internal static bool ValidConfiguration(FrozenBuildConfiguration value)
    {
        if (value is null || value.InputInterpretation is null || value.Geometry is null || value.Repair is null || value.Classification is null || value.Assets is null || value.Outputs is null || value.Partition is null || value.ThreeDTiles is null)
        {
            return false;
        }

        var input = value.InputInterpretation;
        var origin = input.LocalOriginMeters;
        var geometry = value.Geometry;
        var repair = value.Repair;
        var output = value.Outputs;
        var partition = value.Partition;
        var tiles = value.ThreeDTiles;
        return value.DefaultProfileCode == "CORE04B_V2" &&
            Enum.IsDefined(input.SourceUnit) && input.SourceUnit is not (CadUnit.Unknown or CadUnit.Unitless) &&
            Enum.IsDefined(input.TargetUnit) && input.TargetUnit is not (CadUnit.Unknown or CadUnit.Unitless) &&
            Enum.IsDefined(input.UnitConfirmation) && Enum.IsDefined(input.LocalOriginStrategy) &&
            ((input.UnitConfirmation == ConversionPlanUnitConfirmation.UseSourceUnit && input.TargetUnit == input.SourceUnit) ||
             (input.UnitConfirmation == ConversionPlanUnitConfirmation.ExplicitMeters && input.TargetUnit == CadUnit.Meters) ||
             (input.UnitConfirmation == ConversionPlanUnitConfirmation.ExplicitMillimeters && input.TargetUnit == CadUnit.Millimeters) ||
             (input.UnitConfirmation == ConversionPlanUnitConfirmation.ExplicitCentimeters && input.TargetUnit == CadUnit.Centimeters)) &&
            input.CoordinateMode == "localCartesian" && input.UpAxis == "zUp" &&
            double.IsFinite(origin.X) && double.IsFinite(origin.Y) && double.IsFinite(origin.Z) &&
            double.IsFinite(input.ZOffsetMeters) && double.IsFinite(input.YawDegrees) && input.YawDegrees >= 0 && input.YawDegrees < 360 &&
            geometry.WallHeightMeters is > 0 && double.IsFinite(geometry.WallHeightMeters.Value) &&
            geometry.ColumnHeightMeters is > 0 && double.IsFinite(geometry.ColumnHeightMeters.Value) &&
            repair.ContractVersion == "1.0" && repair.EnabledActions is not null && repair.EnabledActions.All(IsValidRepairCandidate) &&
            (output.GenerateSingleGlb || output.PublishScenePackageArtifact || output.Generate3DTiles) &&
            output.PrimaryOutput is "singleGlb" or "scenePackage" or "threeDTiles" &&
            ((output.PrimaryOutput == "singleGlb" && output.GenerateSingleGlb) || (output.PrimaryOutput == "scenePackage" && output.PublishScenePackageArtifact) || (output.PrimaryOutput == "threeDTiles" && output.Generate3DTiles)) &&
            (!output.Generate3DTiles || output.PublishScenePackageArtifact || output.GenerateScenePackageAsDependency) &&
            partition is not null && double.IsFinite(partition.CellSizeMeters) && partition.CellSizeMeters > 0 &&
            double.IsFinite(partition.OriginXMeters) && double.IsFinite(partition.OriginYMeters) && partition.MaximumIntersectedCellsPerObject > 0 &&
            Enum.IsDefined(partition.LargeObjectBehavior) && Enum.IsDefined(partition.InvalidBoundsBehavior) &&
            tiles is not null && double.IsFinite(tiles.RootGeometricErrorMeters) && tiles.RootGeometricErrorMeters > 0 &&
            double.IsFinite(tiles.MinimumBoundingHalfExtentMeters) && tiles.MinimumBoundingHalfExtentMeters > 0 &&
            tiles.Refine == "ADD" && tiles.CoordinateMode == "localCartesian" && tiles.Unit == "meters" && tiles.UpAxis == "zUp" && tiles.ContentUriStrategy == "scenePackagePartitionGlb";
    }

    private static bool IsValidRepairCandidate(CadBuildRepairCandidate candidate) =>
        candidate is not null && !string.IsNullOrWhiteSpace(candidate.RepairActionId) && candidate.Action is { } action &&
        action.Id == candidate.RepairActionId && Enum.IsDefined(action.ActionType) &&
        action.Confidence is CadGeometryRepairConfidence.Deterministic or CadGeometryRepairConfidence.High &&
        candidate.GeometryObjectIds is not null && candidate.ContourIds is not null;

    internal static async Task<bool> ValidAssetsAsync(ConversionPlanAssetConfiguration assets, string root, CadBuildInputSnapshot snapshot, CancellationToken token)
    {
        if (assets is null || assets.ContractVersion != "1.0" || !Enum.IsDefined(assets.MissingAssetBehavior) || assets.Catalog is null || assets.Bindings is null) return false;
        var candidates = snapshot.AssetCandidates.ToDictionary(value => value.AssetCandidateId, StringComparer.Ordinal);
        if (candidates.Keys.Any(string.IsNullOrWhiteSpace) || assets.Catalog.Any(asset => !Enum.IsDefined(asset.Kind) || string.IsNullOrWhiteSpace(asset.AssetId)) ||
            assets.Catalog.Select(value => value.AssetId).Distinct(StringComparer.Ordinal).Count() != assets.Catalog.Count ||
            assets.Bindings.Select(value => value.AssetCandidateId).Distinct(StringComparer.Ordinal).Count() != assets.Bindings.Count ||
            assets.Bindings.Any(value => string.IsNullOrWhiteSpace(value.AssetCandidateId) || string.IsNullOrWhiteSpace(value.AssetId) || !candidates.ContainsKey(value.AssetCandidateId) || assets.Catalog.All(asset => asset.AssetId != value.AssetId) || !KindMatches(candidates[value.AssetCandidateId], assets.Catalog.Single(asset => asset.AssetId == value.AssetId))) ||
            candidates.Keys.Any(candidate => assets.Bindings.All(value => value.AssetCandidateId != candidate))) return false;
        return await ValidAssetResourcesAsync(assets.Catalog, root, token);
    }

    internal static async Task<bool> ValidAssetsAsync(FrozenAssetConfiguration assets, string root, CadBuildInputSnapshot snapshot, CancellationToken token)
    {
        if (assets is null || assets.ContractVersion != "1.0" || !Enum.IsDefined(assets.MissingAssetBehavior) || assets.Catalog is null || assets.Bindings is null) return false;
        var candidates = snapshot.AssetCandidates.ToDictionary(value => value.AssetCandidateId, StringComparer.Ordinal);
        if (assets.Catalog.Select(value => value.AssetId).Distinct(StringComparer.Ordinal).Count() != assets.Catalog.Count ||
            assets.Bindings.Select(value => value.AssetCandidateId).Distinct(StringComparer.Ordinal).Count() != assets.Bindings.Count ||
            assets.Bindings.Any(value => string.IsNullOrWhiteSpace(value.AssetCandidateId) || string.IsNullOrWhiteSpace(value.AssetId) || !candidates.ContainsKey(value.AssetCandidateId) || assets.Catalog.All(asset => asset.AssetId != value.AssetId))) return false;
        if (candidates.Keys.Any(candidate => assets.Bindings.All(value => value.AssetCandidateId != candidate)) || assets.Bindings.Any(binding => !ValidFrozenAssetBinding(binding, candidates[binding.AssetCandidateId], assets.Catalog.Single(asset => asset.AssetId == binding.AssetId)))) return false;
        return await ValidAssetResourcesAsync(assets.Catalog, root, token);
    }

    private static async Task<bool> ValidAssetResourcesAsync(IReadOnlyList<ConversionPlanAssetResource> catalog, string root, CancellationToken token)
    {
        if (catalog.Select(value => value.AssetId).Distinct(StringComparer.Ordinal).Count() != catalog.Count) return false;
        foreach (var asset in catalog)
        {
            if (!BuildReadyPlanJson.IsSha256(asset.ContentHash) || asset.ContentHash != asset.ContentHash.ToLowerInvariant() || asset.SizeBytes is < 20 or > 512L * 1024L * 1024L || asset.ResourceRelativePath != $"plans/resources/assets/{asset.ContentHash}/asset.glb") return false;
            var path = ResolveControlled(root, asset.ResourceRelativePath, asset.ResourceRelativePath);
            var info = new FileInfo(path);
            if (info.Length != asset.SizeBytes || IsReparsePoint(path) || BuildReadyPlanJson.Sha256(await File.ReadAllBytesAsync(path, token)) != asset.ContentHash || !await PlanAssetResourceImporter.IsBasicGlbAsync(path, token)) return false;
        }
        return true;
    }

    private static bool KindMatches(CadBuildAssetCandidate candidate, ConversionPlanAssetResource asset) => candidate.CandidateType switch
    {
        CadSemanticClassification.StaticFacility => asset.Kind == CadAssetKind.StaticFacility,
        CadSemanticClassification.DynamicEquipment => asset.Kind == CadAssetKind.DynamicEquipment,
        _ => false
    };

    private static bool ValidFrozenAssetBinding(FrozenAssetBinding binding, CadBuildAssetCandidate candidate, ConversionPlanAssetResource asset) =>
        KindMatches(candidate, asset) && binding.Kind == asset.Kind && binding.Position.X == candidate.Position.X && binding.Position.Y == candidate.Position.Y && binding.Position.Z == candidate.Position.Z &&
        binding.RotationDegrees == candidate.RotationDegrees && binding.Scale.X == candidate.Scale.X && binding.Scale.Y == candidate.Scale.Y && binding.Scale.Z == candidate.Scale.Z &&
        double.IsFinite(binding.Position.X) && double.IsFinite(binding.Position.Y) && double.IsFinite(binding.Position.Z) &&
        double.IsFinite(binding.RotationDegrees) && double.IsFinite(binding.Scale.X) && double.IsFinite(binding.Scale.Y) && double.IsFinite(binding.Scale.Z) &&
        binding.Scale.X > 0 && binding.Scale.Y > 0 && binding.Scale.Z > 0;

    private static bool ValidationMatches(JsonElement validation, FrozenConversionPlan plan)
    {
        return validation.ValueKind == JsonValueKind.Object &&
            validation.TryGetProperty("contractVersion", out var contract) && contract.GetString() == "2.0" &&
            validation.TryGetProperty("planId", out var planId) && planId.GetString() == plan.Identity!.PlanId &&
            validation.TryGetProperty("revision", out var revision) && revision.GetInt32() == plan.Identity.Revision &&
            validation.TryGetProperty("planContentId", out var contentId) && contentId.GetString() == plan.Identity.DraftContentHash &&
            validation.TryGetProperty("validationStatus", out var status) && status.GetString() == "valid" &&
            validation.TryGetProperty("validationContentHash", out var hash) && hash.GetString() == plan.Identity.ValidationContentHash &&
            TryReadValidationArtifact(validation, out var artifact) && ConversionPlanValidationArtifactHasher.Compute(artifact) == plan.Identity.ValidationContentHash;
    }

    private static bool TryReadValidationArtifact(JsonElement value, out ConversionPlanValidationArtifact artifact)
    {
        artifact = new ConversionPlanValidationArtifact();
        try
        {
            artifact = value.Deserialize<ConversionPlanValidationArtifact>(BuildReadyPlanJson.Options)!;
            return artifact is not null;
        }
        catch (JsonException) { return false; }
    }

    private static string ResolveControlled(string root, string relative, string expected)
    {
        if (relative != expected || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal) || relative.Contains("://", StringComparison.Ordinal)) throw new InvalidDataException();
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || IsReparsePoint(path, normalizedRoot)) throw new InvalidDataException();
        return path;
    }

    private static bool IsReparsePoint(string path, string? stopAt = null)
    {
        var normalizedStop = stopAt is null ? null : Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            var currentPath = Path.GetFullPath(current.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedStop is not null && string.Equals(currentPath, normalizedStop, StringComparison.OrdinalIgnoreCase)) break;
            current = current switch { FileInfo file => file.Directory, DirectoryInfo directory => directory.Parent, _ => null };
        }
        return false;
    }

    private static FrozenPlanBuildReadinessResult NotReady(params string[] codes) => new() { Status = FrozenPlanBuildReadinessStatus.NotReady, Diagnostics = codes.Select(code => new SceneDiagnostic { Code = code, Severity = DiagnosticSeverity.Error, Message = "Frozen plan is not build ready." }).ToArray() };
}
