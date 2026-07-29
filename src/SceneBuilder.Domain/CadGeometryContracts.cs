namespace SceneBuilder.Domain;

public sealed record CadPoint3
{
    public CadPoint3(double x, double y, double z)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidateFinite(z, nameof(z));

        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    internal CadPoint3 Transform(CadPoint3 origin, double scaleToMeters) =>
        new(
            (X - origin.X) * scaleToMeters,
            (Y - origin.Y) * scaleToMeters,
            (Z - origin.Z) * scaleToMeters);

    internal static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "CAD coordinates must be finite.");
        }
    }
}

public sealed record CadScale3
{
    public CadScale3(double x, double y, double z)
    {
        CadPoint3.ValidateFinite(x, nameof(x));
        CadPoint3.ValidateFinite(y, nameof(y));
        CadPoint3.ValidateFinite(z, nameof(z));

        X = x;
        Y = y;
        Z = z;
    }

    public static CadScale3 Identity { get; } = new(1, 1, 1);

    public double X { get; }

    public double Y { get; }

    public double Z { get; }
}

public sealed record CadPolylineVertex
{
    public CadPolylineVertex(CadPoint3 position, double bulge)
    {
        ArgumentNullException.ThrowIfNull(position);
        CadPoint3.ValidateFinite(bulge, nameof(bulge));

        Position = position;
        Bulge = bulge;
    }

    public CadPoint3 Position { get; }

    public double Bulge { get; }
}

public abstract record CadGeometryEntity
{
    protected CadGeometryEntity(int sourceOrder, string layerName, string entityType, CadBounds? bounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrder);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        SourceOrder = sourceOrder;
        LayerName = layerName;
        EntityType = entityType;
        Bounds = bounds ?? CadBounds.NotEvaluated;
    }

    public int SourceOrder { get; }

    public string LayerName { get; }

    public string EntityType { get; }

    public CadBounds Bounds { get; }
}

public sealed record CadLineGeometry : CadGeometryEntity
{
    public CadLineGeometry(
        int sourceOrder,
        string layerName,
        CadPoint3 start,
        CadPoint3 end,
        CadBounds? bounds = null)
        : base(sourceOrder, layerName, "LINE", bounds ?? CreateBounds(start, end))
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        Start = start;
        End = end;
    }

    public CadPoint3 Start { get; }

    public CadPoint3 End { get; }

    private static CadBounds CreateBounds(CadPoint3 start, CadPoint3 end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        return CadBounds.Computed(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Min(start.Z, end.Z),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y),
            Math.Max(start.Z, end.Z));
    }
}

public sealed record CadPolylineGeometry : CadGeometryEntity
{
    public CadPolylineGeometry(
        int sourceOrder,
        string layerName,
        IReadOnlyList<CadPolylineVertex>? vertices,
        bool isClosed,
        CadBounds? bounds = null)
        : base(sourceOrder, layerName, "LWPOLYLINE", bounds)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        Vertices = vertices.ToArray();
        IsClosed = isClosed;
    }

    public IReadOnlyList<CadPolylineVertex> Vertices { get; }

    public bool IsClosed { get; }
}

public sealed record CadArcGeometry : CadGeometryEntity
{
    public CadArcGeometry(
        int sourceOrder,
        string layerName,
        CadPoint3 center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        CadBounds? bounds = null)
        : base(sourceOrder, layerName, "ARC", bounds)
    {
        ArgumentNullException.ThrowIfNull(center);
        ValidateRadius(radius);
        CadPoint3.ValidateFinite(startAngleDegrees, nameof(startAngleDegrees));
        CadPoint3.ValidateFinite(endAngleDegrees, nameof(endAngleDegrees));

        Center = center;
        Radius = radius;
        StartAngleDegrees = startAngleDegrees;
        EndAngleDegrees = endAngleDegrees;
    }

    public CadPoint3 Center { get; }

    public double Radius { get; }

    public double StartAngleDegrees { get; }

