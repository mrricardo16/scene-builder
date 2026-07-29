namespace SceneBuilder.Domain;

public sealed class CadContourBuilder
{
    private readonly CadContourValidator _validator;
    private readonly CadGeometryTolerance _tolerance;

    public CadContourBuilder(
        CadGeometryTolerance? tolerance = null,
        CadContourValidator? validator = null)
    {
        _tolerance = tolerance ?? CadGeometryTolerance.Default;
        _validator = validator ?? new CadContourValidator();
    }

    public CadContourBuildResult Build(NormalizedCadGeometryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Entities.Count > 0 && document.CoordinateContext is null)
        {
            return Failed("GEOMETRY_COORDINATE_CONTEXT_REQUIRED", "Normalized geometry requires a coordinate context.");
        }

        var contours = new List<CadContour>();
        var openSegments = new List<CadCurveSegment2>();
        var documentDiagnostics = new List<CadContourDiagnostic>();

        foreach (var entity in document.Entities.OrderBy(entity => entity.SourceOrder))
        {
            switch (entity)
            {
                case CadLineGeometry line:
                    openSegments.Add(new CadLineSegment2(
                        line.SourceOrder,
                        0,
                        line.LayerName,
                        line.EntityType,
                        line.Start,
                        line.End));
                    break;
                case CadArcGeometry arc:
                    openSegments.Add(new CadArcSegment2(
                        arc.SourceOrder,
                        0,
                        arc.LayerName,
                        arc.EntityType,
                        arc.Center,
                        arc.Radius,
                        arc.StartAngleDegrees,
                        arc.EndAngleDegrees,
                        CadCurveDirection.CounterClockwise));
                    break;
                case CadPolylineGeometry polyline:
                    AddPolyline(polyline, contours, openSegments, documentDiagnostics);
                    break;
                case CadCircleGeometry circle:
                    contours.Add(_validator.Validate(
                        new CadCircleContour(
                            CreateContourId(circle.SourceOrder),
                            circle.SourceOrder,
                            circle.LayerName,
                            circle.Center,
                            circle.Radius),
                        _tolerance));
                    break;
                case CadInsertGeometry:
                    break;
            }
        }

        documentDiagnostics.AddRange(contours.SelectMany(contour => contour.Diagnostics));
        var documentResult = new CadContourDocument
        {
            Contours = contours,
            OpenSegments = openSegments,
            Diagnostics = documentDiagnostics
        };

