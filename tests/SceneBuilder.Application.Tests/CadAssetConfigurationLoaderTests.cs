namespace SceneBuilder.Application.Tests;

public sealed class CadAssetConfigurationLoaderTests
{
    [Fact]
    public void Load_valid_versioned_catalog_and_bindings_maps_an_executable_configuration()
    {
        const string catalogJson = """
            {
              "contractVersion": "1.0",
              "assets": [
                { "assetId": "pump-a", "kind": "static-facility", "relativeGlbPath": "equipment/pump.glb" }
              ]
            }
            """;
        const string bindingsJson = """
            {
              "contractVersion": "1.0",
              "bindings": [
                {
                  "id": "pump-by-block",
                  "enabled": true,
                  "priority": 10,
                  "kind": "static-facility",
                  "selector": { "semanticObjectId": null, "block": "PUMP_*" },
                  "assetId": "pump-a"
                }
              ]
            }
            """;

        var result = new CadAssetConfigurationLoader().Load(catalogJson, bindingsJson);

        Assert.True(result.IsSuccess);
        var configuration = Assert.IsType<CadAssetConfiguration>(result.Configuration);
        var asset = Assert.Single(configuration.Catalog.Assets);
        Assert.Equal("pump-a", asset.AssetId);
        Assert.Equal(CadAssetKind.StaticFacility, asset.Kind);
        Assert.Equal("equipment/pump.glb", asset.RelativeGlbPath);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[],\"unexpected\":true}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"2.0\",\"assets\":[]}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":null}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"unknown\",\"relativeGlbPath\":\"a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"a.glb\"},{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"b.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":null}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[{\"id\":\"b\",\"enabled\":true,\"priority\":0,\"kind\":\"static-facility\",\"selector\":{\"semanticObjectId\":null,\"block\":null},\"assetId\":\"a\"}]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[{\"id\":\"b\",\"enabled\":true,\"priority\":0,\"kind\":\"dynamic-equipment\",\"selector\":{\"semanticObjectId\":\"node-1\",\"block\":null},\"assetId\":\"a\"}]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"../a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"https://example.test/a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"assets\":[{\"assetId\":\"a\",\"kind\":\"static-facility\",\"relativeGlbPath\":\"assets//a.glb\"}]}", "{\"contractVersion\":\"1.0\",\"bindings\":[]}")]
    public void Load_rejects_invalid_contracts_without_returning_a_partial_configuration(string catalogJson, string bindingsJson)
    {
        var result = new CadAssetConfigurationLoader().Load(catalogJson, bindingsJson);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ASSET_CONFIG_INVALID");
        Assert.DoesNotContain(catalogJson, result.Diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.DoesNotContain(bindingsJson, result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }
}