    public double EndAngleDegrees { get; }

    internal static void ValidateRadius(double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "CAD radii must be finite and greater than zero.");
        }
    }
}

public sealed record CadCircleGeometry : CadGeometryEntity
{
    public CadCircleGeometry(
        int sourceOrder,
        string layerName,
        CadPoint3 center,
        double radius,
        CadBounds? bounds = null)
        : base(sourceOrder, layerName, "CIRCLE", bounds)
    {
        ArgumentNullException.ThrowIfNull(center);
        CadArcGeometry.ValidateRadius(radius);

        Center = center;
        Radius = radius;
    }

    public CadPoint3 Center { get; }

    public double Radius { get; }
}

public sealed record CadInsertGeometry : CadGeometryEntity
{
    public CadInsertGeometry(
        int sourceOrder,
        string layerName,
        string blockName,
        CadPoint3 position,
        double rotationDegrees,
        CadScale3 scale,
        CadBounds? bounds = null)
        : base(sourceOrder, layerName, "INSERT", bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(scale);
        CadPoint3.ValidateFinite(rotationDegrees, nameof(rotationDegrees));

        BlockName = blockName;
        Position = position;
        RotationDegrees = rotationDegrees;
        Scale = scale;
    }

    public string BlockName { get; }

    public CadPoint3 Position { get; }

    public double RotationDegrees { get; }

    public CadScale3 Scale { get; }
}

public sealed record CadGeometryDocument
{
    public CadDocumentModel Summary { get; init; } = new();

    public IReadOnlyList<CadGeometryEntity> ModelSpaceEntities { get; init; } = Array.Empty<CadGeometryEntity>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public enum CadGeometryNormalizationStatus
{
    Succeeded = 0,
    Failed = 1
}

public sealed record CadCoordinateContext
{
    public CadCoordinateContext(CadUnit sourceUnit, double unitScaleToMeters, CadPoint3 sourceOrigin)
    {
        if (!double.IsFinite(unitScaleToMeters) || unitScaleToMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitScaleToMeters));
        }

        ArgumentNullException.ThrowIfNull(sourceOrigin);

        SourceUnit = sourceUnit;
        UnitScaleToMeters = unitScaleToMeters;
        SourceOrigin = sourceOrigin;
    }

    public CadUnit SourceUnit { get; }

    public double UnitScaleToMeters { get; }

    public CadPoint3 SourceOrigin { get; }
}

public sealed record NormalizedCadGeometryDocument
{
    public CadDocumentModel Summary { get; init; } = new();

    public CadCoordinateContext? CoordinateContext { get; init; }

    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;