        return new CadContourBuildResult
        {
            Status = contours.Any(contour => contour.ValidationState is CadContourValidationState.Invalid)
                ? CadContourBuildStatus.PartiallySucceeded
                : CadContourBuildStatus.Succeeded,
            Document = documentResult,
            Diagnostics = documentDiagnostics
        };
    }

    private void AddPolyline(
        CadPolylineGeometry polyline,
        ICollection<CadContour> contours,
        ICollection<CadCurveSegment2> openSegments,
        ICollection<CadContourDiagnostic> documentDiagnostics)
    {
        var conversion = CreatePolylineSegments(polyline);
        if (polyline.IsClosed)
        {
            var contour = new CadSegmentContour(
                CreateContourId(polyline.SourceOrder),
                conversion.Segments,
                isSourceDefinedClosed: true,
                conversion.Diagnostics);
            contours.Add(_validator.Validate(contour, _tolerance));
            return;
        }

        foreach (var segment in conversion.Segments)
        {
            openSegments.Add(segment);
        }

        foreach (var diagnostic in conversion.Diagnostics)
        {
            documentDiagnostics.Add(diagnostic);
        }
    }

    private PolylineSegmentConversion CreatePolylineSegments(CadPolylineGeometry polyline)
    {
        var segments = new List<CadCurveSegment2>();
        var diagnostics = new List<CadContourDiagnostic>();
        var vertices = polyline.Vertices;
        for (var index = 0; index < vertices.Count - 1; index++)
        {
            segments.Add(CreatePolylineSegment(polyline, vertices[index], vertices[index + 1], index, diagnostics));
        }

        if (polyline.IsClosed && vertices.Count > 1 && !CadContourMath.AreEqual2(
                vertices[^1].Position,
                vertices[0].Position,
                _tolerance.PointEqualityMeters))
        {
            segments.Add(CreatePolylineSegment(
                polyline,
                vertices[^1],
                vertices[0],
                vertices.Count - 1,
                diagnostics));
        }

        return new PolylineSegmentConversion(segments, diagnostics);
    }

    private CadCurveSegment2 CreatePolylineSegment(
        CadPolylineGeometry polyline,
        CadPolylineVertex start,
        CadPolylineVertex end,
        int segmentOrder,
        ICollection<CadContourDiagnostic> diagnostics)
    {
        if (start.Bulge == 0)
        {
            return new CadLineSegment2(
                polyline.SourceOrder,
                segmentOrder,
                polyline.LayerName,
                polyline.EntityType,
                start.Position,
                end.Position);
        }

        var chordLength = CadContourMath.Distance2(start.Position, end.Position);
        if (chordLength <= _tolerance.ZeroLengthMeters)
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_BULGE_CHORD_TOO_SHORT",
                "A nonzero bulge has a chord shorter than the configured tolerance.",
                CreateSourceSubject(polyline.SourceOrder)));
            return new CadLineSegment2(
                polyline.SourceOrder,
                segmentOrder,
                polyline.LayerName,
                polyline.EntityType,
                start.Position,
                end.Position);
        }

        var includedAngle = 4d * Math.Atan(start.Bulge);
        var absoluteHalfAngle = Math.Abs(includedAngle) / 2d;
        var radius = chordLength / (2d * Math.Sin(absoluteHalfAngle));
        var midpointX = (start.Position.X + end.Position.X) / 2d;
        var midpointY = (start.Position.Y + end.Position.Y) / 2d;
        var perpendicularX = -(end.Position.Y - start.Position.Y) / chordLength;
        var perpendicularY = (end.Position.X - start.Position.X) / chordLength;
        var centerDistance = chordLength / (2d * Math.Tan(absoluteHalfAngle));
        var direction = start.Bulge > 0
            ? CadCurveDirection.CounterClockwise
            : CadCurveDirection.Clockwise;
        var side = direction is CadCurveDirection.CounterClockwise ? 1d : -1d;
        var center = new CadPoint3(
            midpointX + (side * perpendicularX * centerDistance),
            midpointY + (side * perpendicularY * centerDistance),
            start.Position.Z);
        var startAngleDegrees = Math.Atan2(start.Position.Y - center.Y, start.Position.X - center.X) * 180d / Math.PI;
        var endAngleDegrees = Math.Atan2(end.Position.Y - center.Y, end.Position.X - center.X) * 180d / Math.PI;

        return new CadArcSegment2(
            polyline.SourceOrder,
            segmentOrder,
            polyline.LayerName,
            polyline.EntityType,
            center,
            radius,
            startAngleDegrees,
            endAngleDegrees,
            direction,
            start.Position,
            end.Position);
    }

    private static CadContourBuildResult Failed(string code, string message)
    {
        var diagnostic = CreateDiagnostic(code, message, "document");
        return new CadContourBuildResult
        {
            Status = CadContourBuildStatus.Failed,
            Diagnostics = [diagnostic]
        };
    }

    private static CadContourDiagnostic CreateDiagnostic(string code, string message, string subject) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            Subject = subject
        };

    private static string CreateContourId(int sourceOrder) => $"contour:{sourceOrder:D4}";

    private static string CreateSourceSubject(int sourceOrder) => $"source-order:{sourceOrder}";

    private sealed record PolylineSegmentConversion(
        IReadOnlyList<CadCurveSegment2> Segments,
        IReadOnlyList<CadContourDiagnostic> Diagnostics);
}

public sealed class CadContourValidator
{
    public CadContour Validate(CadContour contour, CadGeometryTolerance? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(contour);
        var effectiveTolerance = tolerance ?? CadGeometryTolerance.Default;
        return contour switch
        {
            CadSegmentContour segmentContour => ValidateSegments(segmentContour, effectiveTolerance),
            CadCircleContour circleContour => ValidateCircle(circleContour, effectiveTolerance),
            _ => throw new ArgumentOutOfRangeException(nameof(contour))
        };
    }

