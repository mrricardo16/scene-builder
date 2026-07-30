namespace SceneBuilder.Domain;

public sealed class ScenePartitionPlanner
{
    private const string GlobalPartitionId = "partition-global";

    public ScenePartitionPlanResult Plan(SceneDraft draft, ScenePartitionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(policy);
        if (!IsValidPolicy(policy) || !IsValidDraft(draft))
        {
            return Failed("PARTITION_DRAFT_INVALID");
        }

        var nodes = draft.Nodes.ToDictionary(node => node.SemanticObjectId, StringComparer.Ordinal);
        var partitions = new Dictionary<PartitionKey, PartitionAccumulator>();
        var assignments = new List<ScenePartitionAssignment>();
        var diagnostics = new List<SceneDiagnostic>();
        var skipped = false;

        foreach (var semanticObject in draft.SemanticObjects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var node = nodes[semanticObject.Id];
            if (!TryGetAnchor(semanticObject, node, out var anchorX, out var anchorY) || semanticObject.Bounds.State is not CadBoundsState.Computed)
            {
                if (policy.InvalidBoundsBehavior is InvalidBoundsBehavior.Fail)
                {
                    return Failed("PARTITION_OBJECT_BOUNDS_INVALID");
                }

                skipped = true;
                diagnostics.Add(Diagnostic("PARTITION_OBJECT_SKIPPED", DiagnosticSeverity.Warning));
                continue;
            }

            if (!TryGetCoverage(semanticObject.Bounds, policy, out var coverage, out var coverageFailure) || !TryGetCell(anchorX, anchorY, policy, out var anchor))
            {
                return Failed(coverageFailure ?? "PARTITION_GRID_INDEX_OVERFLOW");
            }

            var isLarge = coverage.Count > policy.MaximumIntersectedCellsPerObject;
            if (isLarge && policy.LargeObjectBehavior is LargeSceneObjectBehavior.Fail)
            {
                return Failed("PARTITION_OBJECT_BOUNDS_INVALID");
            }

            PartitionKey owner;
            IReadOnlyList<string> intersected;
            if (isLarge && policy.LargeObjectBehavior is LargeSceneObjectBehavior.AssignToGlobalPartition)
            {
                owner = PartitionKey.Global;
                intersected = [GlobalPartitionId];
                diagnostics.Add(Diagnostic("PARTITION_OBJECT_ASSIGNED_GLOBAL", DiagnosticSeverity.Warning));
            }
            else if (isLarge)
            {
                owner = anchor;
                intersected = [owner.ToId()];
            }
            else
            {
                owner = anchor;
                intersected = coverage.ToIds().ToArray();
            }

            if (!Contains(semanticObject.Bounds, anchorX, anchorY))
            {
                diagnostics.Add(Diagnostic("PARTITION_ANCHOR_OUTSIDE_BOUNDS", DiagnosticSeverity.Warning));
            }

            if (!owner.IsGlobal && !CanRepresentCellBounds(owner, policy))
            {
                return Failed("PARTITION_GRID_INDEX_OVERFLOW");
            }

            var accumulator = GetOrCreate(partitions, owner, policy);
            accumulator.Add(semanticObject, node);
            assignments.Add(new ScenePartitionAssignment
            {
                SemanticObjectId = semanticObject.Id,
                SceneNodeId = node.Id,
                OwnerPartitionId = owner.ToId(),
                CrossesPartitionBoundary = coverage.Count > 1,
                IntersectedPartitionIds = intersected
            });
        }

        var plannedPartitions = partitions
            .OrderBy(pair => pair.Key.IsGlobal)
            .ThenBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Y)
            .Select(pair => pair.Value.ToPartition())
            .ToArray();
        var plan = new ScenePartitionPlan
        {
            Policy = policy,
            SceneBounds = CombineBounds(draft.SemanticObjects.Where(item => item.Bounds.State is CadBoundsState.Computed).Select(item => item.Bounds)),
            Partitions = plannedPartitions,
            Assignments = assignments.OrderBy(item => item.SemanticObjectId, StringComparer.Ordinal).ToArray(),
            Diagnostics = diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray()
        };
        return new ScenePartitionPlanResult
        {
            Status = skipped ? ScenePartitionPlanStatus.PartiallySucceeded : ScenePartitionPlanStatus.Succeeded,
            Plan = plan,
            Diagnostics = plan.Diagnostics
        };
    }

    private static bool IsValidPolicy(ScenePartitionPolicy policy) =>
        double.IsFinite(policy.CellSizeMeters) && policy.CellSizeMeters > 0 &&
        double.IsFinite(policy.OriginXMeters) && double.IsFinite(policy.OriginYMeters) &&
        policy.MaximumIntersectedCellsPerObject > 0 && Enum.IsDefined(policy.LargeObjectBehavior) && Enum.IsDefined(policy.InvalidBoundsBehavior);

    private static bool IsValidDraft(SceneDraft draft) =>
        !string.IsNullOrWhiteSpace(draft.Id) &&
        draft.SemanticObjects.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == draft.SemanticObjects.Count &&
        draft.Nodes.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == draft.Nodes.Count &&
        draft.Nodes.Select(item => item.SemanticObjectId).Distinct(StringComparer.Ordinal).Count() == draft.Nodes.Count &&
        draft.Nodes.Count == draft.SemanticObjects.Count &&
        draft.Nodes.All(node => draft.SemanticObjects.Any(item => item.Id == node.SemanticObjectId && item.Classification == node.Classification));

    private static bool TryGetAnchor(CadSemanticObject semanticObject, SceneNode node, out double x, out double y)
    {
        if (node.ContentKind is SceneNodeContentKind.StaticAssetReference or SceneNodeContentKind.DynamicAssetReference)
        {
            if (node.Transform is null || !double.IsFinite(node.Transform.Position.X) || !double.IsFinite(node.Transform.Position.Y))
            {
                x = y = 0;
                return false;
            }

            x = node.Transform.Position.X;
            y = node.Transform.Position.Y;
            return true;
        }

        var bounds = semanticObject.Bounds;
        if (bounds.State is not CadBoundsState.Computed)
        {
            x = y = 0;
            return false;
        }

        x = bounds.MinX + ((bounds.MaxX - bounds.MinX) / 2d);
        y = bounds.MinY + ((bounds.MaxY - bounds.MinY) / 2d);
        return double.IsFinite(x) && double.IsFinite(y);
    }

    private static bool Contains(CadBounds bounds, double x, double y) =>
        x >= bounds.MinX && x <= bounds.MaxX && y >= bounds.MinY && y <= bounds.MaxY;

    private static bool TryGetCoverage(CadBounds bounds, ScenePartitionPolicy policy, out Coverage coverage, out string? failure)
    {
        coverage = default;
        failure = null;
        if (bounds.State is not CadBoundsState.Computed ||
            !TryGetIndex(bounds.MinX, policy.OriginXMeters, policy.CellSizeMeters, out var minX) ||
            !TryGetIndex(bounds.MinY, policy.OriginYMeters, policy.CellSizeMeters, out var minY) ||
            !TryGetIndex(bounds.MinX == bounds.MaxX ? bounds.MaxX : Math.BitDecrement(bounds.MaxX), policy.OriginXMeters, policy.CellSizeMeters, out var maxX) ||
            !TryGetIndex(bounds.MinY == bounds.MaxY ? bounds.MaxY : Math.BitDecrement(bounds.MaxY), policy.OriginYMeters, policy.CellSizeMeters, out var maxY))
        {
            failure = "PARTITION_GRID_INDEX_OVERFLOW";
            return false;
        }

        try
        {
            var count = checked(((long)maxX - minX + 1) * ((long)maxY - minY + 1));
            if (count <= 0)
            {
                failure = "PARTITION_GRID_INDEX_OVERFLOW";
                return false;
            }

            coverage = new Coverage(minX, maxX, minY, maxY, count);
            return true;
        }
        catch (OverflowException)
        {
            failure = "PARTITION_GRID_INDEX_OVERFLOW";
            return false;
        }
    }

    private static bool TryGetCell(double x, double y, ScenePartitionPolicy policy, out PartitionKey key)
    {
        if (!TryGetIndex(x, policy.OriginXMeters, policy.CellSizeMeters, out var ix) ||
            !TryGetIndex(y, policy.OriginYMeters, policy.CellSizeMeters, out var iy))
        {
            key = default;
            return false;
        }

        key = new PartitionKey(ix, iy, false);
        return true;
    }

    private static bool CanRepresentCellBounds(PartitionKey key, ScenePartitionPolicy policy)
    {
        var minX = policy.OriginXMeters + (key.X * policy.CellSizeMeters);
        var minY = policy.OriginYMeters + (key.Y * policy.CellSizeMeters);
        return double.IsFinite(minX) && double.IsFinite(minY) &&
            double.IsFinite(minX + policy.CellSizeMeters) &&
            double.IsFinite(minY + policy.CellSizeMeters);
    }

    private static bool TryGetIndex(double coordinate, double origin, double cellSize, out int index)
    {
        var value = Math.Floor((coordinate - origin) / cellSize);
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            index = 0;
            return false;
        }

        index = checked((int)value);
        return true;
    }

    private static PartitionAccumulator GetOrCreate(IDictionary<PartitionKey, PartitionAccumulator> partitions, PartitionKey key, ScenePartitionPolicy policy)
    {
        if (!partitions.TryGetValue(key, out var accumulator))
        {
            accumulator = new PartitionAccumulator(key, policy);
            partitions.Add(key, accumulator);
        }

        return accumulator;
    }

    private static CadBounds CombineBounds(IEnumerable<CadBounds> bounds)
    {
        var values = bounds.ToArray();
        return values.Length == 0 ? CadBounds.Empty : CadBounds.Computed(values.Min(item => item.MinX), values.Min(item => item.MinY), values.Min(item => item.MinZ), values.Max(item => item.MaxX), values.Max(item => item.MaxY), values.Max(item => item.MaxZ));
    }

    private static ScenePartitionPlanResult Failed(string code) => new() { Status = ScenePartitionPlanStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Scene partition planning did not complete normally." };

    private readonly record struct PartitionKey(int X, int Y, bool IsGlobal)
    {
        public static PartitionKey Global { get; } = new(0, 0, true);

        public string ToId() => IsGlobal ? GlobalPartitionId : $"partition-x-{Format(X)}-y-{Format(Y)}";

        private static string Format(int value) => value >= 0 ? $"p{value:D6}" : $"m{Math.Abs((long)value):D6}";
    }

    private readonly record struct Coverage(int MinX, int MaxX, int MinY, int MaxY, long Count)
    {
        public IEnumerable<string> ToIds()
        {
            var x = MinX;
            while (true)
            {
                var y = MinY;
                while (true)
                {
                    yield return new PartitionKey(x, y, false).ToId();
                    if (y == MaxY) break;
                    y++;
                }

                if (x == MaxX) break;
                x++;
            }
        }
    }

    private sealed class PartitionAccumulator(PartitionKey key, ScenePartitionPolicy policy)
    {
        private readonly List<CadSemanticObject> _objects = [];
        private readonly List<SceneNode> _nodes = [];

        public void Add(CadSemanticObject semanticObject, SceneNode node)
        {
            _objects.Add(semanticObject);
            _nodes.Add(node);
        }

        public ScenePartition ToPartition() => new()
        {
            Id = key.ToId(),
            XIndex = key.IsGlobal ? null : key.X,
            YIndex = key.IsGlobal ? null : key.Y,
            CellBounds = key.IsGlobal ? CadBounds.NotEvaluated : CellBounds(key, policy),
            ContentBounds = CombineBounds(_objects.Select(item => item.Bounds)),
            SemanticObjectIds = _objects.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            SceneNodeIds = _nodes.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray()
        };

        private static CadBounds CellBounds(PartitionKey key, ScenePartitionPolicy policy)
        {
            var minX = policy.OriginXMeters + (key.X * policy.CellSizeMeters);
            var minY = policy.OriginYMeters + (key.Y * policy.CellSizeMeters);
            return CadBounds.Computed(minX, minY, 0, minX + policy.CellSizeMeters, minY + policy.CellSizeMeters, 0);
        }
    }
}
