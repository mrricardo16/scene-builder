namespace SceneBuilder.Domain.Tests;

public sealed class CadClassificationRuleTests
{
    [Fact]
    public void Rule_engine_uses_case_insensitive_wildcards_and_frozen_layer_rank()
    {
        var subject = Subject("subject:contour:000001", CadClassificationSubjectKind.Contour, "syn_wall_a", "LWPOLYLINE");
        var rules = RuleSet(
            Rule("wall-wildcard", CadSemanticClassification.Wall, 1, layer: "SYN_WALL_*"),
            Rule("floor-exact", CadSemanticClassification.Floor, 1, layer: "SYN_WALL_A"));

        var result = new CadRuleEngine().Classify(rules, [subject]);

        var classification = Assert.Single(result.Objects);
        Assert.Equal(CadClassificationStatus.Succeeded, result.Status);
        Assert.Equal(CadSemanticClassification.Floor, classification.Classification);
        Assert.Equal("floor-exact", classification.MatchedRuleId);
        Assert.Equal(390, classification.MatchRank);
        Assert.Equal(1, classification.Priority);
    }

    [Fact]
    public void Rule_engine_leaves_different_classification_ties_unclassified_and_reports_conflict()
    {
        var subject = Subject("subject:segment:000001", CadClassificationSubjectKind.OpenSegment, "SYN_ROAD_A", "LINE");
        var rules = RuleSet(
            Rule("road", CadSemanticClassification.Road, 100, layer: "SYN_ROAD_*"),
            Rule("floor", CadSemanticClassification.Floor, 100, layer: "SYN_ROAD_*"));

        var result = new CadRuleEngine().Classify(rules, [subject]);

        var classification = Assert.Single(result.Objects);
        Assert.Equal(CadClassificationStatus.PartiallySucceeded, result.Status);
        Assert.Equal(CadSemanticClassification.Unclassified, classification.Classification);
        Assert.Null(classification.MatchedRuleId);
        Assert.Contains(classification.Diagnostics, diagnostic => diagnostic.Code == "RULE_CONFLICT");
    }

    [Fact]
    public void Rule_engine_selects_ordinal_rule_id_for_same_classification_ties_independent_of_input_order()
    {
        var subject = Subject("subject:insert:000001", CadClassificationSubjectKind.Insert, "SYN_EQUIPMENT", "INSERT", "SYN_RACK_A");
        var first = Rule("z-rule", CadSemanticClassification.StaticFacility, 10, block: "SYN_RACK_A");
        var second = Rule("a-rule", CadSemanticClassification.StaticFacility, 10, block: "SYN_RACK_A");

        var normal = new CadRuleEngine().Classify(RuleSet(first, second), [subject]);
        var reversed = new CadRuleEngine().Classify(RuleSet(second, first), [subject]);

        Assert.Equal("a-rule", Assert.Single(normal.Objects).MatchedRuleId);
        Assert.Equal(normal.Status, reversed.Status);
        Assert.Equal(Assert.Single(normal.Objects).MatchedRuleId, Assert.Single(reversed.Objects).MatchedRuleId);
        Assert.Equal(Assert.Single(normal.Objects).CandidateRuleIds, Assert.Single(reversed.Objects).CandidateRuleIds);
        Assert.Contains(normal.Diagnostics, diagnostic => diagnostic.Code == "RULE_DUPLICATE_MATCH");
    }

    [Fact]
    public void Rule_set_validator_rejects_invalid_rules_as_a_whole()
    {
        var invalid = new CadRuleSet
        {
            ContractVersion = "1.0",
            Rules = [new CadClassificationRule { Id = " ", Enabled = true, Match = new CadRuleMatch() }]
        };

        var result = new CadRuleEngine().Classify(invalid, [Subject("subject:segment:000001", CadClassificationSubjectKind.OpenSegment, "SYN", "LINE")]);

        Assert.Equal(CadClassificationStatus.Failed, result.Status);
        Assert.Empty(result.Objects);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RULE_CONFIG_INVALID");
    }

