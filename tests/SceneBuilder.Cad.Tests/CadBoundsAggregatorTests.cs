namespace SceneBuilder.Cad.Tests;

public sealed class CadBoundsAggregatorTests
{
    [Fact]
    public void Aggregate_WhenAnyRangeCannotBeEvaluated_ReturnsNotEvaluatedInsteadOfPartialRange()
    {
        var result = CadBoundsAggregator.Aggregate(
        [
            CadBounds.Computed(0, 0, 0, 10, 10, 0),
            CadBounds.NotEvaluated
        ]);

        Assert.Equal(CadBounds.NotEvaluated, result);
    }
}
