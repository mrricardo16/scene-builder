using System.Security.Cryptography;
using System.Text;

namespace SceneBuilder.Domain;

public sealed class CadGeometryRepairAnalyzer
{
    public CadGeometryRepairPlan Analyze(
        CadContourDocument document,
        CadGeometryRepairPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var effectivePolicy = policy ?? new CadGeometryRepairPolicy();
        var segments = document.OpenSegments.OrderBy(segment => segment.Id, StringComparer.Ordinal).ToArray();
        var diagnostics = new List<SceneDiagnostic>();
        var actions = new List<CadGeometryRepairAction>();

        AnalyzeEndpointPairs(segments, effectivePolicy, actions, diagnostics);
        var duplicateSegmentIds = AnalyzeDuplicateLines(segments, effectivePolicy, actions, diagnostics);
        AnalyzeCollinearLines(segments, duplicateSegmentIds, effectivePolicy, actions, diagnostics);
        DetectBranching(segments, effectivePolicy, diagnostics);

        var orderedActions = actions
            .OrderBy(action => action.Id, StringComparer.Ordinal)
            .ToArray();
        DetectActionConflicts(orderedActions, diagnostics);

        var hasConflicts = diagnostics.Any(diagnostic => diagnostic.Code is
            "REPAIR_ACTION_CONFLICT" or "REPAIR_CHAIN_BRANCHING_CONFLICT");
        var status = hasConflicts
            ? CadGeometryRepairPlanStatus.HasConflicts
            : orderedActions.Length == 0
                ? CadGeometryRepairPlanStatus.NoChangesRequired
                : CadGeometryRepairPlanStatus.Ready;

        return new CadGeometryRepairPlan
        {
            Id = CadGeometryRepairIdentity.PlanId(orderedActions),
            Status = status,
            Actions = orderedActions,
            Diagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void AnalyzeEndpointPairs(
        IReadOnlyList<CadCurveSegment2> segments,
        CadGeometryRepairPolicy policy,
        ICollection<CadGeometryRepairAction> actions,
        ICollection<SceneDiagnostic> diagnostics)
    {
        var endpoints = segments
            .SelectMany(segment => new[]
            {
                new EndpointReference(segment, IsStart: true, segment.Start),
                new EndpointReference(segment, IsStart: false, segment.End)
            })
            .OrderBy(endpoint => endpoint.Segment.Id, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.IsStart ? 0 : 1)
            .ToArray();

        for (var left = 0; left < endpoints.Length; left++)
        {
            for (var right = left + 1; right < endpoints.Length; right++)
            {
                var first = endpoints[left];
                var second = endpoints[right];
                if (first.Segment.Id == second.Segment.Id)
                {
                    continue;
                }

                var distance = CadContourMath.Distance2(first.Point, second.Point);
                if (distance > policy.MaximumBridgeGapMeters ||
                    Math.Abs(first.Point.Z - second.Point.Z) > policy.MaximumElevationDifferenceMeters)
                {
                    continue;
                }

                if (!CanRepairTogether(first.Segment, second.Segment, policy))
                {
                    diagnostics.Add(Diagnostic(
                        "REPAIR_CROSS_LAYER_BLOCKED",
                        "A nearby endpoint candidate was blocked by the cross-layer policy."));
                    continue;
                }

                if (distance <= policy.DuplicateDistanceToleranceMeters)
                {
                    continue;
                }

                if (distance <= policy.EndpointSnapToleranceMeters &&
                    first.Segment is CadLineSegment2 && second.Segment is CadLineSegment2)
                {
                    var midpoint = new CadPoint3(
                        (first.Point.X + second.Point.X) / 2d,
                        (first.Point.Y + second.Point.Y) / 2d,
                        (first.Point.Z + second.Point.Z) / 2d);
                    actions.Add(Action(
                        CadGeometryRepairActionType.SnapEndpoint,
                        [first.Segment.Id, second.Segment.Id],
                        [first.Point, second.Point],
                        [midpoint, midpoint],
                        distance,
                        "REPAIR_ENDPOINT_SNAP_CANDIDATE",
                        CadGeometryRepairConfidence.High,
                        EndpointPairDiscriminator(first, second)));
                    continue;
                }

                if (distance <= policy.MaximumBridgeGapMeters)
                {
                    actions.Add(Action(
                        CadGeometryRepairActionType.BridgeGap,
                        [first.Segment.Id, second.Segment.Id],
                        [first.Point, second.Point],
                        [first.Point, second.Point],
                        distance,
                        "REPAIR_GAP_BRIDGE_CANDIDATE",
                        CadGeometryRepairConfidence.High,
                        EndpointPairDiscriminator(first, second)));
                }
            }
        }
    }

    private static HashSet<string> AnalyzeDuplicateLines(
        IReadOnlyList<CadCurveSegment2> segments,
        CadGeometryRepairPolicy policy,
        ICollection<CadGeometryRepairAction> actions,
        ICollection<SceneDiagnostic> diagnostics)
    {
        var duplicateSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        var lines = segments.OfType<CadLineSegment2>().ToArray();
        for (var left = 0; left < lines.Length; left++)
        {
            for (var right = left + 1; right < lines.Length; right++)
            {
                var first = lines[left];
                var second = lines[right];
                if (!CanRepairTogether(first, second, policy) ||
                    Math.Abs(first.Start.Z - second.Start.Z) > policy.MaximumElevationDifferenceMeters ||
                    Math.Abs(first.End.Z - second.End.Z) > policy.MaximumElevationDifferenceMeters)
                {
                    continue;
                }

                var sameDirection = PointsMatch(first.Start, second.Start, policy.DuplicateDistanceToleranceMeters) &&
                    PointsMatch(first.End, second.End, policy.DuplicateDistanceToleranceMeters);
                var reverseDirection = PointsMatch(first.Start, second.End, policy.DuplicateDistanceToleranceMeters) &&
                    PointsMatch(first.End, second.Start, policy.DuplicateDistanceToleranceMeters);
                if (!sameDirection && !reverseDirection)
                {
                    continue;
                }

                var actionType = sameDirection
                    ? CadGeometryRepairActionType.RemoveExactDuplicate
                    : CadGeometryRepairActionType.RemoveReverseDuplicate;
                actions.Add(Action(
                    actionType,
                    [first.Id, second.Id],
                    [first.Start, first.End, second.Start, second.End],
                    Array.Empty<CadPoint3>(),
                    null,
                    sameDirection ? "REPAIR_DUPLICATE_SEGMENT" : "REPAIR_REVERSE_DUPLICATE_SEGMENT",
                    CadGeometryRepairConfidence.Deterministic));
                duplicateSegmentIds.Add(first.Id);
                duplicateSegmentIds.Add(second.Id);
                diagnostics.Add(Diagnostic(
                    sameDirection ? "REPAIR_DUPLICATE_SEGMENT" : "REPAIR_REVERSE_DUPLICATE_SEGMENT",
                    "A fully duplicated line segment was found."));
            }
        }

        return duplicateSegmentIds;
    }

    private static void AnalyzeCollinearLines(
        IReadOnlyList<CadCurveSegment2> segments,
        IReadOnlySet<string> duplicateSegmentIds,
        CadGeometryRepairPolicy policy,
        ICollection<CadGeometryRepairAction> actions,
        ICollection<SceneDiagnostic> diagnostics)
    {
        var lines = segments
            .OfType<CadLineSegment2>()
            .Where(line => !duplicateSegmentIds.Contains(line.Id))
            .ToArray();
        for (var left = 0; left < lines.Length; left++)
        {
            for (var right = left + 1; right < lines.Length; right++)
            {
                var first = lines[left];
                var second = lines[right];
                if (!CanRepairTogether(first, second, policy) ||
                    !TryGetSharedEndpoint(first, second, policy.DuplicateDistanceToleranceMeters, out _) ||
                    !AreCollinear(first, second, policy))
                {
                    continue;
                }

                if (HasBranchAtSharedEndpoint(segments, first, second, policy.DuplicateDistanceToleranceMeters))
                {
                    diagnostics.Add(Diagnostic(
                        "REPAIR_CHAIN_BRANCHING_CONFLICT",
                        "A collinear merge candidate has a branching endpoint."));
                    continue;
                }

                var outerPoints = GetOuterPoints(first, second, policy.DuplicateDistanceToleranceMeters);
                actions.Add(Action(
                    CadGeometryRepairActionType.MergeCollinearSegments,
                    [first.Id, second.Id],
                    [first.Start, first.End, second.Start, second.End],
                    outerPoints,
                    null,
                    "REPAIR_COLLINEAR_MERGE_CANDIDATE",
                    CadGeometryRepairConfidence.High));
            }
        }
    }

    private static void DetectBranching(
        IReadOnlyList<CadCurveSegment2> segments,
        CadGeometryRepairPolicy policy,
        ICollection<SceneDiagnostic> diagnostics)
    {
        foreach (var segment in segments)
        {
            foreach (var endpoint in new[] { segment.Start, segment.End })
            {
                var degree = segments.Count(candidate =>
                    PointsMatch(candidate.Start, endpoint, policy.DuplicateDistanceToleranceMeters) ||
                    PointsMatch(candidate.End, endpoint, policy.DuplicateDistanceToleranceMeters));
                if (degree > 2)
                {
                    diagnostics.Add(Diagnostic(
                        "REPAIR_CHAIN_BRANCHING_CONFLICT",
                        "A segment chain contains a branching endpoint."));
                    return;
                }
            }
        }
    }

    private static void DetectActionConflicts(
        IReadOnlyList<CadGeometryRepairAction> actions,
        ICollection<SceneDiagnostic> diagnostics)
    {
        for (var left = 0; left < actions.Count; left++)
        {
            for (var right = left + 1; right < actions.Count; right++)
            {
                if (ActionsConflict(actions[left], actions[right]))
                {
                    diagnostics.Add(Diagnostic(
                        "REPAIR_ACTION_CONFLICT",
                        "Repair actions compete for the same source segment."));
                    return;
                }
            }
        }
    }

    private static bool ActionsConflict(CadGeometryRepairAction first, CadGeometryRepairAction second)
    {
        if (!first.SourceSegmentIds.Intersect(second.SourceSegmentIds, StringComparer.Ordinal).Any())
        {
            return false;
        }

        if (first.ActionType is CadGeometryRepairActionType.RemoveExactDuplicate or
                CadGeometryRepairActionType.RemoveReverseDuplicate or
                CadGeometryRepairActionType.MergeCollinearSegments ||
            second.ActionType is CadGeometryRepairActionType.RemoveExactDuplicate or
                CadGeometryRepairActionType.RemoveReverseDuplicate or
                CadGeometryRepairActionType.MergeCollinearSegments)
        {
            return true;
        }

        return first.BeforePoints.Any(firstPoint =>
            second.BeforePoints.Any(secondPoint => PointsMatch(firstPoint, secondPoint, CadGeometryTolerance.Default.PointEqualityMeters)));
    }

    private static bool CanRepairTogether(
        CadCurveSegment2 first,
        CadCurveSegment2 second,
        CadGeometryRepairPolicy policy) =>
        policy.AllowCrossLayerRepair || string.Equals(first.SourceLayer, second.SourceLayer, StringComparison.Ordinal);

    private static bool HasBranchAtSharedEndpoint(
        IReadOnlyList<CadCurveSegment2> segments,
        CadLineSegment2 first,
        CadLineSegment2 second,
        double tolerance) =>
        TryGetSharedEndpoint(first, second, tolerance, out var sharedEndpoint) &&
        segments.Count(segment =>
            PointsMatch(segment.Start, sharedEndpoint, tolerance) ||
            PointsMatch(segment.End, sharedEndpoint, tolerance)) > 2;

    private static bool TryGetSharedEndpoint(
        CadLineSegment2 first,
        CadLineSegment2 second,
        double tolerance,
        out CadPoint3 sharedEndpoint)
    {
        foreach (var firstPoint in new[] { first.Start, first.End })
        {
            if (PointsMatch(firstPoint, second.Start, tolerance) || PointsMatch(firstPoint, second.End, tolerance))
            {
                sharedEndpoint = firstPoint;
                return true;
            }
        }

        sharedEndpoint = null!;
        return false;
    }

    private static bool AreCollinear(CadLineSegment2 first, CadLineSegment2 second, CadGeometryRepairPolicy policy)
    {
        var firstDirection = Direction(first.Start, first.End);
        var secondDirection = Direction(second.Start, second.End);
        var angle = Math.Acos(Math.Clamp(Math.Abs((firstDirection.X * secondDirection.X) + (firstDirection.Y * secondDirection.Y)), -1d, 1d)) * 180d / Math.PI;
        return angle <= policy.CollinearAngleToleranceDegrees &&
            DistanceFromLine(second.Start, first) <= policy.CollinearDistanceToleranceMeters &&
            DistanceFromLine(second.End, first) <= policy.CollinearDistanceToleranceMeters;
    }

    private static IReadOnlyList<CadPoint3> GetOuterPoints(CadLineSegment2 first, CadLineSegment2 second, double tolerance)
    {
        var points = new[] { first.Start, first.End, second.Start, second.End };
        var shared = points.Where(point => points.Count(candidate => PointsMatch(point, candidate, tolerance)) > 1).ToArray();
        return points.Where(point => !shared.Any(candidate => PointsMatch(point, candidate, tolerance))).Take(2).ToArray();
    }

    private static (double X, double Y) Direction(CadPoint3 start, CadPoint3 end)
    {
        var length = CadContourMath.Distance2(start, end);
        return length == 0 ? (0, 0) : ((end.X - start.X) / length, (end.Y - start.Y) / length);
    }

    private static double DistanceFromLine(CadPoint3 point, CadLineSegment2 line)
    {
        var length = CadContourMath.Distance2(line.Start, line.End);
        return length == 0
            ? CadContourMath.Distance2(point, line.Start)
            : Math.Abs(((line.End.X - line.Start.X) * (line.Start.Y - point.Y)) -
                ((line.Start.X - point.X) * (line.End.Y - line.Start.Y))) / length;
    }

    private static bool PointsMatch(CadPoint3 first, CadPoint3 second, double tolerance) =>
        CadContourMath.AreEqual2(first, second, tolerance) && Math.Abs(first.Z - second.Z) <= tolerance;

    private static CadGeometryRepairAction Action(
        CadGeometryRepairActionType actionType,
        IReadOnlyList<string> sourceSegmentIds,
        IReadOnlyList<CadPoint3> beforePoints,
        IReadOnlyList<CadPoint3> afterPoints,
        double? distanceMeters,
        string reasonCode,
        CadGeometryRepairConfidence confidence,
        string identityDiscriminator = "")
    {
        var orderedSourceIds = sourceSegmentIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return new CadGeometryRepairAction
        {
            Id = CadGeometryRepairIdentity.ActionId(actionType, orderedSourceIds, reasonCode, identityDiscriminator),
            ActionType = actionType,
            SourceSegmentIds = orderedSourceIds,
            BeforePoints = beforePoints.ToArray(),
            AfterPoints = afterPoints.ToArray(),
            DistanceMeters = distanceMeters,
            ReasonCode = reasonCode,
            Confidence = confidence
        };
    }

    private static SceneDiagnostic Diagnostic(string code, string message) =>
        new() { Severity = DiagnosticSeverity.Warning, Code = code, Message = message };

    private static string EndpointPairDiscriminator(EndpointReference first, EndpointReference second) =>
        $"{(first.IsStart ? 'S' : 'E')}{(second.IsStart ? 'S' : 'E')}";

    private sealed record EndpointReference(CadCurveSegment2 Segment, bool IsStart, CadPoint3 Point);
}

public sealed class CadGeometryRepairApplier
{
    private readonly CadContourValidator _contourValidator;

    public CadGeometryRepairApplier(CadContourValidator? contourValidator = null)
    {
        _contourValidator = contourValidator ?? new CadContourValidator();
    }

    public CadGeometryRepairResult Apply(
        CadContourDocument originalDocument,
        CadGeometryRepairPlan plan,
        CadGeometryRepairPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(originalDocument);
        ArgumentNullException.ThrowIfNull(plan);
        var effectivePolicy = policy ?? new CadGeometryRepairPolicy();
        if (plan.Status is CadGeometryRepairPlanStatus.Failed)
        {
            return Failed(originalDocument, plan, "REPAIR_PLAN_FAILED", "The repair plan cannot be applied.");
        }

        var workingSegments = originalDocument.OpenSegments.ToList();
        var applied = new List<CadGeometryRepairAction>();
        var skipped = new List<CadGeometryRepairAction>();
        var diagnostics = new List<SceneDiagnostic>(plan.Diagnostics);
        if (plan.Status is CadGeometryRepairPlanStatus.HasConflicts)
        {
            skipped.AddRange(plan.Actions);
            diagnostics.Add(Diagnostic("REPAIR_ACTION_SKIPPED", "Conflicting repair actions were not applied."));
        }
        else
        {
            foreach (var action in plan.Actions)
            {
                if (!CanApply(action, effectivePolicy) || !ActionMatchesCurrentGeometry(workingSegments, action, effectivePolicy))
                {
                    skipped.Add(action);
                    diagnostics.Add(Diagnostic("REPAIR_ACTION_SKIPPED", "A repair action was not eligible for automatic application."));
                    continue;
                }

                var beforeAction = workingSegments.ToArray();
                if (!ApplyAction(workingSegments, originalDocument.Contours, action, effectivePolicy))
                {
                    skipped.Add(action);
                    diagnostics.Add(Diagnostic("REPAIR_RESULT_INVALID", "A repair action was rejected because it would create unsafe geometry."));
                    continue;
                }

                var validationDiagnostics = new List<SceneDiagnostic>();
                var candidate = BuildRepairedDocument(originalDocument, workingSegments, effectivePolicy, validationDiagnostics);
                if (candidate.Contours.OfType<CadSegmentContour>().Any(contour =>
                        contour.Id.StartsWith("chain:", StringComparison.Ordinal) &&
                        contour.ValidationState is not CadContourValidationState.Valid))
                {
                    workingSegments.Clear();
                    foreach (var segment in beforeAction)
                    {
                        workingSegments.Add(segment);
                    }

                    skipped.Add(action);
                    diagnostics.Add(Diagnostic("REPAIR_RESULT_INVALID", "A repair action was rolled back because SB-06 validation rejected the result."));
                    continue;
                }

                applied.Add(action);
            }
        }

        var repairedDocument = BuildRepairedDocument(originalDocument, workingSegments, effectivePolicy, diagnostics);
        var status = plan.Status switch
        {
            CadGeometryRepairPlanStatus.NoChangesRequired => CadGeometryRepairStatus.NoChangesRequired,
            CadGeometryRepairPlanStatus.HasConflicts => CadGeometryRepairStatus.PartiallySucceeded,
            _ when applied.Count == plan.Actions.Count => CadGeometryRepairStatus.Succeeded,
            _ when applied.Count > 0 => CadGeometryRepairStatus.PartiallySucceeded,
            _ => CadGeometryRepairStatus.Failed
        };

        return new CadGeometryRepairResult
        {
            Status = status,
            OriginalDocument = originalDocument,
            RepairedDocument = repairedDocument,
            Plan = plan,
            AppliedActions = applied,
            SkippedActions = skipped,
            Diagnostics = diagnostics
        };
    }

    private static bool CanApply(CadGeometryRepairAction action, CadGeometryRepairPolicy policy) =>
        action.Confidence is CadGeometryRepairConfidence.Deterministic or CadGeometryRepairConfidence.High &&
        action.ActionType switch
        {
            CadGeometryRepairActionType.SnapEndpoint => policy.AllowEndpointSnap,
            CadGeometryRepairActionType.BridgeGap => policy.AllowGapBridge,
            CadGeometryRepairActionType.RemoveExactDuplicate or CadGeometryRepairActionType.RemoveReverseDuplicate => policy.AllowDuplicateRemoval,
            CadGeometryRepairActionType.MergeCollinearSegments => policy.AllowCollinearMerge,
            CadGeometryRepairActionType.ReverseSegment => policy.AllowSegmentReversal,
            _ => false
        };

    private static bool ActionMatchesCurrentGeometry(
        IReadOnlyList<CadCurveSegment2> segments,
        CadGeometryRepairAction action,
        CadGeometryRepairPolicy policy)
    {
        var sources = segments
            .Where(segment => action.SourceSegmentIds.Contains(segment.Id, StringComparer.Ordinal))
            .OrderBy(segment => segment.Id, StringComparer.Ordinal)
            .ToArray();
        if (sources.Length != action.SourceSegmentIds.Distinct(StringComparer.Ordinal).Count() ||
            sources.Length == 0 ||
            (!policy.AllowCrossLayerRepair && sources.Select(segment => segment.SourceLayer).Distinct(StringComparer.Ordinal).Skip(1).Any()))
        {
            return false;
        }

        return action.ActionType switch
        {
            CadGeometryRepairActionType.SnapEndpoint =>
                sources.All(segment => segment is CadLineSegment2) &&
                action.BeforePoints.Count == 2 && action.AfterPoints.Count == 2 &&
                EndpointPairMatchesSources(sources, action.BeforePoints, policy.DuplicateDistanceToleranceMeters) &&
                DistanceWithin(action.BeforePoints[0], action.BeforePoints[1], 0d, policy.EndpointSnapToleranceMeters) &&
                Math.Abs(action.BeforePoints[0].Z - action.BeforePoints[1].Z) <= policy.MaximumElevationDifferenceMeters &&
                IsMidpointSnap(action, policy.DuplicateDistanceToleranceMeters),
            CadGeometryRepairActionType.BridgeGap =>
                action.BeforePoints.Count == 2 && action.AfterPoints.Count == 2 &&
                EndpointPairMatchesSources(sources, action.BeforePoints, policy.DuplicateDistanceToleranceMeters) &&
                PointsMatch(action.BeforePoints[0], action.AfterPoints[0], policy.DuplicateDistanceToleranceMeters) &&
                PointsMatch(action.BeforePoints[1], action.AfterPoints[1], policy.DuplicateDistanceToleranceMeters) &&
                DistanceWithin(action.BeforePoints[0], action.BeforePoints[1], policy.EndpointSnapToleranceMeters, policy.MaximumBridgeGapMeters),
            CadGeometryRepairActionType.RemoveExactDuplicate =>
                sources is [CadLineSegment2 first, CadLineSegment2 second] &&
                PointsMatch(first.Start, second.Start, policy.DuplicateDistanceToleranceMeters) &&
                PointsMatch(first.End, second.End, policy.DuplicateDistanceToleranceMeters),
            CadGeometryRepairActionType.RemoveReverseDuplicate =>
                sources is [CadLineSegment2 first, CadLineSegment2 second] &&
                PointsMatch(first.Start, second.End, policy.DuplicateDistanceToleranceMeters) &&
                PointsMatch(first.End, second.Start, policy.DuplicateDistanceToleranceMeters),
            CadGeometryRepairActionType.MergeCollinearSegments =>
                sources is [CadLineSegment2 first, CadLineSegment2 second] &&
                action.AfterPoints.Count == 2 &&
                TryGetSharedEndpoint(first, second, policy.DuplicateDistanceToleranceMeters, out var sharedEndpoint) &&
                AreCollinear(first, second, policy) &&
                !HasBranchAtSharedEndpoint(segments, sharedEndpoint, policy.DuplicateDistanceToleranceMeters) &&
                PointsMatchUnordered(action.AfterPoints, GetOuterPoints(first, second, policy.DuplicateDistanceToleranceMeters), policy.DuplicateDistanceToleranceMeters),
            _ => false
        };

    }

    private static bool IsMidpointSnap(CadGeometryRepairAction action, double tolerance)
    {
        var midpoint = new CadPoint3(
            (action.BeforePoints[0].X + action.BeforePoints[1].X) / 2d,
            (action.BeforePoints[0].Y + action.BeforePoints[1].Y) / 2d,
            (action.BeforePoints[0].Z + action.BeforePoints[1].Z) / 2d);
        return PointsMatch(action.AfterPoints[0], midpoint, tolerance) &&
            PointsMatch(action.AfterPoints[1], midpoint, tolerance);
    }

    private static bool EndpointPairMatchesSources(
        IReadOnlyList<CadCurveSegment2> sources,
        IReadOnlyList<CadPoint3> points,
        double tolerance)
    {
        if (sources.Count != 2 || points.Count != 2 || sources[0].Id == sources[1].Id)
        {
            return false;
        }

        var firstMatches = sources.Count(segment => EndpointBelongsToSegment(segment, points[0], tolerance));
        var secondMatches = sources.Count(segment => EndpointBelongsToSegment(segment, points[1], tolerance));
        return firstMatches == 1 && secondMatches == 1 &&
            ((EndpointBelongsToSegment(sources[0], points[0], tolerance) &&
              EndpointBelongsToSegment(sources[1], points[1], tolerance)) ||
             (EndpointBelongsToSegment(sources[1], points[0], tolerance) &&
              EndpointBelongsToSegment(sources[0], points[1], tolerance)));
    }

    private static bool EndpointBelongsToSegment(CadCurveSegment2 segment, CadPoint3 point, double tolerance) =>
        PointsMatch(segment.Start, point, tolerance) || PointsMatch(segment.End, point, tolerance);

    private static bool DistanceWithin(CadPoint3 first, CadPoint3 second, double exclusiveMinimum, double maximum) =>
        CadContourMath.Distance2(first, second) > exclusiveMinimum &&
        CadContourMath.Distance2(first, second) <= maximum;

    private static bool TryGetSharedEndpoint(
        CadLineSegment2 first,
        CadLineSegment2 second,
        double tolerance,
        out CadPoint3 sharedEndpoint)
    {
        foreach (var firstPoint in new[] { first.Start, first.End })
        {
            if (PointsMatch(firstPoint, second.Start, tolerance) || PointsMatch(firstPoint, second.End, tolerance))
            {
                sharedEndpoint = firstPoint;
                return true;
            }
        }

        sharedEndpoint = null!;
        return false;
    }

    private static bool AreCollinear(CadLineSegment2 first, CadLineSegment2 second, CadGeometryRepairPolicy policy)
    {
        var firstLength = CadContourMath.Distance2(first.Start, first.End);
        var secondLength = CadContourMath.Distance2(second.Start, second.End);
        if (firstLength == 0 || secondLength == 0)
        {
            return false;
        }

        var firstX = (first.End.X - first.Start.X) / firstLength;
        var firstY = (first.End.Y - first.Start.Y) / firstLength;
        var secondX = (second.End.X - second.Start.X) / secondLength;
        var secondY = (second.End.Y - second.Start.Y) / secondLength;
        var angle = Math.Acos(Math.Clamp(Math.Abs((firstX * secondX) + (firstY * secondY)), -1d, 1d)) * 180d / Math.PI;
        return angle <= policy.CollinearAngleToleranceDegrees &&
            DistanceFromLine(second.Start, first) <= policy.CollinearDistanceToleranceMeters &&
            DistanceFromLine(second.End, first) <= policy.CollinearDistanceToleranceMeters;
    }

    private static bool HasBranchAtSharedEndpoint(
        IReadOnlyList<CadCurveSegment2> segments,
        CadPoint3 sharedEndpoint,
        double tolerance) =>
        segments.Count(segment =>
            PointsMatch(segment.Start, sharedEndpoint, tolerance) ||
            PointsMatch(segment.End, sharedEndpoint, tolerance)) > 2;

    private static IReadOnlyList<CadPoint3> GetOuterPoints(CadLineSegment2 first, CadLineSegment2 second, double tolerance)
    {
        var points = new[] { first.Start, first.End, second.Start, second.End };
        var shared = points.Where(point => points.Count(candidate => PointsMatch(point, candidate, tolerance)) > 1).ToArray();
        return points.Where(point => !shared.Any(candidate => PointsMatch(point, candidate, tolerance))).Take(2).ToArray();
    }

    private static bool PointsMatchUnordered(
        IReadOnlyList<CadPoint3> first,
        IReadOnlyList<CadPoint3> second,
        double tolerance) =>
        first.Count == second.Count && first.All(point => second.Any(candidate => PointsMatch(point, candidate, tolerance)));

    private static double DistanceFromLine(CadPoint3 point, CadLineSegment2 line)
    {
        var length = CadContourMath.Distance2(line.Start, line.End);
        return length == 0
            ? CadContourMath.Distance2(point, line.Start)
            : Math.Abs(((line.End.X - line.Start.X) * (line.Start.Y - point.Y)) -
                ((line.Start.X - point.X) * (line.End.Y - line.Start.Y))) / length;
    }

    private static bool ApplyAction(
        IList<CadCurveSegment2> segments,
        IReadOnlyList<CadContour> protectedContours,
        CadGeometryRepairAction action,
        CadGeometryRepairPolicy policy) =>
        action.ActionType switch
        {
            CadGeometryRepairActionType.SnapEndpoint => ApplySnap(segments, action, policy),
            CadGeometryRepairActionType.BridgeGap => ApplyBridge(segments, protectedContours, action, policy),
            CadGeometryRepairActionType.RemoveExactDuplicate or CadGeometryRepairActionType.RemoveReverseDuplicate => ApplyDuplicateRemoval(segments, action),
            CadGeometryRepairActionType.MergeCollinearSegments => ApplyMerge(segments, action),
            _ => false
        };

    private static bool ApplySnap(IList<CadCurveSegment2> segments, CadGeometryRepairAction action, CadGeometryRepairPolicy policy)
    {
        if (action.BeforePoints.Count != 2 || action.AfterPoints.Count != 2)
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index] is not CadLineSegment2 line || !action.SourceSegmentIds.Contains(line.Id, StringComparer.Ordinal))
            {
                continue;
            }

            var start = MatchReplacement(line.Start, action.BeforePoints, action.AfterPoints, policy.DuplicateDistanceToleranceMeters);
            var end = MatchReplacement(line.End, action.BeforePoints, action.AfterPoints, policy.DuplicateDistanceToleranceMeters);
            if (start == line.Start && end == line.End)
            {
                continue;
            }

            segments[index] = new CadLineSegment2(line.SourceOrder, line.SegmentOrder, line.SourceLayer, line.SourceEntityType, start, end);
            changed = true;
        }