    [Fact]
    public void Rule_set_validator_rejects_unclassified_as_an_enabled_rule_target()
    {
        var ruleSet = RuleSet(Rule("invalid-default", CadSemanticClassification.Unclassified, 0, layer: "SYN_*"));

        var result = new CadRuleEngine().Classify(ruleSet, [Subject("subject:segment", CadClassificationSubjectKind.OpenSegment, "SYN_A", "LINE")]);

        Assert.Equal(CadClassificationStatus.Failed, result.Status);
        Assert.Empty(result.Objects);
    }

    [Theory]
    [InlineData("SYN_WALL_A", "SYN_WALL_A", true)]
    [InlineData("SYN_WALL_*", "SYN_WALL_A", true)]
    [InlineData("SYN_W?LL_A", "SYN_WALL_A", true)]
    [InlineData("WALL_A", "SYN_WALL_A", false)]
    [InlineData("*WALL*", "SYN_WALL_A", true)]
    [InlineData("SYN_?", "SYN_墙", true)]
    public void Wildcard_matcher_uses_full_ordinal_ignore_case_matching(string pattern, string value, bool expected)
    {
        Assert.Equal(expected, CadRuleWildcardMatcher.IsMatch(pattern, value));
    }

    [Theory]
    [InlineData("SYN_LAYER", null, "LINE", 390)]
    [InlineData("SYN_*", null, "LINE", 200)]
    [InlineData(null, "SYN_BLOCK", "INSERT", 490)]
    [InlineData(null, "SYN_*", "INSERT", 290)]
    [InlineData("SYN_LAYER", "SYN_BLOCK", "INSERT", 600)]
    [InlineData("SYN_*", "SYN_BLOCK", "INSERT", 500)]
    [InlineData("SYN_LAYER", "SYN_*", "INSERT", 400)]
    [InlineData("SYN_*", "SYN_*", "INSERT", 300)]
    public void Rule_engine_uses_the_frozen_match_rank_matrix(string? layer, string? block, string entityType, int expectedRank)
    {
        var kind = entityType == "INSERT" ? CadClassificationSubjectKind.Insert : CadClassificationSubjectKind.OpenSegment;
        var subject = Subject("subject:rank", kind, "SYN_LAYER", entityType, entityType == "INSERT" ? "SYN_BLOCK" : null);
        var rule = Rule("rank", CadSemanticClassification.Wall, 1, layer, block) with
        {
            Match = new CadRuleMatch { Layer = layer, Block = block, EntityTypes = [entityType] }
        };

        var result = new CadRuleEngine().Classify(RuleSet(rule), [subject]);

        Assert.Equal(expectedRank, Assert.Single(result.Objects).MatchRank);
    }

    [Fact]
    public void Entity_type_only_rule_has_rank_100_and_block_rule_does_not_match_open_segment()
    {
        var segment = Subject("subject:segment", CadClassificationSubjectKind.OpenSegment, "SYN", "LINE");
        var entityOnly = Rule("line", CadSemanticClassification.Road, 1) with { Match = new CadRuleMatch { EntityTypes = ["LINE"] } };
        var blockOnly = Rule("block", CadSemanticClassification.StaticFacility, 999, block: "SYN_BLOCK");

        var result = new CadRuleEngine().Classify(RuleSet(entityOnly, blockOnly), [segment]);

        var classification = Assert.Single(result.Objects);
        Assert.Equal(CadSemanticClassification.Road, classification.Classification);
        Assert.Equal(100, classification.MatchRank);
    }

    [Fact]
    public void Higher_rank_wins_before_priority_and_disabled_rules_do_not_participate()
    {
        var subject = Subject("subject:priority", CadClassificationSubjectKind.OpenSegment, "SYN_WALL_A", "LINE");
        var higherRank = Rule("higher-rank", CadSemanticClassification.Wall, 1, layer: "SYN_WALL_A");
        var higherPriority = Rule("higher-priority", CadSemanticClassification.Floor, 999, layer: "SYN_WALL_*");
        var disabled = Rule("disabled", CadSemanticClassification.Road, 1000, layer: "SYN_WALL_A") with { Enabled = false };

        var result = new CadRuleEngine().Classify(RuleSet(disabled, higherPriority, higherRank), [subject]);

        var classification = Assert.Single(result.Objects);
        Assert.Equal(CadSemanticClassification.Wall, classification.Classification);
        Assert.Equal("higher-rank", classification.MatchedRuleId);
    }

