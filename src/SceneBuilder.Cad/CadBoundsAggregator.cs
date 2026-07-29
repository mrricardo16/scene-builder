using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

internal static class CadBoundsAggregator
{
    internal static CadBounds Aggregate(IEnumerable<CadBounds> bounds)
    {
        var evaluatedBounds = bounds.ToArray();
        if (evaluatedBounds.Length == 0)
        {
            return CadBounds.Empty;
        }

        if (evaluatedBounds.Any(bound => bound.State is not CadBoundsState.Computed))
        {
            return CadBounds.NotEvaluated;
        }

        return CadBounds.Computed(
            evaluatedBounds.Min(bound => bound.MinX),
            evaluatedBounds.Min(bound => bound.MinY),
            evaluatedBounds.Min(bound => bound.MinZ),
            evaluatedBounds.Max(bound => bound.MaxX),
            evaluatedBounds.Max(bound => bound.MaxY),
            evaluatedBounds.Max(bound => bound.MaxZ));
    }
}