        return changed;
    }

    private static CadPoint3 MatchReplacement(
        CadPoint3 point,
        IReadOnlyList<CadPoint3> beforePoints,
        IReadOnlyList<CadPoint3> afterPoints,
        double tolerance)
    {
        for (var index = 0; index < beforePoints.Count; index++)
        {
            if (CadContourMath.AreEqual2(point, beforePoints[index], tolerance) &&
                Math.Abs(point.Z - beforePoints[index].Z) <= tolerance)
            {
                return afterPoints[index];
            }
        }

        return point;
    }

    private static bool ApplyBridge(
        IList<CadCurveSegment2> segments,
        IReadOnlyList<CadContour> protectedContours,
        CadGeometryRepairAction action,
        CadGeometryRepairPolicy policy)
    {
        if (action.AfterPoints.Count != 2)
        {
            return false;
        }

        var sourceLayer = segments.FirstOrDefault(segment => action.SourceSegmentIds.Contains(segment.Id, StringComparer.Ordinal))?.SourceLayer;
        if (string.IsNullOrWhiteSpace(sourceLayer))
        {
            return false;
        }

        if (BridgeIntersectsProtectedGeometry(segments, protectedContours, action, policy.DuplicateDistanceToleranceMeters))
        {
            return false;
        }

        segments.Add(new CadGeneratedLineSegment2(action.Id, 0, sourceLayer, action.AfterPoints[0], action.AfterPoints[1]));
        return true;
    }

    private static bool BridgeIntersectsProtectedGeometry(
        IEnumerable<CadCurveSegment2> segments,
        IEnumerable<CadContour> protectedContours,
        CadGeometryRepairAction action,
        double tolerance)
    {
        var bridgeStart = action.AfterPoints[0];
        var bridgeEnd = action.AfterPoints[1];
        foreach (var line in segments.OfType<CadLineSegment2>())
        {
            if (!LineSegmentsIntersect(bridgeStart, bridgeEnd, line.Start, line.End, tolerance))
            {
                continue;
            }

            var isPermittedSourceEndpoint = action.SourceSegmentIds.Contains(line.Id, StringComparer.Ordinal) &&
                (PointsMatch(bridgeStart, line.Start, tolerance) ||
                 PointsMatch(bridgeStart, line.End, tolerance) ||
                 PointsMatch(bridgeEnd, line.Start, tolerance) ||
                 PointsMatch(bridgeEnd, line.End, tolerance));
            if (!isPermittedSourceEndpoint)
            {
                return true;
            }
        }

        foreach (var arc in segments.OfType<CadArcSegment2>())
        {
            if (BridgeIntersectsArc(bridgeStart, bridgeEnd, arc, tolerance) &&
                !(action.SourceSegmentIds.Contains(arc.Id, StringComparer.Ordinal) &&
                  BridgeTouchesSegmentAtEndpoint(bridgeStart, bridgeEnd, arc, tolerance)))
            {
                return true;
            }
        }

        foreach (var contour in protectedContours)
        {
            if (contour is CadSegmentContour segmentContour && segmentContour.Segments.Any(segment =>
                    segment switch
                    {
                        CadLineSegment2 line => LineSegmentsIntersect(bridgeStart, bridgeEnd, line.Start, line.End, tolerance),
                        CadArcSegment2 arc => BridgeIntersectsArc(bridgeStart, bridgeEnd, arc, tolerance),
                        _ => false
                    }))
            {
                return true;
            }

            if (contour is CadCircleContour circle && BridgeIntersectsCircle(bridgeStart, bridgeEnd, circle.Center, circle.Radius, tolerance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BridgeTouchesSegmentAtEndpoint(
        CadPoint3 bridgeStart,
        CadPoint3 bridgeEnd,
        CadCurveSegment2 segment,
        double tolerance) =>
        PointsMatch(bridgeStart, segment.Start, tolerance) ||
        PointsMatch(bridgeStart, segment.End, tolerance) ||
        PointsMatch(bridgeEnd, segment.Start, tolerance) ||
        PointsMatch(bridgeEnd, segment.End, tolerance);

    private static bool BridgeIntersectsArc(
        CadPoint3 bridgeStart,
        CadPoint3 bridgeEnd,
        CadArcSegment2 arc,
        double tolerance)
    {
        var deltaX = bridgeEnd.X - bridgeStart.X;
        var deltaY = bridgeEnd.Y - bridgeStart.Y;
        var fromCenterX = bridgeStart.X - arc.Center.X;
        var fromCenterY = bridgeStart.Y - arc.Center.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= tolerance * tolerance)
        {
            return false;
        }

        var b = 2d * ((fromCenterX * deltaX) + (fromCenterY * deltaY));
        var c = (fromCenterX * fromCenterX) + (fromCenterY * fromCenterY) - (arc.Radius * arc.Radius);
        var discriminant = (b * b) - (4d * lengthSquared * c);
        if (discriminant < -tolerance)
        {
            return false;
        }

        var root = Math.Sqrt(Math.Max(0d, discriminant));
        return IsPointOnArcAtLineParameter((-b - root) / (2d * lengthSquared)) ||
            IsPointOnArcAtLineParameter((-b + root) / (2d * lengthSquared));

        bool IsPointOnArcAtLineParameter(double parameter)
        {
            if (parameter < -tolerance || parameter > 1d + tolerance)
            {
                return false;
            }

            var point = new CadPoint3(bridgeStart.X + (parameter * deltaX), bridgeStart.Y + (parameter * deltaY), bridgeStart.Z);
            var angle = Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X) * 180d / Math.PI;
            return CadContourMath.IsAngleOnArc(
                angle,
                arc.StartAngleDegrees,
                arc.EndAngleDegrees,
                arc.Direction);
        }
    }

    private static bool BridgeIntersectsCircle(
        CadPoint3 bridgeStart,
        CadPoint3 bridgeEnd,
        CadPoint3 center,
        double radius,
        double tolerance) =>
        DistanceToSegment(center, bridgeStart, bridgeEnd) <= radius + tolerance;

    private static double DistanceToSegment(CadPoint3 point, CadPoint3 start, CadPoint3 end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared == 0)
        {
            return CadContourMath.Distance2(point, start);
        }

        var parameter = Math.Clamp(((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared, 0d, 1d);
        var closest = new CadPoint3(start.X + (parameter * deltaX), start.Y + (parameter * deltaY), start.Z);
        return CadContourMath.Distance2(point, closest);
    }

    private static bool LineSegmentsIntersect(
        CadPoint3 firstStart,
        CadPoint3 firstEnd,
        CadPoint3 secondStart,
        CadPoint3 secondEnd,
        double tolerance)
    {
        var firstOrientation = Orientation(firstStart, firstEnd, secondStart);
        var secondOrientation = Orientation(firstStart, firstEnd, secondEnd);
        var thirdOrientation = Orientation(secondStart, secondEnd, firstStart);
        var fourthOrientation = Orientation(secondStart, secondEnd, firstEnd);
        if (((firstOrientation > tolerance && secondOrientation < -tolerance) ||
             (firstOrientation < -tolerance && secondOrientation > tolerance)) &&
            ((thirdOrientation > tolerance && fourthOrientation < -tolerance) ||
             (thirdOrientation < -tolerance && fourthOrientation > tolerance)))
        {
            return true;
        }

        return Math.Abs(firstOrientation) <= tolerance && OnSegment(firstStart, secondStart, firstEnd, tolerance) ||
            Math.Abs(secondOrientation) <= tolerance && OnSegment(firstStart, secondEnd, firstEnd, tolerance) ||
            Math.Abs(thirdOrientation) <= tolerance && OnSegment(secondStart, firstStart, secondEnd, tolerance) ||
            Math.Abs(fourthOrientation) <= tolerance && OnSegment(secondStart, firstEnd, secondEnd, tolerance);
    }

    private static double Orientation(CadPoint3 start, CadPoint3 end, CadPoint3 point) =>
        ((end.X - start.X) * (point.Y - start.Y)) - ((end.Y - start.Y) * (point.X - start.X));

    private static bool OnSegment(CadPoint3 start, CadPoint3 point, CadPoint3 end, double tolerance) =>
        point.X >= Math.Min(start.X, end.X) - tolerance &&
        point.X <= Math.Max(start.X, end.X) + tolerance &&
        point.Y >= Math.Min(start.Y, end.Y) - tolerance &&
        point.Y <= Math.Max(start.Y, end.Y) + tolerance;

    private static bool ApplyDuplicateRemoval(IList<CadCurveSegment2> segments, CadGeometryRepairAction action)
    {
        if (action.SourceSegmentIds.Count != 2)
        {
            return false;
        }

        var removeId = action.SourceSegmentIds.OrderBy(id => id, StringComparer.Ordinal).Last();
        var index = segments.ToList().FindIndex(segment => segment.Id == removeId);
        if (index < 0)
        {
            return false;
        }

        segments.RemoveAt(index);
        return true;
    }

    private static bool ApplyMerge(IList<CadCurveSegment2> segments, CadGeometryRepairAction action)
    {
        if (action.SourceSegmentIds.Count != 2 || action.AfterPoints.Count != 2)
        {
            return false;
        }

        var sourceSegments = segments.Where(segment => action.SourceSegmentIds.Contains(segment.Id, StringComparer.Ordinal)).ToArray();
        if (sourceSegments.Length != 2)
        {
            return false;
        }

        var sourceLayer = sourceSegments[0].SourceLayer;
        foreach (var segment in sourceSegments)
        {
            segments.Remove(segment);
        }

        segments.Add(new CadGeneratedLineSegment2(action.Id, 0, sourceLayer, action.AfterPoints[0], action.AfterPoints[1]));
        return true;
    }

    private CadContourDocument BuildRepairedDocument(
        CadContourDocument originalDocument,
        IReadOnlyList<CadCurveSegment2> segments,
        CadGeometryRepairPolicy policy,
        ICollection<SceneDiagnostic> diagnostics)
    {
        var contours = originalDocument.Contours.ToList();
        var openSegments = new List<CadCurveSegment2>();
        var unvisited = new HashSet<string>(segments.Select(segment => segment.Id), StringComparer.Ordinal);
        var chainIndex = 0;
        foreach (var seed in segments.OrderBy(segment => segment.Id, StringComparer.Ordinal))
        {
            if (!unvisited.Contains(seed.Id))
            {
                continue;
            }

            var component = FindComponent(seed, segments, unvisited, policy);
            foreach (var segment in component)
            {
                unvisited.Remove(segment.Id);
            }

            if (HasBranch(component, policy.DuplicateDistanceToleranceMeters))
            {
                diagnostics.Add(Diagnostic("REPAIR_CHAIN_BRANCHING_CONFLICT", "A repaired segment component has a branching endpoint."));
                openSegments.AddRange(component);
                continue;
            }

            var chain = BuildChain(
                component,
                policy.DuplicateDistanceToleranceMeters,
                policy.AllowSegmentReversal,
                out var isComplete);
            var isClosed = isComplete && chain.Count > 1 && PointsMatch(chain[^1].End, chain[0].Start, policy.DuplicateDistanceToleranceMeters);
            if (isClosed)
            {
                var contour = _contourValidator.Validate(
                    new CadSegmentContour($"chain:{chainIndex:D6}", chain, isSourceDefinedClosed: true),
                    new CadGeometryTolerance(
                        policy.DuplicateDistanceToleranceMeters,
                        policy.DuplicateDistanceToleranceMeters,
                        policy.MaximumElevationDifferenceMeters,
                        CadGeometryTolerance.Default.ZeroAreaSquareMeters,
                        CadGeometryTolerance.Default.ArcIntersectionSampleCount));
                contours.Add(contour);
            }
            else
            {
                openSegments.AddRange(isComplete ? chain : component);
            }

            chainIndex++;
        }

        return new CadContourDocument
        {
            Contours = contours,
            OpenSegments = openSegments.OrderBy(segment => segment.Id, StringComparer.Ordinal).ToArray(),
            Diagnostics = originalDocument.Diagnostics
        };
    }

    private static IReadOnlyList<CadCurveSegment2> FindComponent(
        CadCurveSegment2 seed,
        IReadOnlyList<CadCurveSegment2> allSegments,
        IReadOnlySet<string> availableIds,
        CadGeometryRepairPolicy policy)
    {
        var result = new List<CadCurveSegment2>();
        var pending = new Queue<CadCurveSegment2>();
        var discovered = new HashSet<string>(StringComparer.Ordinal) { seed.Id };
        pending.Enqueue(seed);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            result.Add(current);
            foreach (var candidate in allSegments.Where(candidate => availableIds.Contains(candidate.Id) && !discovered.Contains(candidate.Id)))
            {
                if (!LayersCanConnect(current, candidate, policy) ||
                    !Touches(current, candidate, policy.DuplicateDistanceToleranceMeters))
                {
                    continue;
                }

                discovered.Add(candidate.Id);
                pending.Enqueue(candidate);
            }
        }

        return result.OrderBy(segment => segment.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool LayersCanConnect(
        CadCurveSegment2 first,
        CadCurveSegment2 second,
        CadGeometryRepairPolicy policy) =>
        policy.AllowCrossLayerRepair || string.Equals(first.SourceLayer, second.SourceLayer, StringComparison.Ordinal);

    private static IReadOnlyList<CadCurveSegment2> BuildChain(
        IReadOnlyList<CadCurveSegment2> component,
        double tolerance,
        bool allowSegmentReversal,
        out bool isComplete)
    {
        var endpoint = component
            .SelectMany(segment => new[] { segment.Start, segment.End })
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ThenBy(point => point.Z)
            .FirstOrDefault(point => EndpointDegree(component, point, tolerance) == 1);
        var first = component.OrderBy(segment => segment.Id, StringComparer.Ordinal).First();
        var chain = new List<CadCurveSegment2>();
        var remaining = component.ToDictionary(segment => segment.Id, StringComparer.Ordinal);
        var current = endpoint is null
            ? first
            : component.First(segment => PointsMatch(segment.Start, endpoint, tolerance) || PointsMatch(segment.End, endpoint, tolerance));
        if (endpoint is not null && !PointsMatch(current.Start, endpoint, tolerance))
        {
            if (!allowSegmentReversal)
            {
                isComplete = false;
                return component;
            }

            current = Reverse(current);
        }

        while (true)
        {
            chain.Add(current);
            remaining.Remove(current.Id);
            var next = remaining.Values
                .Where(segment => PointsMatch(segment.Start, current.End, tolerance) || PointsMatch(segment.End, current.End, tolerance))
                .OrderBy(segment => segment.Id, StringComparer.Ordinal)
                .ToArray();
            if (next.Length != 1)
            {
                isComplete = remaining.Count == 0;
                break;
            }

            if (PointsMatch(next[0].Start, current.End, tolerance))
            {
                current = next[0];
                continue;
            }

            if (!allowSegmentReversal)
            {
                isComplete = false;
                return component;
            }

            current = Reverse(next[0]);
        }

        isComplete = remaining.Count == 0;
        return chain;
    }

    private static bool HasBranch(IReadOnlyList<CadCurveSegment2> component, double tolerance) =>
        component.SelectMany(segment => new[] { segment.Start, segment.End })
            .Any(point => EndpointDegree(component, point, tolerance) > 2);

    private static int EndpointDegree(IReadOnlyList<CadCurveSegment2> segments, CadPoint3 point, double tolerance) =>
        segments.Count(segment => PointsMatch(segment.Start, point, tolerance) || PointsMatch(segment.End, point, tolerance));

    private static bool Touches(CadCurveSegment2 first, CadCurveSegment2 second, double tolerance) =>
        PointsMatch(first.Start, second.Start, tolerance) ||
        PointsMatch(first.Start, second.End, tolerance) ||
        PointsMatch(first.End, second.Start, tolerance) ||
        PointsMatch(first.End, second.End, tolerance);

    private static CadCurveSegment2 Reverse(CadCurveSegment2 segment) =>
        segment switch
        {
            CadLineSegment2 line => new CadLineSegment2(line.SourceOrder, line.SegmentOrder, line.SourceLayer, line.SourceEntityType, line.End, line.Start),
            CadArcSegment2 arc => new CadArcSegment2(
                arc.SourceOrder,
                arc.SegmentOrder,
                arc.SourceLayer,
                arc.SourceEntityType,
                arc.Center,
                arc.Radius,
                arc.EndAngleDegrees,
                arc.StartAngleDegrees,
                arc.Direction is CadCurveDirection.CounterClockwise ? CadCurveDirection.Clockwise : CadCurveDirection.CounterClockwise,
                arc.End,
                arc.Start),
            _ => segment
        };

    private static bool PointsMatch(CadPoint3 first, CadPoint3 second, double tolerance) =>
        CadContourMath.AreEqual2(first, second, tolerance) && Math.Abs(first.Z - second.Z) <= tolerance;

    private static CadGeometryRepairResult Failed(
        CadContourDocument originalDocument,
        CadGeometryRepairPlan plan,
        string code,
        string message) =>
        new()
        {
            Status = CadGeometryRepairStatus.Failed,
            OriginalDocument = originalDocument,
            Plan = plan,
            Diagnostics = [Diagnostic(code, message)]
        };

    private static SceneDiagnostic Diagnostic(string code, string message) =>
        new() { Severity = DiagnosticSeverity.Warning, Code = code, Message = message };
}

internal static class CadGeometryRepairIdentity
{
    internal static string ActionId(
        CadGeometryRepairActionType actionType,
        IReadOnlyList<string> sourceSegmentIds,
        string reasonCode,
        string identityDiscriminator = "") =>
        $"repair-action:{StableToken($"{actionType}|{reasonCode}|{identityDiscriminator}|{string.Join('|', sourceSegmentIds)}")}";

    internal static string PlanId(IReadOnlyList<CadGeometryRepairAction> actions) =>
        $"repair-plan:{StableToken(string.Join('|', actions.Select(action => action.Id)))}";

    private static string StableToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}
