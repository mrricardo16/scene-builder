using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record CadInputDescriptor
{
    public string Extension { get; init; } = string.Empty;

    public CadSourceFormat SourceFormat { get; init; } = CadSourceFormat.Unknown;
}

public sealed record CadAdapterAnalysisRequest
{
    public string ControlledInputPath { get; init; } = string.Empty;

    public CadInputDescriptor Input { get; init; } = new();

    public CadUnit? UnitOverride { get; init; }
}

public sealed record CadAdapterAnalysisResult
{
    public SceneOperationStatus Status { get; init; } = SceneOperationStatus.Failed;

    public CadDocumentModel? SourceDocument { get; init; }

    public NormalizedCadGeometryDocument? Geometry { get; init; }

    public CadContourDocument? Contours { get; init; }

    public CadGeometryRepairPlan? RepairPlan { get; init; }

    public bool WasUnitOverridden { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface ICadInputAdapter
{
    string AdapterId { get; }

    bool CanHandle(CadInputDescriptor input);

    Task<CadAdapterAnalysisResult> AnalyzeAsync(
        CadAdapterAnalysisRequest request,
        IProgress<SceneOperationProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed record CadImportAnalysisRequest
{
    public string InputPath { get; init; } = string.Empty;

    public string OutputRootDirectory { get; init; } = string.Empty;

    public string? RuleSetPath { get; init; }

    public CadUnit? UnitOverride { get; init; }
}

public sealed record CadImportInputSummary
{
    public CadSourceFormat InputKind { get; init; }

    public string FormatVersion { get; init; } = "Unavailable";

    public CadUnit Unit { get; init; }

    public string UnitStatus { get; init; } = "Unknown";

    public string UnitSource { get; init; } = "Source";
}

public sealed record CadImportStructureSummary
{
    public IReadOnlyList<CadLayerModel> Layers { get; init; } = Array.Empty<CadLayerModel>();

    public IReadOnlyList<CadBlockModel> Blocks { get; init; } = Array.Empty<CadBlockModel>();

    public IReadOnlyList<CadEntityTypeSummary> EntityTypes { get; init; } = Array.Empty<CadEntityTypeSummary>();

    public string XrefStatus { get; init; } = "Unavailable";

    public string ProxyObjectStatus { get; init; } = "Unavailable";

    public int UnsupportedEntityCount { get; init; }
}

public sealed record CadImportGeometrySummary
{
    public int SupportedGeometryCount { get; init; }

    public int OpenSegmentCount { get; init; }

    public int ClosedCandidateCount { get; init; }

    public int ValidContourCount { get; init; }

    public int InvalidContourCount { get; init; }
}

public sealed record CadImportRepairSummary
{
    public int CandidateCount { get; init; }

    public int ApplicableCount { get; init; }

    public int RejectedCount { get; init; }

    public int UnresolvedIssueCount { get; init; }
}

public sealed record CadImportClassificationSummary
{
    public string Status { get; init; } = "NotConfigured";

    public int WallCount { get; init; }

    public int ColumnCount { get; init; }

    public int FloorCount { get; init; }

    public int RoadCount { get; init; }

    public int StaticFacilityCount { get; init; }

    public int DynamicEquipmentCount { get; init; }

    public int UnclassifiedCount { get; init; }

    public int RuleConflictCount { get; init; }
}

public sealed record CadImportAnalysisResult
{
    public string ContractVersion { get; init; } = "2.0";

    public string AnalysisId { get; init; } = string.Empty;

    public string SourceFingerprint { get; init; } = string.Empty;

    public CadBuildInputSnapshotDescriptor BuildInputSnapshot { get; init; } = new();

    public SceneOperationStatus Status { get; init; }

    public CadImportInputSummary Input { get; init; } = new();

    public CadBounds OriginalBounds { get; init; } = CadBounds.NotEvaluated;

    public CadBounds NormalizedBounds { get; init; } = CadBounds.NotEvaluated;

    public CadImportStructureSummary Structure { get; init; } = new();

    public CadImportGeometrySummary Geometry { get; init; } = new();

    public CadImportRepairSummary Repair { get; init; } = new();

    public CadImportClassificationSummary Classification { get; init; } = new();

    public IReadOnlyList<string> AssetCandidates { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SceneArtifactDescriptor> Artifacts { get; init; } = Array.Empty<SceneArtifactDescriptor>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}
