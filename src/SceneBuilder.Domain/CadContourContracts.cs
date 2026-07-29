namespace SceneBuilder.Domain;

public enum CadCurveDirection
{
    CounterClockwise = 0,
    Clockwise = 1
}

public enum CadContourOrientation
{
    Undefined = 0,
    Clockwise = 1,
    CounterClockwise = 2
}

public enum CadContourValidationState
{
    NotEvaluated = 0,
    Valid = 1,
    Invalid = 2
}

public enum CadContourBuildStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2
}

public sealed record CadGeometryTolerance
{
    public CadGeometryTolerance(
        double pointEqualityMeters,
        double zeroLengthMeters,
        double planarityMeters,
        double zeroAreaSquareMeters,
        int arcIntersectionSampleCount)
    {
        ValidateNonNegativeFinite(pointEqualityMeters, nameof(pointEqualityMeters));
        ValidateNonNegativeFinite(zeroLengthMeters, nameof(zeroLengthMeters));
        ValidateNonNegativeFinite(planarityMeters, nameof(planarityMeters));
        ValidateNonNegativeFinite(zeroAreaSquareMeters, nameof(zeroAreaSquareMeters));

        if (arcIntersectionSampleCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(arcIntersectionSampleCount));
        }

        PointEqualityMeters = pointEqualityMeters;
        ZeroLengthMeters = zeroLengthMeters;
        PlanarityMeters = planarityMeters;
        ZeroAreaSquareMeters = zeroAreaSquareMeters;
        ArcIntersectionSampleCount = arcIntersectionSampleCount;
    }

    public static CadGeometryTolerance Default { get; } = new(
        pointEqualityMeters: 0.000001d,
        zeroLengthMeters: 0.000001d,
        planarityMeters: 0.000001d,
        zeroAreaSquareMeters: 0.000000000001d,
        arcIntersectionSampleCount: 32);

    public double PointEqualityMeters { get; }

    public double ZeroLengthMeters { get; }

    public double PlanarityMeters { get; }

    public double ZeroAreaSquareMeters { get; }

    public int ArcIntersectionSampleCount { get; }

    private static void ValidateNonNegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public abstract record CadCurveSegment2
{
    protected CadCurveSegment2(
        int sourceOrder,
        int segmentOrder,
        string sourceLayer,
        string sourceEntityType,
        CadPoint3 start,
        CadPoint3 end,
        CadBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentOrder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLayer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityType);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        ArgumentNullException.ThrowIfNull(bounds);

        SourceOrder = sourceOrder;
        SegmentOrder = segmentOrder;
        SourceLayer = sourceLayer;
        SourceEntityType = sourceEntityType;
        Start = start;
        End = end;
        Bounds = bounds;
    }

    public int SourceOrder { get; }

    public int SegmentOrder { get; }

    public string SourceLayer { get; }

    public string SourceEntityType { get; }

    public CadPoint3 Start { get; }

    public CadPoint3 End { get; }

    public CadBounds Bounds { get; }

    public virtual string Id => $"segment:{SourceOrder:D6}:{SegmentOrder:D6}";

    public virtual double LengthMeters => CadContourMath.Distance2(Start, End);
}

public sealed record CadLineSegment2 : CadCurveSegment2
{
    public CadLineSegment2(
        int sourceOrder,
        int segmentOrder,
        string sourceLayer,
        string sourceEntityType,
        CadPoint3 start,
        CadPoint3 end)
        : base(sourceOrder, segmentOrder, sourceLayer, sourceEntityType, start, end, CadContourMath.BoundsForPoints([start, end]))
    {
    }
}

public sealed record CadArcSegment2 : CadCurveSegment2
{
    public CadArcSegment2(
        int sourceOrder,
        int segmentOrder,
        string sourceLayer,
        string sourceEntityType,
        CadPoint3 center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        CadCurveDirection direction,
        CadPoint3? exactStart = null,
        CadPoint3? exactEnd = null)
        : base(
            sourceOrder,
            segmentOrder,
            sourceLayer,
            sourceEntityType,
            exactStart ?? PointAt(center, radius, startAngleDegrees),
            exactEnd ?? PointAt(center, radius, endAngleDegrees),
            CadContourMath.BoundsForArc(center, radius, startAngleDegrees, endAngleDegrees, direction))
    {
        ArgumentNullException.ThrowIfNull(center);
        CadArcGeometry.ValidateRadius(radius);
        CadPoint3.ValidateFinite(startAngleDegrees, nameof(startAngleDegrees));
        CadPoint3.ValidateFinite(endAngleDegrees, nameof(endAngleDegrees));
        ValidateExactEndpoint(center, radius, startAngleDegrees, exactStart, nameof(exactStart));
        ValidateExactEndpoint(center, radius, endAngleDegrees, exactEnd, nameof(exactEnd));

        Center = center;
        Radius = radius;
        StartAngleDegrees = startAngleDegrees;
        EndAngleDegrees = endAngleDegrees;
        Direction = direction;
    }

