namespace SceneBuilder.Domain.Tests;

public sealed class CadGeometryRepairTests
{
    [Fact]
    public void Repair_contract_collections_default_to_non_null_empty_values()
    {
        Assert.Empty(new CadGeometryRepairAction().SourceSegmentIds);
        Assert.Empty(new CadGeometryRepairAction().BeforePoints);
        Assert.Empty(new CadGeometryRepairAction().AfterPoints);
        Assert.Empty(new CadGeometryRepairPlan().Actions);
        Assert.Empty(new CadGeometryRepairPlan().Diagnostics);
        Assert.Empty(new CadGeometryRepairResult().AppliedActions);
        Assert.Empty(new CadGeometryRepairResult().SkippedActions);
        Assert.Empty(new CadGeometryRepairResult().Diagnostics);
    }
    [Fact]
    public void Repair_policy_rejects_invalid_tolerances_and_defaults_to_same_layer_only()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadGeometryRepairPolicy(endpointSnapToleranceMeters: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadGeometryRepairPolicy(endpointSnapToleranceMeters: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadGeometryRepairPolicy(endpointSnapToleranceMeters: double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadGeometryRepairPolicy(maximumBridgeGapMeters: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadGeometryRepairPolicy(collinearAngleToleranceDegrees: 181));

        var policy = new CadGeometryRepairPolicy();
        Assert.False(policy.AllowCrossLayerRepair);
        Assert.True(policy.AllowEndpointSnap);
        Assert.True(policy.AllowGapBridge);
    }

    [Fact]
    public void Segment_ids_are_stable_and_do_not_include_source_layer()
    {
        var segment = Line(12, 3, "SYN_PRIVATE_NAME", 0, 0, 1, 0);

        Assert.Equal("segment:000012:000003", segment.Id);
        Assert.DoesNotContain("SYN_PRIVATE_NAME", segment.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_creates_stable_same_layer_snap_action_without_mutating_original_segments()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1.001, 0, 2, 0));
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);

        var first = new CadGeometryRepairAnalyzer().Analyze(original, policy);
        var second = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var action = Assert.Single(first.Actions);
        Assert.Equal(CadGeometryRepairActionType.SnapEndpoint, action.ActionType);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(action.Id, Assert.Single(second.Actions).Id);
        Assert.Equal(["segment:000001:000000", "segment:000002:000000"], action.SourceSegmentIds);
        Assert.Equal(new CadPoint3(1, 0, 0), original.OpenSegments[0].End);
    }

