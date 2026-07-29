using SceneBuilder.Domain;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class BlenderManifestMapperTests
{
    [Fact]
    public void Map_returns_a_safe_failure_when_scene_nodes_have_duplicate_semantic_ids()
    {
        var wall = CreateWall();
        var draft = new SceneDraft
        {
            Id = "draft:public:001",
            SemanticObjects = [wall],
            Nodes = [NodeFor(wall), NodeFor(wall)]
        };

        var result = new BlenderManifestMapper().Map(draft);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BLENDER_MANIFEST_INVALID");
    }

    [Fact]
    public void Map_emits_only_supported_procedural_geometry_in_stable_order_without_source_metadata()
    {
        var wall = CreateWall();
        var floor = new CadFloorObject("semantic:floor:002", "contour:000002", CadClassificationSubjectKind.Contour, Bounds(), null, Rectangle("contour:000002", 2, 4, 2));
        var facility = new CadStaticFacilityObject("semantic:static-facility:003", "insert:000003", Bounds(), null, "private-block", new CadPoint3(5, 5, 0), 0, CadScale3.Identity);
        var draft = new SceneDraft
        {
            Id = "draft:public:001",
            SemanticObjects = [facility, floor, wall],
            Nodes = [NodeFor(wall), NodeFor(floor), NodeFor(facility, SceneNodeContentKind.StaticAssetReference)]
        };

        var result = new BlenderManifestMapper().Map(draft);
        var manifest = Assert.IsType<BlenderManifest>(result.Manifest);
        var serialized = BlenderManifestMapper.Serialize(manifest);

        Assert.Equal("1.0", manifest.ContractVersion);
        Assert.Equal("meters", manifest.Unit);
        Assert.Equal([floor.Id, wall.Id], manifest.Objects.Select(item => item.Id));
        Assert.Equal([facility.Id], result.SkippedSemanticObjectIds);
        Assert.DoesNotContain("private-block", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceLayer", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Tessellate_preserves_arc_endpoints_without_repeating_the_closed_endpoint()
    {
        var arc = new CadArcSegment2(1, 1, "SYN", "ARC", new CadPoint3(0, 0, 0), 1, 0, 180, CadCurveDirection.CounterClockwise);
        var contour = Assert.IsType<CadSegmentContour>(new CadContourValidator().Validate(new CadSegmentContour(
            "contour:arc",
            [
                arc,
                new CadLineSegment2(1, 0, "SYN", "LINE", new CadPoint3(-1, 0, 0), new CadPoint3(1, 0, 0))
            ],
            isSourceDefinedClosed: true)));

        var points = BlenderProfileTessellator.Tessellate(contour, 30);

        Assert.Equal(7, points.Count);
        Assert.Equal(1, points[0].X, 8);
        Assert.Equal(0, points[0].Y, 8);
        Assert.Equal(Math.Cos(Math.PI / 6), points[1].X, 8);
        Assert.Equal(-1, points[^1].X, 8);
        Assert.NotEqual(points[0], points[^1]);
    }

    private static CadWallObject CreateWall() =>
        new("semantic:wall:001", "contour:000001", CadClassificationSubjectKind.Contour, Bounds(), new CadRuleGeometryDefaults { HeightMeters = 3 }, Rectangle("contour:000001", 0, 2, 2), null, 3);

    private static CadSegmentContour Rectangle(string id, int sourceOrder, double width, double height) =>
        Assert.IsType<CadSegmentContour>(new CadContourValidator().Validate(new CadSegmentContour(
            id,
            [
                Line(sourceOrder, 0, 0, 0, width, 0),
                Line(sourceOrder, 1, width, 0, width, height),
                Line(sourceOrder, 2, width, height, 0, height),
                Line(sourceOrder, 3, 0, height, 0, 0)
            ],
            isSourceDefinedClosed: true)));

    private static CadLineSegment2 Line(int sourceOrder, int segmentOrder, double startX, double startY, double endX, double endY) =>
        new(sourceOrder, segmentOrder, "SYN", "LWPOLYLINE", new CadPoint3(startX, startY, 0), new CadPoint3(endX, endY, 0));

    private static SceneNode NodeFor(CadSemanticObject semanticObject, SceneNodeContentKind contentKind = SceneNodeContentKind.ProceduralStaticGeometry) =>
        new()
        {
            Id = $"node:{semanticObject.Id}",
            SemanticObjectId = semanticObject.Id,
            Classification = semanticObject.Classification,
            ContentKind = contentKind,
            Bounds = semanticObject.Bounds,
            SourceSubjectId = semanticObject.SourceSubjectId,
            SourceSubjectKind = semanticObject.SourceSubjectKind
        };

    private static CadBounds Bounds() => CadBounds.Computed(0, 0, 0, 10, 10, 3);
}