    public CadPoint3 Center { get; }

    public double Radius { get; }

    public double StartAngleDegrees { get; }

    public double EndAngleDegrees { get; }

    public CadCurveDirection Direction { get; }

    public double SignedSweepRadians => CadContourMath.SignedSweepRadians(StartAngleDegrees, EndAngleDegrees, Direction);

    public override double LengthMeters => Radius * Math.Abs(SignedSweepRadians);

    public CadPoint3 PointAtFraction(double fraction)
    {
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        if (fraction == 0)
        {
            return Start;
        }

        if (fraction == 1)
        {
            return End;
        }

        var angleDegrees = StartAngleDegrees + (SignedSweepRadians * fraction * 180d / Math.PI);
        return PointAt(Center, Radius, angleDegrees);
    }

    private static CadPoint3 PointAt(CadPoint3 center, double radius, double angleDegrees)
    {
        ArgumentNullException.ThrowIfNull(center);
        CadArcGeometry.ValidateRadius(radius);
        CadPoint3.ValidateFinite(angleDegrees, nameof(angleDegrees));

        var radians = angleDegrees * Math.PI / 180d;
        return new CadPoint3(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)),
            center.Z);
    }

    private static void ValidateExactEndpoint(
        CadPoint3 center,
        double radius,
        double expectedAngleDegrees,
        CadPoint3? endpoint,
        string parameterName)
    {
        if (endpoint is null)
        {
            return;
        }

        var allowedDifference = Math.Max(1d, radius) * 0.000000001d;
        var expected = PointAt(center, radius, expectedAngleDegrees);
        if (CadContourMath.Distance2(expected, endpoint) > allowedDifference ||
            Math.Abs(endpoint.Z - expected.Z) > allowedDifference)
        {
            throw new ArgumentException("Exact arc endpoints must match the declared circle and angle.", parameterName);
        }
    }
}

public sealed record CadContourDiagnostic
{
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;
}

public abstract record CadContour
{
    public string Id { get; init; } = string.Empty;

    public bool IsSourceDefinedClosed { get; init; }

    public bool IsClosed { get; init; }

    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;

    public double SignedAreaSquareMeters { get; init; }

    public CadContourOrientation Orientation { get; init; } = CadContourOrientation.Undefined;

    public CadContourValidationState ValidationState { get; init; } = CadContourValidationState.NotEvaluated;

    public IReadOnlyList<CadContourDiagnostic> Diagnostics { get; init; } = Array.Empty<CadContourDiagnostic>();
}

public sealed record CadSegmentContour : CadContour
{
    public CadSegmentContour(
        string id,
        IReadOnlyList<CadCurveSegment2>? segments,
        bool isSourceDefinedClosed,
        IReadOnlyList<CadContourDiagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(segments);

        Id = id;
        IsSourceDefinedClosed = isSourceDefinedClosed;
        Segments = segments.ToArray();
        Bounds = CadContourMath.BoundsForSegments(Segments);
        SignedAreaSquareMeters = CadContourMath.SignedAreaForSegments(Segments);
        Diagnostics = diagnostics?.ToArray() ?? Array.Empty<CadContourDiagnostic>();
    }

    public IReadOnlyList<CadCurveSegment2> Segments { get; init; } = Array.Empty<CadCurveSegment2>();
}

public sealed record CadCircleContour : CadContour
{
    public CadCircleContour(
        string id,
        int sourceOrder,
        string sourceLayer,
        CadPoint3 center,
        double radius)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLayer);
        ArgumentNullException.ThrowIfNull(center);
        CadArcGeometry.ValidateRadius(radius);

        Id = id;
        IsSourceDefinedClosed = true;
        IsClosed = true;
        SourceOrder = sourceOrder;
        SourceLayer = sourceLayer;
        Center = center;
        Radius = radius;
        Bounds = CadBounds.Computed(
            center.X - radius,
            center.Y - radius,
            center.Z,
            center.X + radius,
            center.Y + radius,
            center.Z);
        SignedAreaSquareMeters = Math.PI * radius * radius;
        Orientation = CadContourOrientation.CounterClockwise;
    }

    public int SourceOrder { get; }

    public string SourceLayer { get; }

    public CadPoint3 Center { get; }

    public double Radius { get; }
}

public sealed record CadContourDocument
{
    public IReadOnlyList<CadContour> Contours { get; init; } = Array.Empty<CadContour>();

    public IReadOnlyList<CadCurveSegment2> OpenSegments { get; init; } = Array.Empty<CadCurveSegment2>();