    [Fact]
    public void Analyzer_assigns_distinct_stable_ids_to_multiple_endpoint_candidates_for_the_same_segments()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 0.001, 0.001, 1.001, 0.001));
        var policy = new CadGeometryRepairPolicy();

        var first = new CadGeometryRepairAnalyzer().Analyze(original, policy);
        var second = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        Assert.Equal(2, first.Actions.Count);
        Assert.Equal(2, first.Actions.Select(action => action.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(first.Actions.Select(action => action.Id), second.Actions.Select(action => action.Id));
    }

    [Fact]
    public void Analyzer_blocks_cross_layer_and_non_planar_endpoint_candidates_by_default()
    {
        var crossLayer = new CadGeometryRepairAnalyzer().Analyze(
            OpenDocument(Line(1, 0, "SYN_A", 0, 0, 1, 0), Line(2, 0, "SYN_B", 1.001, 0, 2, 0)),
            new CadGeometryRepairPolicy());
        Assert.Empty(crossLayer.Actions);
        Assert.Contains(crossLayer.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_CROSS_LAYER_BLOCKED");

        var nonPlanar = new CadGeometryRepairAnalyzer().Analyze(
            OpenDocument(Line(1, 0, "SYN", 0, 0, 1, 0), Line(2, 0, "SYN", 1.001, 0, 2, 0, startZ: 1)),
            new CadGeometryRepairPolicy());
        Assert.Empty(nonPlanar.Actions);
    }

    [Fact]
    public void Analyzer_allows_cross_layer_snap_only_when_the_policy_explicitly_enables_it()
    {
        var document = OpenDocument(
            Line(1, 0, "SYN_A", 0, 0, 1, 0),
            Line(2, 0, "SYN_B", 1.001, 0, 2, 0));
        var policy = new CadGeometryRepairPolicy(allowCrossLayerRepair: true);

        var plan = new CadGeometryRepairAnalyzer().Analyze(document, policy);

        Assert.Equal(CadGeometryRepairPlanStatus.Ready, plan.Status);
        Assert.Equal(CadGeometryRepairActionType.SnapEndpoint, Assert.Single(plan.Actions).ActionType);
    }

    [Fact]
    public void Analyzer_creates_bridge_for_small_gap_and_not_for_large_gap()
    {
        var analyzer = new CadGeometryRepairAnalyzer();
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);

        var smallGap = analyzer.Analyze(
            OpenDocument(Line(1, 0, "SYN", 0, 0, 1, 0), Line(2, 0, "SYN", 1.01, 0, 2, 0)),
            policy);
        var bridge = Assert.Single(smallGap.Actions);
        Assert.Equal(CadGeometryRepairActionType.BridgeGap, bridge.ActionType);
        Assert.Equal(CadGeometryRepairConfidence.High, bridge.Confidence);

        var largeGap = analyzer.Analyze(
            OpenDocument(Line(1, 0, "SYN", 0, 0, 1, 0), Line(2, 0, "SYN", 1.1, 0, 2, 0)),
            policy);
        Assert.Empty(largeGap.Actions);
    }

    [Fact]
    public void Analyzer_distinguishes_exact_and_reverse_duplicate_lines()
    {
        var plan = new CadGeometryRepairAnalyzer().Analyze(
            OpenDocument(
                Line(1, 0, "SYN", 0, 0, 1, 0),
                Line(2, 0, "SYN", 0, 0, 1, 0),
                Line(3, 0, "SYN", 1, 0, 0, 0)),
            new CadGeometryRepairPolicy());

        Assert.Contains(plan.Actions, action => action.ActionType == CadGeometryRepairActionType.RemoveExactDuplicate);
        Assert.Contains(plan.Actions, action => action.ActionType == CadGeometryRepairActionType.RemoveReverseDuplicate);
    }

    [Fact]
    public void Analyzer_does_not_remove_partial_parallel_cross_layer_or_arc_duplicates()
    {
        var plan = new CadGeometryRepairAnalyzer().Analyze(
            OpenDocument(
                Line(1, 0, "SYN_A", 0, 0, 2, 0),
                Line(2, 0, "SYN_A", 1, 0, 3, 0),
                Line(3, 0, "SYN_A", 0, 1, 2, 1),
                Line(4, 0, "SYN_B", 0, 0, 2, 0),
                new CadArcSegment2(5, 0, "SYN_A", "ARC", new CadPoint3(0, 1, 0), 1, 270, 0, CadCurveDirection.CounterClockwise),
                new CadArcSegment2(6, 0, "SYN_A", "ARC", new CadPoint3(0, 1, 0), 1, 270, 0, CadCurveDirection.CounterClockwise)),
            new CadGeometryRepairPolicy());

        Assert.DoesNotContain(plan.Actions, action => action.ActionType is
            CadGeometryRepairActionType.RemoveExactDuplicate or
            CadGeometryRepairActionType.RemoveReverseDuplicate);
    }

    [Theory]
    [InlineData(false, CadGeometryRepairActionType.RemoveExactDuplicate)]
    [InlineData(true, CadGeometryRepairActionType.RemoveReverseDuplicate)]
    public void Applier_removes_one_duplicate_line_with_stable_retention(bool reverse, CadGeometryRepairActionType actionType)
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            reverse ? Line(2, 0, "SYN", 1, 0, 0, 0) : Line(2, 0, "SYN", 0, 0, 1, 0));
        var policy = new CadGeometryRepairPolicy();
        var plan = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var result = new CadGeometryRepairApplier().Apply(original, plan, policy);

        Assert.Equal(CadGeometryRepairPlanStatus.Ready, plan.Status);
        Assert.Equal(actionType, Assert.Single(plan.Actions).ActionType);
        Assert.Equal(CadGeometryRepairStatus.Succeeded, result.Status);
        Assert.Equal("segment:000001:000000", Assert.Single(result.RepairedDocument!.OpenSegments).Id);
    }

    [Fact]
    public void Analyzer_merges_unbranched_collinear_lines_but_reports_branch_conflicts()
    {
        var policy = new CadGeometryRepairPolicy();
        var mergePlan = new CadGeometryRepairAnalyzer().Analyze(
            OpenDocument(Line(1, 0, "SYN", 0, 0, 1, 0), Line(2, 0, "SYN", 1, 0, 2, 0)),
            policy);
        Assert.Contains(mergePlan.Actions, action => action.ActionType == CadGeometryRepairActionType.MergeCollinearSegments);

        var branchPlan = new CadGeometryRepairAnalyzer().Analyze(
            OpenDocument(
                Line(1, 0, "SYN", 0, 0, 1, 0),
                Line(2, 0, "SYN", 1, 0, 2, 0),
                Line(3, 0, "SYN", 1, 0, 1, 1)),
            policy);
        Assert.Equal(CadGeometryRepairPlanStatus.HasConflicts, branchPlan.Status);
        Assert.Contains(branchPlan.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_CHAIN_BRANCHING_CONFLICT");
    }

    [Fact]
    public void Conflict_plan_keeps_original_geometry_and_returns_partially_succeeded_result()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1, 0, 2, 0),
            Line(3, 0, "SYN", 1, 0, 1, 1));
        var plan = new CadGeometryRepairAnalyzer().Analyze(original);

        var result = new CadGeometryRepairApplier().Apply(original, plan);

        Assert.Equal(CadGeometryRepairStatus.PartiallySucceeded, result.Status);
        Assert.Same(original, result.OriginalDocument);
        Assert.Equal(3, original.OpenSegments.Count);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_ACTION_SKIPPED");
    }

    [Fact]
    public void Applier_merges_unbranched_collinear_lines_into_a_generated_audited_segment()
    {
        var original = OpenDocument(Line(1, 0, "SYN", 0, 0, 1, 0), Line(2, 0, "SYN", 1, 0, 2, 0));
        var policy = new CadGeometryRepairPolicy();
        var plan = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var result = new CadGeometryRepairApplier().Apply(original, plan, policy);

        Assert.Equal(CadGeometryRepairStatus.Succeeded, result.Status);
        var segment = Assert.IsType<CadGeneratedLineSegment2>(Assert.Single(result.RepairedDocument!.OpenSegments));
        Assert.Equal(new CadPoint3(0, 0, 0), segment.Start);
        Assert.Equal(new CadPoint3(2, 0, 0), segment.End);
        Assert.Equal(2, original.OpenSegments.Count);
    }

    [Fact]
    public void Arc_endpoints_can_participate_in_bridge_without_redefining_the_arc()
    {
        var arc = new CadArcSegment2(1, 0, "SYN", "ARC", new CadPoint3(0, 1, 0), 1, 270, 0, CadCurveDirection.CounterClockwise);
        var line = Line(2, 0, "SYN", 1.01, 1, 2, 1);
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);
        var plan = new CadGeometryRepairAnalyzer().Analyze(OpenDocument(arc, line), policy);

        var result = new CadGeometryRepairApplier().Apply(OpenDocument(arc, line), plan, policy);

        Assert.Equal(CadGeometryRepairActionType.BridgeGap, Assert.Single(plan.Actions).ActionType);
        Assert.Equal(3, result.RepairedDocument!.OpenSegments.Count);
        Assert.Same(arc, result.OriginalDocument!.OpenSegments[0]);
    }

    [Fact]
    public void Applier_applies_controlled_actions_keeps_original_document_and_revalidates_closed_chain()
    {
        var original = OpenDocument(
            Line(3, 0, "SYN", 1, 1, 0, 1),
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(4, 0, "SYN", 0, 1, 0, 0),
            Line(2, 0, "SYN", 1, 0, 1, 1));
        var policy = new CadGeometryRepairPolicy();
        var plan = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var result = new CadGeometryRepairApplier().Apply(original, plan, policy);

        Assert.Equal(CadGeometryRepairStatus.NoChangesRequired, result.Status);
        Assert.Same(original, result.OriginalDocument);
        var contour = Assert.IsType<CadSegmentContour>(Assert.Single(result.RepairedDocument!.Contours));
        Assert.Equal(CadContourValidationState.Valid, contour.ValidationState);
        Assert.Equal(1, contour.SignedAreaSquareMeters, precision: 10);
        Assert.Equal(new CadPoint3(1, 1, 0), original.OpenSegments[0].Start);
    }

    [Fact]
    public void Applier_applies_safe_bridge_and_keeps_audit_separate_from_skipped_conflicts()
    {
        var original = OpenDocument(Line(1, 0, "SYN", 0, 0, 1, 0), Line(2, 0, "SYN", 1.01, 0, 2, 0));
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);
        var plan = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var result = new CadGeometryRepairApplier().Apply(original, plan, policy);

        Assert.Equal(CadGeometryRepairStatus.Succeeded, result.Status);
        Assert.Single(result.AppliedActions);
        Assert.Empty(result.SkippedActions);
        Assert.Equal(3, result.RepairedDocument!.OpenSegments.Count);
        Assert.Equal(2, original.OpenSegments.Count);
    }

    [Fact]
    public void Applier_rejects_bridge_that_crosses_an_unrelated_segment()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1.01, 0, 2, 0),
            Line(3, 0, "SYN", 1.005, -1, 1.005, 1));
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);
        var plan = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var result = new CadGeometryRepairApplier().Apply(original, plan, policy);

        Assert.Equal(CadGeometryRepairPlanStatus.Ready, plan.Status);
        Assert.Equal(CadGeometryRepairStatus.Failed, result.Status);
        Assert.Empty(result.AppliedActions);
        Assert.Single(result.SkippedActions);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_RESULT_INVALID");
        Assert.Equal(3, original.OpenSegments.Count);
    }

    [Fact]
    public void Applier_rejects_bridge_that_intersects_a_source_defined_contour()
    {
        var original = new CadContourDocument
        {
            OpenSegments = [
                Line(1, 0, "SYN", 0, 0, 1, 0),
                Line(2, 0, "SYN", 1.01, 0, 2, 0)
            ],
            Contours = [
                new CadSegmentContour(
                    "contour:000010",
                    [Line(10, 0, "SYN_CONTOUR", 1.005, -1, 1.005, 1)],
                    isSourceDefinedClosed: true)
            ]
        };
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);

        var result = new CadGeometryRepairApplier().Apply(original, new CadGeometryRepairAnalyzer().Analyze(original, policy), policy);

        Assert.Equal(CadGeometryRepairStatus.Failed, result.Status);
        Assert.Empty(result.AppliedActions);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_RESULT_INVALID");
    }

    [Fact]
    public void Applier_rejects_bridge_that_intersects_an_open_arc()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1.01, 0, 2, 0),
            new CadArcSegment2(3, 0, "SYN", "ARC", new CadPoint3(1.005, 0.1, 0), 0.1, 180, 360, CadCurveDirection.CounterClockwise));
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);

        var result = new CadGeometryRepairApplier().Apply(original, new CadGeometryRepairAnalyzer().Analyze(original, policy), policy);

        Assert.Equal(CadGeometryRepairStatus.Failed, result.Status);
        Assert.Empty(result.AppliedActions);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_RESULT_INVALID");
    }

    [Fact]
    public void Four_small_independent_endpoint_gaps_form_a_ready_plan_and_a_valid_repaired_rectangle()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1.001, 0, 1.001, 1),
            Line(3, 0, "SYN", 1.001, 1.001, 0, 1.001),
            Line(4, 0, "SYN", 0, 1, 0, 0.001));
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);
        var plan = new CadGeometryRepairAnalyzer().Analyze(original, policy);

        var result = new CadGeometryRepairApplier().Apply(original, plan, policy);

        Assert.Equal(CadGeometryRepairPlanStatus.Ready, plan.Status);
        Assert.Equal(4, plan.Actions.Count);
        Assert.Equal(CadGeometryRepairStatus.Succeeded, result.Status);
        var contour = Assert.IsType<CadSegmentContour>(Assert.Single(result.RepairedDocument!.Contours));
        Assert.Equal(CadContourValidationState.Valid, contour.ValidationState);
    }

    [Fact]
    public void Applier_does_not_build_a_closed_chain_from_cross_layer_segments_by_default()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN_A", 0, 0, 1, 0),
            Line(2, 0, "SYN_B", 1, 0, 1, 1),
            Line(3, 0, "SYN_A", 1, 1, 0, 1),
            Line(4, 0, "SYN_B", 0, 1, 0, 0));

        var result = new CadGeometryRepairApplier().Apply(
            original,
            new CadGeometryRepairAnalyzer().Analyze(original),
            new CadGeometryRepairPolicy());

        Assert.Empty(result.RepairedDocument!.Contours);
        Assert.Equal(4, result.RepairedDocument.OpenSegments.Count);
    }

    [Fact]
    public void Applier_keeps_a_chain_open_when_segment_reversal_is_disabled()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1, 0, 1, 1),
            Line(3, 0, "SYN", 0, 1, 1, 1),
            Line(4, 0, "SYN", 0, 1, 0, 0));
        var policy = new CadGeometryRepairPolicy(allowSegmentReversal: false);

        var result = new CadGeometryRepairApplier().Apply(original, new CadGeometryRepairAnalyzer().Analyze(original, policy), policy);

        Assert.Empty(result.RepairedDocument!.Contours);
        Assert.Equal(4, result.RepairedDocument.OpenSegments.Count);
    }

    [Fact]
    public void Applier_rejects_a_forged_cross_layer_bridge_plan()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN_A", 0, 0, 1, 0),
            Line(2, 0, "SYN_B", 1.01, 0, 2, 0));
        var action = new CadGeometryRepairAction
        {
            Id = "repair-action:forged",
            ActionType = CadGeometryRepairActionType.BridgeGap,
            SourceSegmentIds = ["segment:000001:000000", "segment:000002:000000"],
            BeforePoints = [new CadPoint3(1, 0, 0), new CadPoint3(1.01, 0, 0)],
            AfterPoints = [new CadPoint3(1, 0, 0), new CadPoint3(1.01, 0, 0)],
            Confidence = CadGeometryRepairConfidence.High
        };
        var plan = new CadGeometryRepairPlan { Id = "repair-plan:forged", Status = CadGeometryRepairPlanStatus.Ready, Actions = [action] };

        var result = new CadGeometryRepairApplier().Apply(original, plan, new CadGeometryRepairPolicy());

        Assert.Equal(CadGeometryRepairStatus.Failed, result.Status);
        Assert.Empty(result.AppliedActions);
        Assert.Single(result.SkippedActions);
        Assert.Equal(2, result.RepairedDocument!.OpenSegments.Count);
    }

    [Fact]
    public void Applier_rejects_forged_snap_and_non_collinear_merge_actions()
    {
        var snapOriginal = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 1, 0),
            Line(2, 0, "SYN", 1.001, 0, 2, 0));
        var forgedSnap = new CadGeometryRepairAction
        {
            Id = "repair-action:forged-snap",
            ActionType = CadGeometryRepairActionType.SnapEndpoint,
            SourceSegmentIds = ["segment:000001:000000", "segment:000002:000000"],
            BeforePoints = [new CadPoint3(1, 0, 0), new CadPoint3(1.001, 0, 0)],
            AfterPoints = [new CadPoint3(20, 20, 0), new CadPoint3(20, 20, 0)],
            Confidence = CadGeometryRepairConfidence.High
        };

        var snapResult = new CadGeometryRepairApplier().Apply(
            snapOriginal,
            new CadGeometryRepairPlan { Id = "repair-plan:forged-snap", Status = CadGeometryRepairPlanStatus.Ready, Actions = [forgedSnap] },
            new CadGeometryRepairPolicy());

        Assert.Equal(CadGeometryRepairStatus.Failed, snapResult.Status);
        Assert.Equal(new CadPoint3(1, 0, 0), snapOriginal.OpenSegments[0].End);

        var mergeOriginal = OpenDocument(
            Line(3, 0, "SYN", 0, 0, 1, 0),
            Line(4, 0, "SYN", 1, 0, 1, 1));
        var forgedMerge = new CadGeometryRepairAction
        {
            Id = "repair-action:forged-merge",
            ActionType = CadGeometryRepairActionType.MergeCollinearSegments,
            SourceSegmentIds = ["segment:000003:000000", "segment:000004:000000"],
            AfterPoints = [new CadPoint3(0, 0, 0), new CadPoint3(1, 1, 0)],
            Confidence = CadGeometryRepairConfidence.High
        };

        var mergeResult = new CadGeometryRepairApplier().Apply(
            mergeOriginal,
            new CadGeometryRepairPlan { Id = "repair-plan:forged-merge", Status = CadGeometryRepairPlanStatus.Ready, Actions = [forgedMerge] },
            new CadGeometryRepairPolicy());

        Assert.Equal(CadGeometryRepairStatus.Failed, mergeResult.Status);
        Assert.Equal(2, mergeResult.RepairedDocument!.OpenSegments.Count);
    }

    [Fact]
    public void Applier_rejects_forged_snap_and_bridge_that_use_two_endpoints_from_one_source_segment()
    {
        var snapOriginal = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 0.001, 0),
            Line(2, 0, "SYN", 2, 0, 3, 0));
        var forgedSnap = new CadGeometryRepairAction
        {
            Id = "repair-action:forged-same-source-snap",
            ActionType = CadGeometryRepairActionType.SnapEndpoint,
            SourceSegmentIds = ["segment:000001:000000", "segment:000002:000000"],
            BeforePoints = [new CadPoint3(0, 0, 0), new CadPoint3(0.001, 0, 0)],
            AfterPoints = [new CadPoint3(0.0005, 0, 0), new CadPoint3(0.0005, 0, 0)],
            Confidence = CadGeometryRepairConfidence.High
        };

        var snapResult = new CadGeometryRepairApplier().Apply(
            snapOriginal,
            new CadGeometryRepairPlan { Id = "repair-plan:forged-same-source-snap", Status = CadGeometryRepairPlanStatus.Ready, Actions = [forgedSnap] },
            new CadGeometryRepairPolicy());

        Assert.Equal(CadGeometryRepairStatus.Failed, snapResult.Status);
        Assert.Equal(new CadPoint3(0.001, 0, 0), snapOriginal.OpenSegments[0].End);

        var bridgeOriginal = OpenDocument(
            Line(3, 0, "SYN", 0, 0, 0.01, 0),
            Line(4, 0, "SYN", 2, 0, 3, 0));
        var forgedBridge = new CadGeometryRepairAction
        {
            Id = "repair-action:forged-same-source-bridge",
            ActionType = CadGeometryRepairActionType.BridgeGap,
            SourceSegmentIds = ["segment:000003:000000", "segment:000004:000000"],
            BeforePoints = [new CadPoint3(0, 0, 0), new CadPoint3(0.01, 0, 0)],
            AfterPoints = [new CadPoint3(0, 0, 0), new CadPoint3(0.01, 0, 0)],
            Confidence = CadGeometryRepairConfidence.High
        };

        var bridgeResult = new CadGeometryRepairApplier().Apply(
            bridgeOriginal,
            new CadGeometryRepairPlan { Id = "repair-plan:forged-same-source-bridge", Status = CadGeometryRepairPlanStatus.Ready, Actions = [forgedBridge] },
            new CadGeometryRepairPolicy());

        Assert.Equal(CadGeometryRepairStatus.Failed, bridgeResult.Status);
        Assert.Equal(2, bridgeResult.RepairedDocument!.OpenSegments.Count);
    }

    [Fact]
    public void Applier_rejects_a_forged_snap_with_ambiguous_duplicate_source_endpoints()
    {
        var original = OpenDocument(
            Line(1, 0, "SYN", 0, 0, 0.001, 0),
            Line(2, 0, "SYN", 0, 0, 0.001, 0));
        var action = new CadGeometryRepairAction
        {
            Id = "repair-action:forged-ambiguous-snap",
            ActionType = CadGeometryRepairActionType.SnapEndpoint,
            SourceSegmentIds = ["segment:000001:000000", "segment:000002:000000"],
            BeforePoints = [new CadPoint3(0, 0, 0), new CadPoint3(0.001, 0, 0)],
            AfterPoints = [new CadPoint3(0.0005, 0, 0), new CadPoint3(0.0005, 0, 0)],
            Confidence = CadGeometryRepairConfidence.High
        };

        var result = new CadGeometryRepairApplier().Apply(
            original,
            new CadGeometryRepairPlan { Id = "repair-plan:forged-ambiguous-snap", Status = CadGeometryRepairPlanStatus.Ready, Actions = [action] },
            new CadGeometryRepairPolicy());

        Assert.Equal(CadGeometryRepairStatus.Failed, result.Status);
        Assert.Equal(new CadPoint3(0.001, 0, 0), original.OpenSegments[0].End);
    }

    private static CadContourDocument OpenDocument(params CadCurveSegment2[] segments) =>
        new() { OpenSegments = segments };

    private static CadLineSegment2 Line(
        int sourceOrder,
        int segmentOrder,
        string layer,
        double startX,
        double startY,
        double endX,
        double endY,
        double startZ = 0,
        double endZ = 0) =>
        new(
            sourceOrder,
            segmentOrder,
            layer,
            "LINE",
            new CadPoint3(startX, startY, startZ),
            new CadPoint3(endX, endY, endZ));
}
