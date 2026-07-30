using SceneBuilder.Domain;
using SceneBuilder.Tiles;
using Xunit;

namespace SceneBuilder.Tiles.Tests;

public sealed class TilesetBoundingVolumeBuilderTests
{
    [Fact]
    public void CreateBox_uses_content_bounds_center_and_expands_only_a_degenerate_axis()
    {
        var box = new TilesetBoundingVolumeBuilder().CreateBox(
            CadBounds.Computed(0, -20, 5, 10, 20, 5),
            minimumHalfExtentMeters: 0.001d);

        Assert.Equal([5d, 0d, 5d, 5d, 0d, 0d, 0d, 20d, 0d, 0d, 0d, 0.001d], box);
    }
}