    private static CadSegmentContour ValidateSegments(CadSegmentContour contour, CadGeometryTolerance tolerance)
    {
        var diagnostics = contour.Diagnostics.ToList();
        var segments = contour.Segments;
        if (segments.Count < 2)
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_SEGMENT_COUNT_INSUFFICIENT",
                "A source-defined contour requires at least two segments.",
                contour.Id));
        }

        foreach (var segment in segments.Where(segment => segment.LengthMeters <= tolerance.ZeroLengthMeters))
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_ZERO_LENGTH_SEGMENT",
                "A contour segment is shorter than the configured zero-length tolerance.",
                CreateSegmentSubject(segment)));
        }

        var areSegmentsContinuous = true;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!CadContourMath.AreEqual2(segments[index].End, segments[index + 1].Start, tolerance.PointEqualityMeters))
            {
                areSegmentsContinuous = false;
                diagnostics.Add(CreateDiagnostic(
                    "CONTOUR_SEGMENTS_DISCONNECTED",
                    "Consecutive contour segments are not continuous in XY.",
                    contour.Id));
                break;
            }
        }

        var isClosed = contour.IsSourceDefinedClosed && areSegmentsContinuous && segments.Count > 0 &&
            CadContourMath.AreEqual2(segments[^1].End, segments[0].Start, tolerance.PointEqualityMeters);
        if (contour.IsSourceDefinedClosed && !isClosed)
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_NOT_CLOSED",
                "A source-defined closed contour does not close in XY.",
                contour.Id));
        }

        if (HasNonPlanarPoints(segments, tolerance.PlanarityMeters))
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_NON_PLANAR",
                "Contour point elevations exceed the configured planarity tolerance.",
                contour.Id));
        }

        var signedArea = CadContourMath.SignedAreaForSegments(segments);
        if (Math.Abs(signedArea) <= tolerance.ZeroAreaSquareMeters)
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_AREA_TOO_SMALL",
                "Contour signed area is within the configured zero-area tolerance.",
                contour.Id));
        }

        if (HasSelfIntersection(segments, tolerance))
        {
            diagnostics.Add(CreateDiagnostic(
                "CONTOUR_SELF_INTERSECTION",
                "Contour segments intersect away from their permitted shared endpoints.",
                contour.Id));
        }

        var state = diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)
            ? CadContourValidationState.Invalid
            : CadContourValidationState.Valid;
        return contour with
        {
            IsClosed = isClosed,
            Bounds = CadContourMath.BoundsForSegments(segments),
            SignedAreaSquareMeters = signedArea,
            Orientation = GetOrientation(signedArea, tolerance.ZeroAreaSquareMeters),
            ValidationState = state,
            Diagnostics = diagnostics
        };
    }

    private static CadCircleContour ValidateCircle(CadCircleContour contour, CadGeometryTolerance tolerance)
    {
        var signedArea = Math.PI * contour.Radius * contour.Radius;
        return contour with
        {
            IsClosed = true,
            SignedAreaSquareMeters = signedArea,
            Orientation = GetOrientation(signedArea, tolerance.ZeroAreaSquareMeters),
            ValidationState = CadContourValidationState.Valid,
            Diagnostics = Array.Empty<CadContourDiagnostic>()
        };
    }

    private static bool HasNonPlanarPoints(IReadOnlyList<CadCurveSegment2> segments, double tolerance)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        var elevations = segments.SelectMany(segment => new[] { segment.Start.Z, segment.End.Z });
        return elevations.Max() - elevations.Min() > tolerance;
    }

    private static bool HasSelfIntersection(IReadOnlyList<CadCurveSegment2> segments, CadGeometryTolerance tolerance)
    {
        var pieces = segments
            .SelectMany((segment, segmentIndex) => SamplePieces(segment, segmentIndex, tolerance.ArcIntersectionSampleCount))
            .ToArray();
        for (var left = 0; left < pieces.Length; left++)
        {
            for (var right = left + 1; right < pieces.Length; right++)
            {
                if (pieces[left].SegmentIndex == pieces[right].SegmentIndex)
                {
                    continue;
                }

                var permittedSharedEndpoint = GetPermittedSharedEndpoint(
                    pieces[left].SegmentIndex,
                    pieces[right].SegmentIndex,
                    segments);
                if (HasForbiddenIntersection(
                        pieces[left],
                        pieces[right],
                        permittedSharedEndpoint,
                        tolerance.PointEqualityMeters))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<SampledLinePiece> SamplePieces(
        CadCurveSegment2 segment,
        int segmentIndex,
        int arcSampleCount)
    {
        if (segment is not CadArcSegment2 arc)
        {
            yield return new SampledLinePiece(segmentIndex, segment.Start, segment.End);
            yield break;
        }

        var pieceCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.SignedSweepRadians) / (2d * Math.PI) * arcSampleCount));
        var start = arc.PointAtFraction(0);
        for (var index = 1; index <= pieceCount; index++)
        {
            var end = arc.PointAtFraction((double)index / pieceCount);
            yield return new SampledLinePiece(segmentIndex, start, end);
            start = end;
        }
    }

    private static bool AreAdjacent(int first, int second, int segmentCount) =>
        Math.Abs(first - second) == 1 ||
        (segmentCount > 1 &&
            ((first == 0 && second == segmentCount - 1) ||
             (second == 0 && first == segmentCount - 1)));

    private static CadPoint3? GetPermittedSharedEndpoint(
        int firstSegmentIndex,
        int secondSegmentIndex,
        IReadOnlyList<CadCurveSegment2> segments)
    {
        if (!AreAdjacent(firstSegmentIndex, secondSegmentIndex, segments.Count))
        {
            return null;
        }

        if (Math.Abs(firstSegmentIndex - secondSegmentIndex) == 1)
        {
            var earlierIndex = Math.Min(firstSegmentIndex, secondSegmentIndex);
            return segments[earlierIndex].End;
        }

        return segments[0].Start;
    }

    private static bool HasForbiddenIntersection(
        SampledLinePiece first,
        SampledLinePiece second,
        CadPoint3? permittedSharedEndpoint,
        double tolerance)
    {
        var intersections = new List<CadPoint3>();
        AddIfOnSegment(first.Start, second, intersections, tolerance);
        AddIfOnSegment(first.End, second, intersections, tolerance);
        AddIfOnSegment(second.Start, first, intersections, tolerance);
        AddIfOnSegment(second.End, first, intersections, tolerance);

        if (intersections.Count == 0)
        {
            return ProperlyCrosses(first, second, tolerance);
        }

        return permittedSharedEndpoint is null ||
            intersections.Any(point => !CadContourMath.AreEqual2(point, permittedSharedEndpoint, tolerance));
    }

    private static void AddIfOnSegment(
        CadPoint3 point,
        SampledLinePiece segment,
        ICollection<CadPoint3> intersections,
        double tolerance)
    {
        if (!IsPointOnSegment(point, segment, tolerance) ||
            intersections.Any(existing => CadContourMath.AreEqual2(existing, point, tolerance)))
        {
            return;
        }

        intersections.Add(point);
    }

    private static bool IsPointOnSegment(CadPoint3 point, SampledLinePiece segment, double tolerance)
    {
        var length = CadContourMath.Distance2(segment.Start, segment.End);
        if (length <= tolerance)
        {
            return CadContourMath.AreEqual2(point, segment.Start, tolerance);
        }

        if (Math.Abs(Cross(segment.Start, segment.End, point)) > length * tolerance)
        {
            return false;
        }

        return point.X >= Math.Min(segment.Start.X, segment.End.X) - tolerance &&
            point.X <= Math.Max(segment.Start.X, segment.End.X) + tolerance &&
            point.Y >= Math.Min(segment.Start.Y, segment.End.Y) - tolerance &&
            point.Y <= Math.Max(segment.Start.Y, segment.End.Y) + tolerance;
    }

    private static bool ProperlyCrosses(SampledLinePiece first, SampledLinePiece second, double tolerance)
    {
        var firstCrossStart = Cross(first.Start, first.End, second.Start);
        var firstCrossEnd = Cross(first.Start, first.End, second.End);
        var secondCrossStart = Cross(second.Start, second.End, first.Start);
        var secondCrossEnd = Cross(second.Start, second.End, first.End);

        return OppositeSides(firstCrossStart, firstCrossEnd, tolerance) &&
            OppositeSides(secondCrossStart, secondCrossEnd, tolerance);
    }

    private static bool OppositeSides(double first, double second, double tolerance) =>
        (first > tolerance && second < -tolerance) || (first < -tolerance && second > tolerance);

    private static double Cross(CadPoint3 start, CadPoint3 end, CadPoint3 point) =>
        ((end.X - start.X) * (point.Y - start.Y)) - ((end.Y - start.Y) * (point.X - start.X));

    private static CadContourOrientation GetOrientation(double signedArea, double tolerance) =>
        signedArea > tolerance
            ? CadContourOrientation.CounterClockwise
            : signedArea < -tolerance
                ? CadContourOrientation.Clockwise
                : CadContourOrientation.Undefined;

    private static CadContourDiagnostic CreateDiagnostic(string code, string message, string subject) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            Subject = subject
        };

    private static string CreateSegmentSubject(CadCurveSegment2 segment) =>
        $"source-order:{segment.SourceOrder}:segment:{segment.SegmentOrder}";

    private sealed record SampledLinePiece(int SegmentIndex, CadPoint3 Start, CadPoint3 End);
}
