namespace SceneBuilder.Domain.Tests;

public sealed class CadBoundsContractTests
{
    [Fact]
    public void Constructor_creates_computed_bounds()
    {
        var bounds = new CadBounds(1, 2, 3, 4, 5, 6);

        Assert.Equal(CadBoundsState.Computed, bounds.State);
        Assert.Equal(bounds, CadBounds.Computed(1, 2, 3, 4, 5, 6));
    }

    [Fact]
    public void Empty_not_evaluated_and_zero_computed_bounds_are_distinct()
    {
        var zeroComputed = new CadBounds(0, 0, 0, 0, 0, 0);

        Assert.Equal(CadBoundsState.Empty, CadBounds.Empty.State);
        Assert.Equal(CadBoundsState.NotEvaluated, CadBounds.NotEvaluated.State);
        Assert.NotEqual(CadBounds.Empty, CadBounds.NotEvaluated);
        Assert.NotEqual(CadBounds.Empty, zeroComputed);
        Assert.NotEqual(CadBounds.NotEvaluated, zeroComputed);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0, 1, 1, 1)]
    [InlineData(0, double.PositiveInfinity, 0, 1, 1, 1)]
    [InlineData(0, 0, double.NegativeInfinity, 1, 1, 1)]
    [InlineData(0, 0, 0, double.NaN, 1, 1)]
    [InlineData(0, 0, 0, 1, double.PositiveInfinity, 1)]
    [InlineData(0, 0, 0, 1, 1, double.NegativeInfinity)]
    public void Constructor_rejects_non_finite_coordinates(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CadBounds(minX, minY, minZ, maxX, maxY, maxZ));
    }

    [Theory]
    [InlineData(2, 0, 0, 1, 1, 1)]
    [InlineData(0, 2, 0, 1, 1, 1)]
    [InlineData(0, 0, 2, 1, 1, 1)]
    public void Constructor_rejects_reversed_axes(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        Assert.Throws<ArgumentException>(() =>
            CadBounds.Computed(minX, minY, minZ, maxX, maxY, maxZ));
    }

    [Fact]
    public void State_cannot_be_publicly_mutated()
    {
        var stateProperty = typeof(CadBounds).GetProperty(nameof(CadBounds.State));

        Assert.NotNull(stateProperty);
        Assert.Null(stateProperty.SetMethod);
    }
}