    public IReadOnlyList<CadGeometryEntity> Entities { get; init; } = Array.Empty<CadGeometryEntity>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record CadGeometryNormalizationResult
{
    public CadGeometryNormalizationStatus Status { get; init; } = CadGeometryNormalizationStatus.Failed;

    public NormalizedCadGeometryDocument? Document { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed class CadGeometryNormalizer
{
    public CadGeometryNormalizationResult Normalize(CadGeometryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.ModelSpaceEntities.Count == 0 && document.Summary.Bounds.State is CadBoundsState.Empty)
        {
            return new CadGeometryNormalizationResult
            {
                Status = CadGeometryNormalizationStatus.Succeeded,
                Document = new NormalizedCadGeometryDocument
                {
                    Summary = document.Summary,
                    Bounds = CadBounds.Empty,
                    Diagnostics = document.Diagnostics
                }
            };
        }

        if (!TryGetScaleToMeters(document.Summary.Unit, out var scaleToMeters))
        {
            return Failed("GEOMETRY_UNIT_UNRESOLVED", "The CAD unit cannot be normalized to meters.");
        }

        if (document.Summary.Bounds.State is not CadBoundsState.Computed)
        {
            return Failed("GEOMETRY_BOUNDS_NOT_COMPUTED", "The document bounds are required to determine a local origin.");
        }

        var bounds = document.Summary.Bounds;
        var context = new CadCoordinateContext(
            document.Summary.Unit,
            scaleToMeters,
            new CadPoint3(bounds.MinX, bounds.MinY, bounds.MinZ));
        var normalizedEntities = document.ModelSpaceEntities
            .Select(entity => NormalizeEntity(entity, context))
            .ToArray();

        return new CadGeometryNormalizationResult
        {
            Status = CadGeometryNormalizationStatus.Succeeded,
            Document = new NormalizedCadGeometryDocument
            {
                Summary = document.Summary,
                CoordinateContext = context,
                Bounds = NormalizeBounds(document.Summary.Bounds, context),
                Entities = normalizedEntities,
                Diagnostics = document.Diagnostics
            }
        };
    }

    private static CadGeometryNormalizationResult Failed(string code, string message) =>
        new()
        {
            Status = CadGeometryNormalizationStatus.Failed,
            Diagnostics =
            [
                new SceneDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = code,
                    Message = message
                }
            ]
        };

    private static bool TryGetScaleToMeters(CadUnit unit, out double scaleToMeters)
    {
        scaleToMeters = unit switch
        {
            CadUnit.Millimeters => 0.001,
            CadUnit.Centimeters => 0.01,
            CadUnit.Meters => 1,
            CadUnit.Inches => 0.0254,
            CadUnit.Feet => 0.3048,
            _ => 0
        };

        return scaleToMeters > 0;
    }

    private static CadGeometryEntity NormalizeEntity(CadGeometryEntity entity, CadCoordinateContext context)
    {
        var bounds = NormalizeBounds(entity.Bounds, context);

        return entity switch
        {
            CadLineGeometry line => new CadLineGeometry(
                line.SourceOrder,
                line.LayerName,
                line.Start.Transform(context.SourceOrigin, context.UnitScaleToMeters),
                line.End.Transform(context.SourceOrigin, context.UnitScaleToMeters),
                bounds),
            CadPolylineGeometry polyline => new CadPolylineGeometry(
                polyline.SourceOrder,
                polyline.LayerName,
                polyline.Vertices.Select(vertex => new CadPolylineVertex(
                    vertex.Position.Transform(context.SourceOrigin, context.UnitScaleToMeters),
                    vertex.Bulge)).ToArray(),
                polyline.IsClosed,
                bounds),
            CadArcGeometry arc => new CadArcGeometry(
                arc.SourceOrder,
                arc.LayerName,
                arc.Center.Transform(context.SourceOrigin, context.UnitScaleToMeters),
                arc.Radius * context.UnitScaleToMeters,
                arc.StartAngleDegrees,
                arc.EndAngleDegrees,
                bounds),
            CadCircleGeometry circle => new CadCircleGeometry(
                circle.SourceOrder,
                circle.LayerName,
                circle.Center.Transform(context.SourceOrigin, context.UnitScaleToMeters),
                circle.Radius * context.UnitScaleToMeters,
                bounds),
            CadInsertGeometry insert => new CadInsertGeometry(
                insert.SourceOrder,
                insert.LayerName,
                insert.BlockName,
                insert.Position.Transform(context.SourceOrigin, context.UnitScaleToMeters),
                insert.RotationDegrees,
                insert.Scale,
                bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(entity), "Unsupported CAD geometry entity.")
        };
    }

    private static CadBounds NormalizeBounds(CadBounds bounds, CadCoordinateContext context)
    {
        if (bounds.State is not CadBoundsState.Computed)
        {
            return bounds;
        }

        var minimum = new CadPoint3(bounds.MinX, bounds.MinY, bounds.MinZ)
            .Transform(context.SourceOrigin, context.UnitScaleToMeters);
        var maximum = new CadPoint3(bounds.MaxX, bounds.MaxY, bounds.MaxZ)
            .Transform(context.SourceOrigin, context.UnitScaleToMeters);

        return CadBounds.Computed(minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z);
    }
}
