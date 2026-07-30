using SceneBuilder.Domain;

namespace SceneBuilder.Application.Tests;

public sealed class CadAssetBindingResolverTests
{
    [Fact]
    public void Resolve_prefers_semantic_id_over_exact_and_wildcard_block_matches()
    {
        var subject = StaticFacility("facility-1", "pump-01");
        var configuration = Configuration(
            assets:
            [
                Asset("semantic", CadAssetKind.StaticFacility),
                Asset("exact", CadAssetKind.StaticFacility),
                Asset("wildcard", CadAssetKind.StaticFacility)
            ],
            bindings:
            [
                Binding("wildcard", "wildcard", block: "pump-*") ,
                Binding("exact", "exact", block: "PUMP-01"),
                Binding("semantic", "semantic", semanticObjectId: "FACILITY-1")
            ]);

        var result = new CadAssetBindingResolver().Resolve([subject], configuration);

        var resolution = Assert.Single(result.Resolutions);
        Assert.Equal(CadAssetResolutionStatus.Resolved, resolution.Status);
        Assert.Equal("semantic", resolution.Asset!.AssetId);
        Assert.Equal("semantic", resolution.BindingId);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_rejects_same_rank_and_priority_matches_for_different_assets()
    {
        var configuration = Configuration(
            [Asset("a", CadAssetKind.StaticFacility), Asset("b", CadAssetKind.StaticFacility)],
            [Binding("a", "a", block: "PUMP-*"), Binding("b", "b", block: "PUMP-*")]);

        var result = new CadAssetBindingResolver().Resolve([StaticFacility("facility-1", "pump-01")], configuration);

        var resolution = Assert.Single(result.Resolutions);
        Assert.Equal(CadAssetResolutionStatus.Conflict, resolution.Status);
        Assert.Null(resolution.Asset);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ASSET_BINDING_CONFLICT");
    }

    [Fact]
    public void Resolve_uses_ordinal_binding_id_for_same_asset_duplicate_matches_regardless_of_input_order()
    {
        var configuration = Configuration(
            [Asset("pump", CadAssetKind.StaticFacility)],
            [Binding("z-binding", "pump", block: "PUMP-*"), Binding("a-binding", "pump", block: "PUMP-*")]);

        var result = new CadAssetBindingResolver().Resolve([StaticFacility("facility-1", "pump-01")], configuration);

        var resolution = Assert.Single(result.Resolutions);
        Assert.Equal(CadAssetResolutionStatus.Resolved, resolution.Status);
        Assert.Equal("a-binding", resolution.BindingId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ASSET_BINDING_DUPLICATE_MATCH");
    }

    [Fact]
    public void Resolve_keeps_static_and_dynamic_assets_separate_and_ignores_disabled_bindings()
    {
        var configuration = Configuration(
            [Asset("static", CadAssetKind.StaticFacility), Asset("dynamic", CadAssetKind.DynamicEquipment)],
            [
                Binding("disabled", "static", block: "PUMP-*", enabled: false),
                Binding("dynamic", "dynamic", block: "PUMP-*")
            ]);

        var result = new CadAssetBindingResolver().Resolve(
            [StaticFacility("facility-1", "pump-01"), DynamicEquipment("equipment-1", "pump-01")],
            configuration);

        Assert.Equal(CadAssetResolutionStatus.Unmapped, result.Resolutions.Single(resolution => resolution.SemanticObjectId == "facility-1").Status);
        Assert.Equal("dynamic", result.Resolutions.Single(resolution => resolution.SemanticObjectId == "equipment-1").Asset!.AssetId);
    }

    private static CadAssetConfiguration Configuration(IReadOnlyList<CadAssetDefinition> assets, IReadOnlyList<CadAssetBinding> bindings) => new()
    {
        Catalog = new CadAssetCatalog { ContractVersion = CadAssetConfigurationLoader.ContractVersion, Assets = assets },
        Bindings = new CadAssetBindingSet { ContractVersion = CadAssetConfigurationLoader.ContractVersion, Bindings = bindings }
    };

    private static CadAssetDefinition Asset(string id, CadAssetKind kind) => new()
    {
        AssetId = id,
        Kind = kind,
        RelativeGlbPath = $"assets/{id}.glb"
    };

    private static CadAssetBinding Binding(string id, string assetId, string? semanticObjectId = null, string? block = null, bool enabled = true) => new()
    {
        Id = id,
        Enabled = enabled,
        Priority = 0,
        Kind = assetId == "dynamic" ? CadAssetKind.DynamicEquipment : CadAssetKind.StaticFacility,
        Selector = new CadAssetBindingSelector { SemanticObjectId = semanticObjectId, Block = block },
        AssetId = assetId
    };

    private static CadStaticFacilityObject StaticFacility(string id, string blockName) => new(
        id,
        "insert-1",
        new CadBounds(0, 0, 0, 1, 1, 1),
        null,
        blockName,
        new CadPoint3(0, 0, 0),
        0,
        CadScale3.Identity);

    private static CadDynamicEquipmentObject DynamicEquipment(string id, string blockName) => new(
        id,
        "insert-2",
        new CadBounds(0, 0, 0, 1, 1, 1),
        null,
        blockName,
        new CadPoint3(0, 0, 0),
        0,
        CadScale3.Identity);
}
