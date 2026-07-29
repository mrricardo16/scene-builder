namespace SceneBuilder.Domain.Tests;

public sealed class CadGeometryContractTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Point_rejects_non_finite_coordinates(double invalidCoordinate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CadPoint3(invalidCoordinate, 0, 0));
    }

    [Fact]
    public void Line_preserves_coordinates_and_computes_matching_bounds()
    {
        var line = new CadLineGeometry(
            sourceOrder: 4,
            layerName: "SYN_GEOMETRY",
            start: new CadPoint3(10, 20, 30),
            end: new CadPoint3(40, 50, 60));

        Assert.Equal("LINE", line.EntityType);
        Assert.Equal(4, line.SourceOrder);
        Assert.Equal(new CadPoint3(10, 20, 30), line.Start);
        Assert.Equal(new CadPoint3(40, 50, 60), line.End);
        Assert.Equal(new CadBounds(10, 20, 30, 40, 50, 60), line.Bounds);
    }

    [Fact]
    public void Polyline_preserves_vertex_order_bulge_elevation_and_closed_flag_without_repair()
    {
        var polyline = new CadPolylineGeometry(
            sourceOrder: 1,
            layerName: "SYN_OUTLINE",
            vertices:
            [
                new CadPolylineVertex(new CadPoint3(10, 20, 3), 0.5),
                new CadPolylineVertex(new CadPoint3(40, 50, 3), 0)
            ],
            isClosed: true,
            bounds: new CadBounds(10, 20, 3, 40, 50, 3));

        Assert.True(polyline.IsClosed);
        Assert.Collection(
            polyline.Vertices,
            vertex => Assert.Equal(new CadPoint3(10, 20, 3), vertex.Position),
            vertex => Assert.Equal(new CadPoint3(40, 50, 3), vertex.Position));
        Assert.Equal(0.5, polyline.Vertices[0].Bulge);
        Assert.Equal(2, polyline.Vertices.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Arc_and_circle_reject_non_positive_or_non_finite_radii(double invalidRadius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CadArcGeometry(0, "0", new CadPoint3(0, 0, 0), invalidRadius, 0, 90, CadBounds.NotEvaluated));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CadCircleGeometry(0, "0", new CadPoint3(0, 0, 0), invalidRadius, CadBounds.NotEvaluated));
    }

    [Fact]
    public void Insert_requires_block_name_and_preserves_transform()
    {
        Assert.Throws<ArgumentException>(() =>
            new CadInsertGeometry(0, "0", " ", new CadPoint3(0, 0, 0), 0, CadScale3.Identity, CadBounds.NotEvaluated));

        var insert = new CadInsertGeometry(
            2,
            "SYN_INSERT",
            "SYN_BLOCK_A",
            new CadPoint3(1, 2, 3),
            45,
            new CadScale3(2, 3, 4),
            CadBounds.NotEvaluated);

        Assert.Equal("INSERT", insert.EntityType);
        Assert.Equal("SYN_BLOCK_A", insert.BlockName);
        Assert.Equal(new CadScale3(2, 3, 4), insert.Scale);
        Assert.Equal(45, insert.RotationDegrees);
    }

    [Fact]
    public void Normalize_converts_millimeters_to_local_meters_without_mutating_source_geometry()
    {
        var sourceLine = new CadLineGeometry(
            0,
            "SYN_GEOMETRY",
            new CadPoint3(101000, 202000, 3000),
            new CadPoint3(102000, 203000, 4000));
        var source = new CadGeometryDocument
        {
            Summary = new CadDocumentModel
            {
                Unit = CadUnit.Millimeters,
                Bounds = new CadBounds(100000, 200000, 0, 102000, 203000, 4000)
            },
            ModelSpaceEntities = [sourceLine]
        };

        var result = new CadGeometryNormalizer().Normalize(source);

        Assert.Equal(CadGeometryNormalizationStatus.Succeeded, result.Status);
        var normalized = Assert.IsType<NormalizedCadGeometryDocument>(result.Document);
        var context = Assert.IsType<CadCoordinateContext>(normalized.CoordinateContext);
        Assert.Equal(0.001, context.UnitScaleToMeters);
        Assert.Equal(new CadPoint3(100000, 200000, 0), context.SourceOrigin);
        var normalizedLine = Assert.IsType<CadLineGeometry>(Assert.Single(normalized.Entities));
        Assert.Equal(new CadPoint3(1, 2, 3), normalizedLine.Start);
        Assert.Equal(new CadPoint3(2, 3, 4), normalizedLine.End);
        Assert.Equal(new CadBounds(0, 0, 0, 2, 3, 4), normalized.Bounds);
        Assert.Equal(new CadPoint3(101000, 202000, 3000), sourceLine.Start);
    }

    [Fact]
    public void Normalize_rejects_unknown_units_and_uncomputed_bounds_without_fabricating_an_origin()
    {
        var geometry = new CadLineGeometry(0, "0", new CadPoint3(1, 2, 3), new CadPoint3(4, 5, 6));
        var unknownUnitResult = new CadGeometryNormalizer().Normalize(new CadGeometryDocument
        {
            Summary = new CadDocumentModel { Unit = CadUnit.Unknown, Bounds = geometry.Bounds },
            ModelSpaceEntities = [geometry]
        });
        var unknownUnitDiagnostic = Assert.Single(unknownUnitResult.Diagnostics);
        Assert.Equal(CadGeometryNormalizationStatus.Failed, unknownUnitResult.Status);
        Assert.Equal("GEOMETRY_UNIT_UNRESOLVED", unknownUnitDiagnostic.Code);

        var uncomputedBoundsResult = new CadGeometryNormalizer().Normalize(new CadGeometryDocument
        {
            Summary = new CadDocumentModel { Unit = CadUnit.Meters, Bounds = CadBounds.NotEvaluated },
            ModelSpaceEntities = [geometry]
        });
        var boundsDiagnostic = Assert.Single(uncomputedBoundsResult.Diagnostics);
        Assert.Equal(CadGeometryNormalizationStatus.Failed, uncomputedBoundsResult.Status);
        Assert.Equal("GEOMETRY_BOUNDS_NOT_COMPUTED", boundsDiagnostic.Code);
    }

    [Theory]
    [InlineData(CadUnit.Millimeters, 1000, 1)]
    [InlineData(CadUnit.Centimeters, 100, 1)]
    [InlineData(CadUnit.Meters, 1, 1)]
    [InlineData(CadUnit.Inches, 1, 0.0254)]
    [InlineData(CadUnit.Feet, 1, 0.3048)]
    public void Normalize_uses_the_configured_unit_scale(
        CadUnit unit,
        double sourceDistance,
        double expectedMeters)
    {
        var line = new CadLineGeometry(0, "0", new CadPoint3(0, 0, 0), new CadPoint3(sourceDistance, 0, 0));
        var result = new CadGeometryNormalizer().Normalize(new CadGeometryDocument
        {
            Summary = new CadDocumentModel
            {
                Unit = unit,
                Bounds = new CadBounds(0, 0, 0, sourceDistance, 0, 0)
            },
            ModelSpaceEntities = [line]
        });

        var normalized = Assert.IsType<NormalizedCadGeometryDocument>(result.Document);
        var normalizedLine = Assert.IsType<CadLineGeometry>(Assert.Single(normalized.Entities));
        Assert.Equal(expectedMeters, normalizedLine.End.X, precision: 10);
    }

    [Fact]
    public void Normalize_empty_document_succeeds_without_an_origin()
    {
        var result = new CadGeometryNormalizer().Normalize(new CadGeometryDocument
        {
            Summary = new CadDocumentModel { Unit = CadUnit.Unitless, Bounds = CadBounds.Empty }
        });

        Assert.Equal(CadGeometryNormalizationStatus.Succeeded, result.Status);
        var normalized = Assert.IsType<NormalizedCadGeometryDocument>(result.Document);
        Assert.Null(normalized.CoordinateContext);
        Assert.Empty(normalized.Entities);
        Assert.Equal(CadBounds.Empty, normalized.Bounds);
    }
}
