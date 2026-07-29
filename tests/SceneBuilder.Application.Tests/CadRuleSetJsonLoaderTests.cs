using SceneBuilder.Domain;

namespace SceneBuilder.Application.Tests;

public sealed class CadRuleSetJsonLoaderTests
{
    [Fact]
    public void Load_PublicSyntheticSceneDraftRules_MapsAllSupportedSemanticClassifications()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "public-synthetic-scene-draft-rules.json"));

        var result = new CadRuleSetJsonLoader().Load(json);

        var ruleSet = Assert.IsType<CadRuleSet>(result.RuleSet);
        Assert.True(result.IsSuccess);
        Assert.Equal(6, ruleSet.Rules.Count);
        Assert.Equal(
            [
                CadSemanticClassification.Wall,
                CadSemanticClassification.Floor,
                CadSemanticClassification.Column,
                CadSemanticClassification.Road,
                CadSemanticClassification.StaticFacility,
                CadSemanticClassification.DynamicEquipment
            ],
            ruleSet.Rules.Select(rule => rule.Classification));
    }

    [Fact]
    public void Load_valid_fixture_maps_frozen_text_and_normalizes_entity_types()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "public-synthetic-rules-valid.json");

        var result = new CadRuleSetJsonLoader().Load(File.ReadAllText(path));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.RuleSet!.Rules.Count);
        var rule = Assert.Single(result.RuleSet!.Rules.Where(rule => rule.Id == "synthetic-wall-layer"));
        Assert.Equal(CadSemanticClassification.Wall, rule.Classification);
        Assert.Equal(["LWPOLYLINE"], rule.Match.EntityTypes);
        Assert.Equal(3, rule.GeometryDefaults!.HeightMeters);
        Assert.Contains(result.RuleSet.Rules, rule => rule.Id == "synthetic-mobile-block" && rule.Classification == CadSemanticClassification.DynamicEquipment);
    }

    [Fact]
    public void Load_rejects_unknown_json_fields_without_executing_partial_rules()
    {
        const string json = "{\"contractVersion\":\"1.0\",\"rules\":[],\"script\":\"ignored\"}";

        var result = new CadRuleSetJsonLoader().Load(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.RuleSet);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RULE_CONFIG_INVALID");
    }

    [Theory]
    [InlineData("public-synthetic-rules-invalid.json")]
    public void Load_rejects_invalid_fixture_contracts(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", fixtureName);

        var result = new CadRuleSetJsonLoader().Load(File.ReadAllText(path));

        Assert.False(result.IsSuccess);
        Assert.Null(result.RuleSet);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RULE_CONFIG_INVALID");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"contractVersion\":\"1.0\",\"rules\":null}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"rules\":[{\"id\":\"x\",\"enabled\":true,\"priority\":0,\"match\":{\"layer\":null,\"block\":null,\"entityTypes\":[]},\"classification\":\"unknown\",\"geometryDefaults\":null}]}")]
    public void Load_rejects_malformed_null_or_unknown_classification_json(string json)
    {
        var result = new CadRuleSetJsonLoader().Load(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.RuleSet);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RULE_CONFIG_INVALID");
    }

    [Theory]
    [InlineData("{\"contractVersion\":\"1.0\",\"rules\":[{\"id\":\"x\",\"priority\":0,\"match\":{\"layer\":\"SYN_*\",\"block\":null,\"entityTypes\":[]},\"classification\":\"wall\",\"geometryDefaults\":null}]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"rules\":[{\"id\":\"x\",\"enabled\":true,\"match\":{\"layer\":\"SYN_*\",\"block\":null,\"entityTypes\":[]},\"classification\":\"wall\",\"geometryDefaults\":null}]}")]
    public void Load_rejects_rules_with_missing_required_boolean_or_priority(string json)
    {
        var result = new CadRuleSetJsonLoader().Load(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.RuleSet);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RULE_CONFIG_INVALID");
    }
}
