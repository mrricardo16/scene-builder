namespace SceneBuilder.Domain.Tests;

public sealed class ScenePartitionPlannerTests
{
    [Fact]
    public void Plan_assigns_asset_anchors_to_stable_half_open_grid_partitions()
    {
        var draft = Draft(
            Asset("asset-zero", 0, 0),
            Asset("asset-upper", 100, 100),
            Asset("asset-negative", -0.001, -0.001));

        var result = new ScenePartitionPlanner().Plan(draft, new ScenePartitionPolicy());

        Assert.Equal(ScenePartitionPlanStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                "partition-x-m000001-y-m000001",
                "partition-x-p000000-y-p000000",
                "partition-x-p000001-y-p000001"
            ],
            result.Plan!.Partitions.Select(partition => partition.Id));
        Assert.Equal("partition-x-p000001-y-p000001", result.Plan.Assignments.Single(assignment => assignment.SemanticObjectId == "asset-upper").OwnerPartitionId);
        Assert.All(result.Plan.Assignments, assignment => Assert.Single(assignment.IntersectedPartitionIds));
    }

    [Fact]
    public void Plan_keeps_asset_ownership_at_its_transform_anchor_and_reports_an_outside_bounds_anchor()
    {
        var asset = Asset("asset-outside", 0, 0, 150, 0);

        var result = new ScenePartitionPlanner().Plan(Draft(asset), new ScenePartitionPolicy());

        Assert.Equal(ScenePartitionPlanStatus.Succeeded, result.Status);
        Assert.Equal("partition-x-p000001-y-p000000", Assert.Single(result.Plan!.Assignments).OwnerPartitionId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PARTITION_ANCHOR_OUTSIDE_BOUNDS");
    }

    [Fact]
    public void Plan_fails_with_a_diagnostic_instead_of_overflowing_when_a_grid_cell_bound_is_not_finite()
    {
        var asset = Asset("asset-extreme", 1e308, 0, 1e308, 0);

        var result = new ScenePartitionPlanner().Plan(
            Draft(asset),
            new ScenePartitionPolicy { CellSizeMeters = 1e308 });

        Assert.Equal(ScenePartitionPlanStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PARTITION_GRID_INDEX_OVERFLOW");
    }

    private static SceneDraft Draft(params CadStaticFacilityObject[] objects) => new()
    {
        Id = "partition-test",
        SemanticObjects = objects,
        Nodes = objects.Select(item => new SceneNode
        {
            Id = "node-" + item.Id,
            SemanticObjectId = item.Id,
            Classification = item.Classification,
            ContentKind = SceneNodeContentKind.StaticAssetReference,
            Bounds = item.Bounds,
            Transform = new SceneNodeTransform(item.Position, item.RotationDegrees, item.Scale)
        }).ToArray()
    };

    private static CadStaticFacilityObject Asset(string id, double x, double y, double? positionX = null, double? positionY = null) => new(
        id,
        "insert-" + id,
        CadBounds.Computed(x, y, 0, x, y, 1),
        null,
        "synthetic",
        new CadPoint3(positionX ?? x, positionY ?? y, 0),
        0,
        CadScale3.Identity);
}
