using SceneBuilder.Application;
using SceneBuilder.Domain;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class BlenderAssetManifestMapperTests
{
    [Fact]
    public void MapAssets_emits_v2_anonymous_asset_reference_without_block_or_asset_root_data()
    {
        var facility = new CadStaticFacilityObject("facility-1", "insert-1", CadBounds.Computed(0, 0, 0, 2, 3, 4), null, "private-pump-block", new CadPoint3(2, 3, 4), 90, CadScale3.Identity);
        var transform = new SceneNodeTransform(new CadPoint3(2, 3, 4), 90, CadScale3.Identity);
        var draft = new SceneDraft
        {
            Id = "draft-1",
            SemanticObjects = [facility],
            Nodes = [new SceneNode { Id = "node-1", SemanticObjectId = facility.Id, Classification = facility.Classification, ContentKind = SceneNodeContentKind.StaticAssetReference, Bounds = facility.Bounds, Transform = transform }]
        };
        var resolution = new CadAssetResolution
        {
            SemanticObjectId = facility.Id,
            Kind = CadAssetKind.StaticFacility,
            Status = CadAssetResolutionStatus.Resolved,
            Asset = new CadAssetDefinition { AssetId = "private-pump-asset", Kind = CadAssetKind.StaticFacility, RelativeGlbPath = "original/private-pump.glb" },
            BindingId = "binding-1"
        };

        var result = new BlenderManifestMapper().MapAssets(draft, [resolution], [new StagedBlenderAsset("private-pump-asset", CadAssetKind.StaticFacility, "assets/asset-000001.glb")], MissingAssetBehavior.Skip);

        var manifest = Assert.IsType<BlenderManifest>(result.Manifest);
        var serialized = BlenderManifestMapper.Serialize(manifest);
        var item = Assert.Single(manifest.Objects);
        Assert.Equal("2.0", manifest.ContractVersion);
        Assert.Equal("static-asset", item.Kind);
        Assert.Equal("assets/asset-000001.glb", item.AssetFile);
        Assert.DoesNotContain("private-pump-block", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("private-pump-asset", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("original/private-pump.glb", serialized, StringComparison.Ordinal);
    }
}