    [Fact]
    public void Rule_engine_returns_identical_results_for_large_stable_subject_and_rule_inputs()
    {
        var subjects = Enumerable.Range(0, 1000)
            .Select(index => Subject($"subject:segment:{index:D6}", CadClassificationSubjectKind.OpenSegment, "SYN_ROAD_A", "LINE"))
            .Reverse()
            .ToArray();
        var rules = Enumerable.Range(0, 100)
            .Select(index => Rule($"rule:{index:D3}", CadSemanticClassification.Road, index, layer: "SYN_ROAD_*"))
            .Reverse()
            .ToArray();

        var first = new CadRuleEngine().Classify(RuleSet(rules), subjects);
        var second = new CadRuleEngine().Classify(RuleSet(rules.Reverse().ToArray()), subjects.Reverse().ToArray());

        Assert.Equal(1000, first.Objects.Count);
        Assert.Equal(first.Objects.Select(item => item.Subject.Id), second.Objects.Select(item => item.Subject.Id));
        Assert.All(first.Objects, item => Assert.Equal("rule:099", item.MatchedRuleId));
    }

    [Fact]
    public void Subject_builder_preserves_contours_open_segments_and_inserts_in_stable_order()
    {
        var validContour = new CadContourValidator().Validate(new CadSegmentContour(
            "contour:000010",
            [
                Line(10, 0, "SYN_WALL_A", "LWPOLYLINE", 0, 0, 1, 0),
                Line(10, 1, "SYN_WALL_A", "LWPOLYLINE", 1, 0, 1, 1),
                Line(10, 2, "SYN_WALL_A", "LWPOLYLINE", 1, 1, 0, 1),
                Line(10, 3, "SYN_WALL_A", "LWPOLYLINE", 0, 1, 0, 0)
            ],
            isSourceDefinedClosed: true));
        var invalidContour = new CadSegmentContour("contour:000011", [], isSourceDefinedClosed: true);
        var input = new CadClassificationInput
        {
            Contours = new CadContourDocument { Contours = [invalidContour, validContour], OpenSegments = [Line(2, 0, "SYN_ROAD_A", "LINE", 0, 0, 1, 0)] },
            Geometry = new NormalizedCadGeometryDocument { Entities = [new CadInsertGeometry(1, "SYN_EQUIPMENT", "SYN_RACK_A", new CadPoint3(0, 0, 0), 0, CadScale3.Identity)] }
        };

        var subjects = new CadClassificationSubjectBuilder().Build(input);

        Assert.Equal(["contour:000010", "contour:000011", "insert:000001", "segment:000002:000000"], subjects.Select(subject => subject.Id));
        Assert.False(subjects.Single(subject => subject.Id == "contour:000011").IsEligibleForClassification);
        Assert.Equal("SYN_RACK_A", subjects.Single(subject => subject.Kind == CadClassificationSubjectKind.Insert).BlockName);
    }

    private static CadRuleSet RuleSet(params CadClassificationRule[] rules) => new()
    {
        ContractVersion = "1.0",
        Rules = rules
    };

    private static CadClassificationRule Rule(
        string id,
        CadSemanticClassification classification,
        int priority,
        string? layer = null,
        string? block = null) => new()
        {
            Id = id,
            Enabled = true,
            Classification = classification,
            Priority = priority,
            Match = new CadRuleMatch { Layer = layer, Block = block }
        };

    private static CadClassificationSubject Subject(
        string id,
        CadClassificationSubjectKind kind,
        string layer,
        string entityType,
        string? blockName = null) => new()
        {
            Id = id,
            Kind = kind,
            SourceLayer = layer,
            SourceEntityType = entityType,
            BlockName = blockName,
            Bounds = CadBounds.NotEvaluated
        };

    private static CadLineSegment2 Line(
        int sourceOrder,
        int segmentOrder,
        string layer,
        string entityType,
        double startX,
        double startY,
        double endX,
        double endY) => new(
            sourceOrder,
            segmentOrder,
            layer,
            entityType,
            new CadPoint3(startX, startY, 0),
            new CadPoint3(endX, endY, 0));
}
