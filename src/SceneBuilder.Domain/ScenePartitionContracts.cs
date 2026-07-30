namespace SceneBuilder.Domain;

public enum LargeSceneObjectBehavior
{
    AssignToGlobalPartition = 0,
    AssignToAnchorPartition = 1,
    Fail = 2
}

public enum InvalidBoundsBehavior
{
    Skip = 0,
    Fail = 1
}

public enum ScenePartitionPlanStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2
}

public sealed record ScenePartitionPolicy
{
    public double CellSizeMeters { get; init; } = 100d;

    public double OriginXMeters { get; init; }

    public double OriginYMeters { get; init; }

    public int MaximumIntersectedCellsPerObject { get; init; } = 16;

    public LargeSceneObjectBehavior LargeObjectBehavior { get; init; } = LargeSceneObjectBehavior.AssignToGlobalPartition;

    public InvalidBoundsBehavior InvalidBoundsBehavior { get; init; } = InvalidBoundsBehavior.Fail;
}

public sealed record ScenePartitionPlan
{
    public ScenePartitionPolicy Policy { get; init; } = new();

    public CadBounds SceneBounds { get; init; } = CadBounds.NotEvaluated;

    public IReadOnlyList<ScenePartition> Partitions { get; init; } = Array.Empty<ScenePartition>();

    public IReadOnlyList<ScenePartitionAssignment> Assignments { get; init; } = Array.Empty<ScenePartitionAssignment>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record ScenePartition
{
    public string Id { get; init; } = string.Empty;

    public int? XIndex { get; init; }

    public int? YIndex { get; init; }

    public CadBounds CellBounds { get; init; } = CadBounds.NotEvaluated;

    public CadBounds ContentBounds { get; init; } = CadBounds.NotEvaluated;

    public IReadOnlyList<string> SemanticObjectIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SceneNodeIds { get; init; } = Array.Empty<string>();
}

public sealed record ScenePartitionAssignment
{
    public string SemanticObjectId { get; init; } = string.Empty;

    public string SceneNodeId { get; init; } = string.Empty;

    public string OwnerPartitionId { get; init; } = string.Empty;

    public bool CrossesPartitionBoundary { get; init; }

    public IReadOnlyList<string> IntersectedPartitionIds { get; init; } = Array.Empty<string>();
}

public sealed record ScenePartitionPlanResult
{
    public ScenePartitionPlanStatus Status { get; init; } = ScenePartitionPlanStatus.Failed;

    public ScenePartitionPlan? Plan { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}
