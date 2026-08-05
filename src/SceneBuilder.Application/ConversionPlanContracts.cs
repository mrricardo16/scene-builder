using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public enum ConversionPlanValidationStatus { NotValidated = 0, Valid = 1, Invalid = 2 }
public enum ConversionPlanUnitConfirmation { UseSourceUnit = 0, ExplicitMeters = 1, ExplicitMillimeters = 2, ExplicitCentimeters = 3 }
public enum ConversionPlanLocalOriginStrategy { UseAnalyzedLocalOrigin = 0, ExplicitOffset = 1 }

public sealed record InputInterpretationPlan
{
    public ConversionPlanUnitConfirmation UnitConfirmation { get; init; } = ConversionPlanUnitConfirmation.UseSourceUnit;
    public ConversionPlanLocalOriginStrategy LocalOriginStrategy { get; init; } = ConversionPlanLocalOriginStrategy.UseAnalyzedLocalOrigin;
    public double ZOffsetMeters { get; init; }
    public double YawDegrees { get; init; }
    public double LocalOriginXMeters { get; init; }
    public double LocalOriginYMeters { get; init; }
    public double LocalOriginZMeters { get; init; }
}

public sealed record RepairConfigurationPlan
{
    public IReadOnlyList<string> EnabledActionIds { get; init; } = Array.Empty<string>();
}

public sealed record ClassificationConfigurationPlan
{
    public string? RuleSetFingerprint { get; init; }
}

public sealed record GeometryAdjustmentPlan
{
    public double? WallHeightMeters { get; init; }
    public double? ColumnHeightMeters { get; init; }
}

public sealed record OutputConfigurationPlan
{
    public bool GenerateSingleGlb { get; init; }
    public bool GenerateScenePackage { get; init; }
    public bool Generate3DTiles { get; init; }
}

public sealed record ConversionPlanDraft
{
    public string ContractVersion { get; init; } = "1.0";
    public string PlanId { get; init; } = string.Empty;
    public string PlanContentId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public string SourceAnalysisId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public CadUnit SourceUnit { get; init; }
    public ConversionPlanValidationStatus ValidationStatus { get; init; } = ConversionPlanValidationStatus.NotValidated;
    public InputInterpretationPlan InputInterpretation { get; init; } = new();
    public GeometryAdjustmentPlan Geometry { get; init; } = new();
    public RepairConfigurationPlan Repair { get; init; } = new();
    public ClassificationConfigurationPlan Classification { get; init; } = new();
    public OutputConfigurationPlan Outputs { get; init; } = new();
    public ConversionPlanBuildInputBinding? BuildInput { get; init; }
    public ConversionPlanRuleSetSnapshot? RuleSet { get; init; }
    public ConversionPlanAssetConfiguration? Assets { get; init; }
    public ConversionPlanPartitionConfiguration? Partition { get; init; }
    public ConversionPlanTilesConfiguration? Tiles { get; init; }
}

public sealed record FrozenConversionPlan
{
    public string ContractVersion { get; init; } = "1.0";
    public string FrozenPlanId { get; init; } = string.Empty;
    public ConversionPlanDraft? Draft { get; init; }
    public string FrozenPlanContentHash { get; init; } = string.Empty;
    public FrozenPlanIdentity? Identity { get; init; }
    public ConversionPlanBuildInputBinding? BuildInput { get; init; }
    public FrozenBuildConfiguration? BuildConfiguration { get; init; }
}

public sealed record CreateConversionPlanDraftRequest { public string AnalysisPath { get; init; } = string.Empty; public string OutputRootDirectory { get; init; } = string.Empty; }
public sealed record SaveConversionPlanRevisionRequest { public string PreviousPlanPath { get; init; } = string.Empty; public string OutputRootDirectory { get; init; } = string.Empty; public ConversionPlanDraft Draft { get; init; } = new(); }
public sealed record ValidateConversionPlanRequest { public string PlanPath { get; init; } = string.Empty; public string OutputRootDirectory { get; init; } = string.Empty; }
public sealed record FreezeConversionPlanRequest { public string PlanPath { get; init; } = string.Empty; public string OutputRootDirectory { get; init; } = string.Empty; }

public sealed record ConversionPlanDraftResult
{
    public SceneOperationStatus Status { get; init; }
    public ConversionPlanDraft? Draft { get; init; }
    public IReadOnlyList<SceneArtifactDescriptor> Artifacts { get; init; } = Array.Empty<SceneArtifactDescriptor>();
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record ConversionPlanValidationResult
{
    public string ContractVersion { get; init; } = "1.0";
    public SceneOperationStatus Status { get; init; }
    public string PlanId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public ConversionPlanValidationStatus ValidationStatus { get; init; }
    public string PlanContentId { get; init; } = string.Empty;
    public string ValidationContentHash { get; init; } = string.Empty;
    public string? SnapshotId { get; init; }
    public string? SnapshotContentHash { get; init; }
    public IReadOnlyList<SceneArtifactDescriptor> Artifacts { get; init; } = Array.Empty<SceneArtifactDescriptor>();
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record FrozenConversionPlanResult
{
    public SceneOperationStatus Status { get; init; }
    public FrozenConversionPlan? FrozenPlan { get; init; }
    public IReadOnlyList<SceneArtifactDescriptor> Artifacts { get; init; } = Array.Empty<SceneArtifactDescriptor>();
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
    public FrozenPlanBuildReadinessStatus BuildReadiness { get; init; }
}

public interface IConversionPlanService
{
    Task<ConversionPlanDraftResult> CreateDraftAsync(CreateConversionPlanDraftRequest request, CancellationToken cancellationToken);
    Task<ConversionPlanDraftResult> SaveRevisionAsync(SaveConversionPlanRevisionRequest request, CancellationToken cancellationToken);
    Task<ConversionPlanValidationResult> ValidateAsync(ValidateConversionPlanRequest request, CancellationToken cancellationToken);
    Task<FrozenConversionPlanResult> FreezeAsync(FreezeConversionPlanRequest request, CancellationToken cancellationToken);
}
