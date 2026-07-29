namespace SceneBuilder.Domain.Tests;

public sealed class CadContourBuilderTests
{
    [Fact]
    public void Tolerance_rejects_non_finite_and_negative_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CadGeometryTolerance(double.NaN, 0, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CadGeometryTolerance(0, -1, 0, 0, 1));

        Assert.Equal(0.000001d, CadGeometryTolerance.Default.PointEqualityMeters);
    }

    [Fact]
    public void Arc_crossing_zero_degrees_includes_cardinal_extremum_in_bounds()
    {
        var arc = new CadArcSegment2(
            1,
            0,
            "SYN",
            "ARC",
            new CadPoint3(0, 0, 0),
            2,
            350,
            10,
            CadCurveDirection.CounterClockwise);

        Assert.Equal(2, arc.Bounds.MaxX, precision: 10);
        Assert.Equal(-0.3472963553, arc.Bounds.MinY, precision: 10);
        Assert.Equal(0.3472963553, arc.Bounds.MaxY, precision: 10);
    }

    [Fact]
    public void Arc_rejects_exact_endpoints_that_do_not_lie_on_its_circle()
    {
        Assert.Throws<ArgumentException>(() => new CadArcSegment2(
            0,
            0,
            "SYN",
            "ARC",
            new CadPoint3(0, 0, 0),
            1,
            0,
            90,
            CadCurveDirection.CounterClockwise,
            exactStart: new CadPoint3(2, 0, 0)));
    }

    [Fact]
    public void Arc_rejects_exact_endpoints_that_do_not_match_declared_angles()
    {
        Assert.Throws<ArgumentException>(() => new CadArcSegment2(
            0,
            0,
            "SYN",
            "ARC",
            new CadPoint3(0, 0, 0),
            1,
            0,
            90,
            CadCurveDirection.CounterClockwise,
            exactStart: new CadPoint3(0, 1, 0)));
    }

    [Theory]
    [InlineData(0, typeof(CadLineSegment2))]
    [InlineData(1, typeof(CadArcSegment2))]
    [InlineData(-1, typeof(CadArcSegment2))]
    [InlineData(0.0001, typeof(CadArcSegment2))]
    [InlineData(-0.0001, typeof(CadArcSegment2))]
    public void Polyline_bulge_conversion_preserves_endpoints_and_direction(double bulge, Type expectedType)
    {
        var document = CreateNormalizedDocument(
            new CadPolylineGeometry(
                0,
                "SYN",
                [
                    new CadPolylineVertex(new CadPoint3(0, 0, 0), bulge),
                    new CadPolylineVertex(new CadPoint3(2, 0, 0), 0)
                ],
                isClosed: false));

        var result = new CadContourBuilder().Build(document);
        var segment = Assert.Single(result.Document!.OpenSegments);

        Assert.IsType(expectedType, segment);
        Assert.Equal(new CadPoint3(0, 0, 0), segment.Start);
        Assert.Equal(new CadPoint3(2, 0, 0), segment.End);
        if (segment is CadArcSegment2 arc)
        {
            Assert.Equal(bulge > 0 ? CadCurveDirection.CounterClockwise : CadCurveDirection.Clockwise, arc.Direction);
            Assert.True(double.IsFinite(arc.Radius));
        }
    }

    [Fact]
    public void Closed_rectangle_builds_valid_counterclockwise_contour_with_signed_area()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadPolylineGeometry(
                3,
                "SYN_OUTLINE",
                [
                    Vertex(0, 0), Vertex(4, 0), Vertex(4, 2), Vertex(0, 2)
                ],
                isClosed: true)));

        Assert.Equal(CadContourBuildStatus.Succeeded, result.Status);
        var contour = Assert.IsType<CadSegmentContour>(Assert.Single(result.Document!.Contours));
        Assert.Equal("contour:0003", contour.Id);
        Assert.True(contour.IsClosed);
        Assert.Equal(CadContourValidationState.Valid, contour.ValidationState);
        Assert.Equal(CadContourOrientation.CounterClockwise, contour.Orientation);
        Assert.Equal(8, contour.SignedAreaSquareMeters, precision: 10);
        Assert.Equal(new CadBounds(0, 0, 0, 4, 2, 0), contour.Bounds);
        Assert.Equal(4, contour.Segments.Count);
        Assert.Empty(contour.Diagnostics);
    }

    [Fact]
    public void Reversed_closed_rectangle_is_clockwise_with_matching_absolute_area()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadPolylineGeometry(
                0,
                "SYN",
                [Vertex(0, 0), Vertex(0, 2), Vertex(4, 2), Vertex(4, 0)],
                isClosed: true)));

        var contour = Assert.IsType<CadSegmentContour>(Assert.Single(result.Document!.Contours));
        Assert.Equal(CadContourOrientation.Clockwise, contour.Orientation);
        Assert.Equal(-8, contour.SignedAreaSquareMeters, precision: 10);
    }

    [Fact]
    public void Closed_polyline_with_repeated_first_vertex_does_not_add_a_zero_length_closure_segment()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadPolylineGeometry(
                5,
                "SYN",
                [Vertex(0, 0), Vertex(2, 0), Vertex(2, 1), Vertex(0, 1), Vertex(0, 0)],
                isClosed: true)));

        var contour = Assert.IsType<CadSegmentContour>(Assert.Single(result.Document!.Contours));
        Assert.Equal(4, contour.Segments.Count);
        Assert.DoesNotContain(contour.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_ZERO_LENGTH_SEGMENT");
        Assert.Equal(CadContourValidationState.Valid, contour.ValidationState);
    }

    [Fact]
    public void Closed_bulge_polyline_retains_arc_and_uses_analytic_arc_area()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadPolylineGeometry(
                2,
                "SYN",
                [Vertex(0, 0, bulge: 1), Vertex(2, 0), Vertex(2, 1), Vertex(0, 0)],
                isClosed: true)));

        var contour = Assert.IsType<CadSegmentContour>(Assert.Single(result.Document!.Contours));
        Assert.Equal(CadContourValidationState.Valid, contour.ValidationState);
        Assert.Contains(contour.Segments, segment => segment is CadArcSegment2);
        Assert.Equal(1 + (Math.PI / 2), contour.SignedAreaSquareMeters, precision: 10);
    }

    [Fact]
    public void Circle_builds_natural_closed_contour_without_self_intersection()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadCircleGeometry(4, "SYN", new CadPoint3(5, 5, 0), 2)));

        var contour = Assert.IsType<CadCircleContour>(Assert.Single(result.Document!.Contours));
        Assert.Equal(CadContourValidationState.Valid, contour.ValidationState);
        Assert.Equal(Math.PI * 4, contour.SignedAreaSquareMeters, precision: 10);
        Assert.Equal(new CadBounds(3, 3, 0, 7, 7, 0), contour.Bounds);
        Assert.DoesNotContain(contour.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_SELF_INTERSECTION");
    }

    [Fact]
    public void Open_entities_remain_open_and_insert_is_ignored()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadLineGeometry(0, "SYN", new CadPoint3(0, 0, 0), new CadPoint3(1, 0, 0)),
            new CadArcGeometry(1, "SYN", new CadPoint3(0, 0, 0), 1, 0, 90),
            new CadInsertGeometry(2, "SYN", "SYN_BLOCK", new CadPoint3(0, 0, 0), 0, CadScale3.Identity)));

        Assert.Equal(CadContourBuildStatus.Succeeded, result.Status);
        Assert.Empty(result.Document!.Contours);
        Assert.Equal(2, result.Document.OpenSegments.Count);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Invalid_candidates_are_retained_and_result_is_partially_succeeded()
    {
        var result = new CadContourBuilder().Build(CreateNormalizedDocument(
            new CadPolylineGeometry(0, "SYN", [Vertex(0, 0), Vertex(2, 0), Vertex(2, 2), Vertex(0, 2)], true),
            new CadPolylineGeometry(1, "SYN", [Vertex(0, 0), Vertex(2, 2), Vertex(0, 2), Vertex(2, 0)], true),
            new CadLineGeometry(2, "SYN", new CadPoint3(0, 0, 0), new CadPoint3(1, 0, 0))));

        Assert.Equal(CadContourBuildStatus.PartiallySucceeded, result.Status);
        Assert.Equal(2, result.Document!.Contours.Count);
        Assert.Contains(result.Document.Contours, contour => contour.ValidationState == CadContourValidationState.Valid);
        Assert.Contains(result.Document.Contours, contour => contour.Diagnostics.Any(diagnostic => diagnostic.Code == "CONTOUR_SELF_INTERSECTION"));
        Assert.Single(result.Document.OpenSegments);
    }

    [Fact]
    public void Validator_reports_non_planar_zero_length_small_area_and_declared_not_closed()
    {
        var contour = new CadSegmentContour(
            "contour:0007",
            [
                new CadLineSegment2(7, 0, "SYN", "LWPOLYLINE", new CadPoint3(0, 0, 0), new CadPoint3(0, 0, 0)),
                new CadLineSegment2(7, 1, "SYN", "LWPOLYLINE", new CadPoint3(0, 0, 0), new CadPoint3(0.01, 0, 1))
            ],
            isSourceDefinedClosed: true);

        var validated = new CadContourValidator().Validate(contour, CadGeometryTolerance.Default);

        Assert.Equal(CadContourValidationState.Invalid, validated.ValidationState);
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_ZERO_LENGTH_SEGMENT");
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_NOT_CLOSED");
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_AREA_TOO_SMALL");
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_NON_PLANAR");
    }

    [Fact]
    public void Validator_reports_disconnected_source_segments_without_repairing_them()
    {
        var contour = new CadSegmentContour(
            "contour:0008",
            [
                new CadLineSegment2(8, 0, "SYN", "LWPOLYLINE", new CadPoint3(0, 0, 0), new CadPoint3(1, 0, 0)),
                new CadLineSegment2(8, 1, "SYN", "LWPOLYLINE", new CadPoint3(2, 0, 0), new CadPoint3(0, 0, 0))
            ],
            isSourceDefinedClosed: true);

        var validated = new CadContourValidator().Validate(contour);
        var validatedSegments = Assert.IsType<CadSegmentContour>(validated);

        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_SEGMENTS_DISCONNECTED");
        Assert.False(validated.IsClosed);
        Assert.Equal(new CadPoint3(2, 0, 0), validatedSegments.Segments[1].Start);
    }

    [Theory]
    [MemberData(nameof(SelfIntersectingContours))]
    public void Validator_detects_touches_and_overlaps_outside_the_permitted_shared_endpoint(CadSegmentContour contour)
    {
        var validated = new CadContourValidator().Validate(contour);

        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "CONTOUR_SELF_INTERSECTION");
        Assert.Equal(CadContourValidationState.Invalid, validated.ValidationState);
    }

    public static IEnumerable<object[]> SelfIntersectingContours()
    {
        yield return
        [
            new CadSegmentContour(
                "contour:0010",
                [
                    new CadLineSegment2(10, 0, "SYN", "LWPOLYLINE", new CadPoint3(0, 0, 0), new CadPoint3(2, 0, 0)),
                    new CadLineSegment2(10, 1, "SYN", "LWPOLYLINE", new CadPoint3(2, 0, 0), new CadPoint3(2, 2, 0)),
                    new CadLineSegment2(10, 2, "SYN", "LWPOLYLINE", new CadPoint3(2, 2, 0), new CadPoint3(0, 2, 0)),
                    new CadLineSegment2(10, 3, "SYN", "LWPOLYLINE", new CadPoint3(0, 2, 0), new CadPoint3(1, 0, 0)),
                    new CadLineSegment2(10, 4, "SYN", "LWPOLYLINE", new CadPoint3(1, 0, 0), new CadPoint3(0, 0, 0))
                ],
                isSourceDefinedClosed: true)
        ];

        yield return
        [
            new CadSegmentContour(
                "contour:0011",
                [
                    new CadLineSegment2(11, 0, "SYN", "LWPOLYLINE", new CadPoint3(0, 0, 0), new CadPoint3(2, 0, 0)),
                    new CadLineSegment2(11, 1, "SYN", "LWPOLYLINE", new CadPoint3(2, 0, 0), new CadPoint3(1, 0, 0)),
                    new CadLineSegment2(11, 2, "SYN", "LWPOLYLINE", new CadPoint3(1, 0, 0), new CadPoint3(1, 1, 0)),
                    new CadLineSegment2(11, 3, "SYN", "LWPOLYLINE", new CadPoint3(1, 1, 0), new CadPoint3(0, 0, 0))
                ],
                isSourceDefinedClosed: true)
        ];
    }

    private static CadPolylineVertex Vertex(double x, double y, double bulge = 0) =>
        new(new CadPoint3(x, y, 0), bulge);

    private static NormalizedCadGeometryDocument CreateNormalizedDocument(params CadGeometryEntity[] entities) =>
        new()
        {
            Summary = new CadDocumentModel { Unit = CadUnit.Meters },
            CoordinateContext = new CadCoordinateContext(CadUnit.Meters, 1, new CadPoint3(0, 0, 0)),
            Bounds = CadBounds.Computed(0, 0, 0, 10, 10, 1),
            Entities = entities
        };
}
