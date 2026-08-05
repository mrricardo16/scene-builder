using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record ConversionPlanBuildInputBinding
{
    public string AnalysisContractVersion { get; init; } = string.Empty;
    public string AnalysisId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public string AnalysisArtifactRelativePath { get; init; } = string.Empty;
    public string SnapshotContractVersion { get; init; } = string.Empty;
    public string SnapshotId { get; init; } = string.Empty;
    public string SnapshotContentHash { get; init; } = string.Empty;
    public string SnapshotArtifactRelativePath { get; init; } = string.Empty;
}

public sealed record ConversionPlanRuleSetSnapshot
{
    public string ContractVersion { get; init; } = "1.0";
    public string ContentHash { get; init; } = string.Empty;
    public CadRuleSet RuleSet { get; init; } = new() { ContractVersion = "1.0" };
}

public sealed record ConversionPlanAssetResource
{
    public string AssetId { get; init; } = string.Empty;
    public CadAssetKind Kind { get; init; }
    public string ResourceRelativePath { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

public sealed record ConversionPlanAssetBinding
{
    public string AssetCandidateId { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
}

public sealed record FrozenAssetBinding
{
    public string AssetCandidateId { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public CadAssetKind Kind { get; init; }
    public CadPoint3 Position { get; init; } = new(0, 0, 0);
    public double RotationDegrees { get; init; }
    public CadScale3 Scale { get; init; } = CadScale3.Identity;
}

public sealed record ConversionPlanAssetConfiguration
{
    public string ContractVersion { get; init; } = "1.0";
    public MissingAssetBehavior MissingAssetBehavior { get; init; } = MissingAssetBehavior.Fail;
    public IReadOnlyList<ConversionPlanAssetResource> Catalog { get; init; } = Array.Empty<ConversionPlanAssetResource>();
    public IReadOnlyList<ConversionPlanAssetBinding> Bindings { get; init; } = Array.Empty<ConversionPlanAssetBinding>();
}

public sealed record FrozenAssetConfiguration
{
    public string ContractVersion { get; init; } = "1.0";
    public MissingAssetBehavior MissingAssetBehavior { get; init; } = MissingAssetBehavior.Fail;
    public IReadOnlyList<ConversionPlanAssetResource> Catalog { get; init; } = Array.Empty<ConversionPlanAssetResource>();
    public IReadOnlyList<FrozenAssetBinding> Bindings { get; init; } = Array.Empty<FrozenAssetBinding>();
}

public sealed record ConversionPlanPartitionConfiguration
{
    public double CellSizeMeters { get; init; } = 100d;
    public double OriginXMeters { get; init; }
    public double OriginYMeters { get; init; }
    public int MaximumIntersectedCellsPerObject { get; init; } = 16;
    public LargeSceneObjectBehavior LargeObjectBehavior { get; init; } = LargeSceneObjectBehavior.AssignToGlobalPartition;
    public InvalidBoundsBehavior InvalidBoundsBehavior { get; init; } = InvalidBoundsBehavior.Fail;
    public bool ContinueAfterPartitionFailure { get; init; } = true;
    public bool PublishPartialPackage { get; init; }
}

public sealed record ConversionPlanTilesConfiguration
{
    public double RootGeometricErrorMeters { get; init; } = 100d;
    public double MinimumBoundingHalfExtentMeters { get; init; } = 0.001d;
    public bool AllowPartialScenePackage { get; init; } = true;
    public string Refine { get; init; } = "ADD";
    public string CoordinateMode { get; init; } = "localCartesian";
    public string Unit { get; init; } = "meters";
    public string UpAxis { get; init; } = "zUp";
    public string ContentUriStrategy { get; init; } = "scenePackagePartitionGlb";
}

public sealed record FrozenPlanIdentity
{
    public string PlanId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public string DraftContentHash { get; init; } = string.Empty;
    public string ValidationContentHash { get; init; } = string.Empty;
    public string ValidationArtifactRelativePath { get; init; } = string.Empty;
}

public sealed record FrozenInputInterpretation
{
    public CadUnit SourceUnit { get; init; }
    public CadUnit TargetUnit { get; init; } = CadUnit.Meters;
    public ConversionPlanUnitConfirmation UnitConfirmation { get; init; }
    public ConversionPlanLocalOriginStrategy LocalOriginStrategy { get; init; }
    public CadPoint3 LocalOriginMeters { get; init; } = new(0, 0, 0);
    public double ZOffsetMeters { get; init; }
    public double YawDegrees { get; init; }
    public string CoordinateMode { get; init; } = "localCartesian";
    public string UpAxis { get; init; } = "zUp";
}

public sealed record FrozenRepairConfiguration
{
    public string ContractVersion { get; init; } = "1.0";
    public IReadOnlyList<CadBuildRepairCandidate> EnabledActions { get; init; } = Array.Empty<CadBuildRepairCandidate>();
}

public sealed record FrozenOutputConfiguration
{
    public bool GenerateSingleGlb { get; init; }
    public bool PublishScenePackageArtifact { get; init; }
    public bool Generate3DTiles { get; init; }
    public bool GenerateScenePackageAsDependency { get; init; }
    public string PrimaryOutput { get; init; } = string.Empty;
}

public sealed record FrozenBuildConfiguration
{
    public string DefaultProfileCode { get; init; } = "CORE04B_V2";
    public FrozenInputInterpretation InputInterpretation { get; init; } = new();
    public GeometryAdjustmentPlan Geometry { get; init; } = new();
    public FrozenRepairConfiguration Repair { get; init; } = new();
    public ConversionPlanRuleSetSnapshot Classification { get; init; } = new();
    public FrozenAssetConfiguration Assets { get; init; } = new();
    public FrozenOutputConfiguration Outputs { get; init; } = new();
    public ConversionPlanPartitionConfiguration Partition { get; init; } = new();
    public ConversionPlanTilesConfiguration ThreeDTiles { get; init; } = new();
}

public enum FrozenPlanBuildReadinessStatus { NotReady = 0, Ready = 1 }

public sealed record FrozenPlanBuildReadinessResult
{
    public FrozenPlanBuildReadinessStatus Status { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record ConversionPlanValidationArtifact
{
    public string ContractVersion { get; init; } = "1.0";
    public string PlanId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public string PlanContentId { get; init; } = string.Empty;
    public ConversionPlanValidationStatus ValidationStatus { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
    public string AnalysisId { get; init; } = string.Empty;
    public string? SnapshotId { get; init; }
    public string? SnapshotContentHash { get; init; }
    public string ValidationContentHash { get; init; } = string.Empty;
}
