using SceneBuilder.Domain;

namespace SceneBuilder.Tiles;

public sealed class TilesetBoundingVolumeBuilder
{
    public IReadOnlyList<double> CreateBox(CadBounds bounds, double minimumHalfExtentMeters)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.State is not CadBoundsState.Computed || !double.IsFinite(minimumHalfExtentMeters) || minimumHalfExtentMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "A computed bounds and positive finite minimum half extent are required.");
        }

        var centerX = bounds.MinX + ((bounds.MaxX - bounds.MinX) / 2d);
        var centerY = bounds.MinY + ((bounds.MaxY - bounds.MinY) / 2d);
        var centerZ = bounds.MinZ + ((bounds.MaxZ - bounds.MinZ) / 2d);
        var halfX = Math.Max((bounds.MaxX - bounds.MinX) / 2d, minimumHalfExtentMeters);
        var halfY = Math.Max((bounds.MaxY - bounds.MinY) / 2d, minimumHalfExtentMeters);
        var halfZ = Math.Max((bounds.MaxZ - bounds.MinZ) / 2d, minimumHalfExtentMeters);
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY) || !double.IsFinite(centerZ) || !double.IsFinite(halfX) || !double.IsFinite(halfY) || !double.IsFinite(halfZ))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Bounding volume values must be finite.");
        }

        return [centerX, centerY, centerZ, halfX, 0d, 0d, 0d, halfY, 0d, 0d, 0d, halfZ];
    }
}