    public IReadOnlyList<CadContourDiagnostic> Diagnostics { get; init; } = Array.Empty<CadContourDiagnostic>();
}

public sealed record CadContourBuildResult
{
    public CadContourBuildStatus Status { get; init; } = CadContourBuildStatus.Failed;

    public CadContourDocument? Document { get; init; }

    public IReadOnlyList<CadContourDiagnostic> Diagnostics { get; init; } = Array.Empty<CadContourDiagnostic>();
}

internal static class CadContourMath
{
    internal static double Distance2(CadPoint3 first, CadPoint3 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    internal static CadBounds BoundsForPoints(IReadOnlyList<CadPoint3> points)
    {
        if (points.Count == 0)
        {
            return CadBounds.Empty;
        }

        return CadBounds.Computed(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Min(point => point.Z),
            points.Max(point => point.X),
            points.Max(point => point.Y),
            points.Max(point => point.Z));
    }

    internal static CadBounds BoundsForSegments(IReadOnlyList<CadCurveSegment2> segments)
    {
        if (segments.Count == 0)
        {
            return CadBounds.Empty;
        }

        return CadBounds.Computed(
            segments.Min(segment => segment.Bounds.MinX),
            segments.Min(segment => segment.Bounds.MinY),
            segments.Min(segment => segment.Bounds.MinZ),
            segments.Max(segment => segment.Bounds.MaxX),
            segments.Max(segment => segment.Bounds.MaxY),
            segments.Max(segment => segment.Bounds.MaxZ));
    }

    internal static CadBounds BoundsForArc(
        CadPoint3 center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        CadCurveDirection direction)
    {
        var points = new List<CadPoint3>
        {
            PointOnArc(center, radius, startAngleDegrees),
            PointOnArc(center, radius, endAngleDegrees)
        };

        foreach (var cardinalAngle in new[] { 0d, 90d, 180d, 270d })
        {
            if (IsAngleOnArc(cardinalAngle, startAngleDegrees, endAngleDegrees, direction))
            {
                points.Add(PointOnArc(center, radius, cardinalAngle));
            }
        }

        return BoundsForPoints(points);
    }

    internal static double SignedAreaForSegments(IReadOnlyList<CadCurveSegment2> segments) =>
        segments.Sum(SignedAreaContribution);

    internal static double SignedAreaContribution(CadCurveSegment2 segment) =>
        segment switch
        {
            CadLineSegment2 line => 0.5d * ((line.Start.X * line.End.Y) - (line.End.X * line.Start.Y)),
            CadArcSegment2 arc => SignedAreaContribution(arc),
            _ => 0
        };

    internal static double SignedSweepRadians(double startAngleDegrees, double endAngleDegrees, CadCurveDirection direction)
    {
        var start = NormalizeDegrees(startAngleDegrees);
        var end = NormalizeDegrees(endAngleDegrees);
        var unsignedDegrees = direction is CadCurveDirection.CounterClockwise
            ? NormalizeDegrees(end - start)
            : NormalizeDegrees(start - end);
        var radians = unsignedDegrees * Math.PI / 180d;
        return direction is CadCurveDirection.CounterClockwise ? radians : -radians;
    }

    internal static bool AreEqual2(CadPoint3 first, CadPoint3 second, double tolerance) =>
        Distance2(first, second) <= tolerance;

    internal static bool IsAngleOnArc(
        double candidateDegrees,
        double startAngleDegrees,
        double endAngleDegrees,
        CadCurveDirection direction)
    {
        var candidate = NormalizeDegrees(candidateDegrees);
        var start = NormalizeDegrees(startAngleDegrees);
        var sweep = Math.Abs(SignedSweepRadians(startAngleDegrees, endAngleDegrees, direction)) * 180d / Math.PI;
        var distance = direction is CadCurveDirection.CounterClockwise
            ? NormalizeDegrees(candidate - start)
            : NormalizeDegrees(start - candidate);

        return distance <= sweep + 0.000000000001d;
    }

    private static double SignedAreaContribution(CadArcSegment2 arc)
    {
        var startRadians = arc.StartAngleDegrees * Math.PI / 180d;
        var endRadians = startRadians + arc.SignedSweepRadians;
        var integral =
            (arc.Radius * arc.Center.X * (Math.Sin(endRadians) - Math.Sin(startRadians))) +
            (arc.Radius * arc.Center.Y * (Math.Cos(startRadians) - Math.Cos(endRadians))) +
            (arc.Radius * arc.Radius * (endRadians - startRadians));
        return integral / 2d;
    }

    private static CadPoint3 PointOnArc(CadPoint3 center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new CadPoint3(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)),
            center.Z);
    }

    private static double NormalizeDegrees(double angleDegrees)
    {
        var normalized = angleDegrees % 360d;
        return normalized < 0 ? normalized + 360d : normalized;
    }
}
