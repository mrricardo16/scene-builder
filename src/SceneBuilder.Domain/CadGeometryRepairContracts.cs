namespace SceneBuilder.Domain;

public enum CadGeometryRepairActionType
{
    SnapEndpoint = 0,
    BridgeGap = 1,
    RemoveExactDuplicate = 2,
    RemoveReverseDuplicate = 3,
    MergeCollinearSegments = 4,
    ReverseSegment = 5,
    CloseSegmentChain = 6
}

public enum CadGeometryRepairConfidence
{
    Deterministic = 0,
    High = 1,
    Medium = 2,
    Low = 3
}

public enum CadGeometryRepairPlanStatus
{
    NoChangesRequired = 0,
    Ready = 1,
    HasConflicts = 2,
    Failed = 3
}

public enum CadGeometryRepairStatus
{
    NoChangesRequired = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3
}

public sealed record CadGeometryRepairPolicy
{
    public CadGeometryRepairPolicy(
        double endpointSnapToleranceMeters = 0.002d,
        double maximumBridgeGapMeters = 0.02d,
        double duplicateDistanceToleranceMeters = 0.000001d,
        double collinearDistanceToleranceMeters = 0.000001d,
        double collinearAngleToleranceDegrees = 0.1d,
        double maximumElevationDifferenceMeters = 0.000001d,
        bool allowEndpointSnap = true,
        bool allowGapBridge = true,
        bool allowDuplicateRemoval = true,
        bool allowCollinearMerge = true,
        bool allowSegmentReversal = true,
        bool allowCrossLayerRepair = false)
    {
        ValidateDistance(endpointSnapToleranceMeters, nameof(endpointSnapToleranceMeters));
        ValidateDistance(maximumBridgeGapMeters, nameof(maximumBridgeGapMeters));
        ValidateDistance(duplicateDistanceToleranceMeters, nameof(duplicateDistanceToleranceMeters));
        ValidateDistance(collinearDistanceToleranceMeters, nameof(collinearDistanceToleranceMeters));
        ValidateDistance(maximumElevationDifferenceMeters, nameof(maximumElevationDifferenceMeters));
        if (!double.IsFinite(collinearAngleToleranceDegrees) ||
            collinearAngleToleranceDegrees < 0 ||
            collinearAngleToleranceDegrees > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(collinearAngleToleranceDegrees));
        }

        if (maximumBridgeGapMeters < endpointSnapToleranceMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBridgeGapMeters));
        }

        EndpointSnapToleranceMeters = endpointSnapToleranceMeters;
        MaximumBridgeGapMeters = maximumBridgeGapMeters;
        DuplicateDistanceToleranceMeters = duplicateDistanceToleranceMeters;
        CollinearDistanceToleranceMeters = collinearDistanceToleranceMeters;
        CollinearAngleToleranceDegrees = collinearAngleToleranceDegrees;
        MaximumElevationDifferenceMeters = maximumElevationDifferenceMeters;
        AllowEndpointSnap = allowEndpointSnap;
        AllowGapBridge = allowGapBridge;
        AllowDuplicateRemoval = allowDuplicateRemoval;
        AllowCollinearMerge = allowCollinearMerge;
        AllowSegmentReversal = allowSegmentReversal;
        AllowCrossLayerRepair = allowCrossLayerRepair;
    }

    public double EndpointSnapToleranceMeters { get; }

    public double MaximumBridgeGapMeters { get; }

    public double DuplicateDistanceToleranceMeters { get; }

    public double CollinearDistanceToleranceMeters { get; }

    public double CollinearAngleToleranceDegrees { get; }

    public double MaximumElevationDifferenceMeters { get; }

    public bool AllowEndpointSnap { get; }

    public bool AllowGapBridge { get; }

    public bool AllowDuplicateRemoval { get; }

    public bool AllowCollinearMerge { get; }

    public bool AllowSegmentReversal { get; }

    public bool AllowCrossLayerRepair { get; }

    private static void ValidateDistance(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record CadGeometryRepairAction
{
    public string Id { get; init; } = string.Empty;

    public CadGeometryRepairActionType ActionType { get; init; }

    public IReadOnlyList<string> SourceSegmentIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<CadPoint3> BeforePoints { get; init; } = Array.Empty<CadPoint3>();

    public IReadOnlyList<CadPoint3> AfterPoints { get; init; } = Array.Empty<CadPoint3>();

    public double? DistanceMeters { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    public CadGeometryRepairConfidence Confidence { get; init; }
}

public sealed record CadGeometryRepairPlan
{
    public string Id { get; init; } = string.Empty;

    public CadGeometryRepairPlanStatus Status { get; init; }

    public IReadOnlyList<CadGeometryRepairAction> Actions { get; init; } = Array.Empty<CadGeometryRepairAction>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record CadGeometryRepairResult
{
    public CadGeometryRepairStatus Status { get; init; }

    public CadContourDocument? OriginalDocument { get; init; }

    public CadContourDocument? RepairedDocument { get; init; }

    public CadGeometryRepairPlan Plan { get; init; } = new();

    public IReadOnlyList<CadGeometryRepairAction> AppliedActions { get; init; } = Array.Empty<CadGeometryRepairAction>();

    public IReadOnlyList<CadGeometryRepairAction> SkippedActions { get; init; } = Array.Empty<CadGeometryRepairAction>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record CadGeneratedLineSegment2 : CadCurveSegment2
{
    public CadGeneratedLineSegment2(
        string repairActionId,
        int derivedOrder,
        string sourceLayer,
        CadPoint3 start,
        CadPoint3 end)
        : base(
            sourceOrder: int.MaxValue,
            segmentOrder: derivedOrder,
            sourceLayer,
            sourceEntityType: "REPAIR_LINE",
            start,
            end,
            CadContourMath.BoundsForPoints([start, end]))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repairActionId);
        ArgumentOutOfRangeException.ThrowIfNegative(derivedOrder);

        RepairActionId = repairActionId;
        DerivedOrder = derivedOrder;
    }

    public string RepairActionId { get; }

    public int DerivedOrder { get; }

    public override string Id => $"segment:{RepairActionId}:{DerivedOrder:D2}";
}
