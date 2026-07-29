namespace SceneBuilder.Cad.Tests;

public sealed class ACadSharpDxfInspectorTests
{
    [Fact]
    public async Task InspectAsync_PublicSyntheticEmptyDxf_ReturnsSucceededEmptyDocumentWithInformationDiagnostic()
    {
        var result = await InspectFixtureAsync("public-synthetic-empty.dxf");

        Assert.Equal(CadInspectionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadDocumentModel>(result.Document);
        Assert.Empty(document.Layers);
        Assert.Equal(CadBounds.Empty, document.Bounds);
        Assert.Equal(CadBoundsState.Empty, document.Bounds.State);
        Assert.Empty(document.Blocks);
        Assert.Empty(document.EntityTypes);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("DXF_DOCUMENT_EMPTY", diagnostic.Code);
    }

    [Fact]
    public async Task InspectAsync_PublicSyntheticClosedPolylineDxf_ReturnsCorrectBoundsAndEntityCount()
    {
        var result = await InspectFixtureAsync("public-synthetic-closed-polyline.dxf");

        Assert.Equal(CadInspectionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadDocumentModel>(result.Document);
        var outlineLayer = Assert.Single(document.Layers);
        Assert.Equal("OUTLINE", outlineLayer.Name);
        Assert.Equal(1, outlineLayer.EntityCount);
        Assert.Equal(new CadBounds(0, 0, 0, 100, 50, 0), document.Bounds);
        Assert.Equal(CadBoundsState.Computed, document.Bounds.State);
        Assert.Equal(CadBoundsState.Computed, outlineLayer.Bounds.State);
        var entityType = Assert.Single(document.EntityTypes);
        Assert.Equal("LWPOLYLINE", entityType.Type);
        Assert.Equal(1, entityType.EntityCount);
    }

    [Fact]
    public async Task InspectAsync_PublicSyntheticUnitlessDxf_ReturnsUnitlessUnitAndUnknownUnitWarning()
    {
        var result = await InspectFixtureAsync("public-synthetic-unitless-line.dxf");

        Assert.Equal(CadInspectionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadDocumentModel>(result.Document);
        Assert.Equal(CadUnit.Unitless, document.Unit);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("DXF_UNIT_UNKNOWN", diagnostic.Code);
    }

    [Fact]
    public async Task InspectAsync_PublicSyntheticCircleDxf_ReturnsSucceededDocumentWithoutUnsupportedEntityWarning()
    {
        var result = await InspectFixtureAsync("public-synthetic-unmapped-circle.dxf");

        Assert.Equal(CadInspectionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadDocumentModel>(result.Document);
        Assert.Single(document.Layers);
        var entityType = Assert.Single(document.EntityTypes);
        Assert.Equal("CIRCLE", entityType.Type);
        Assert.Equal(1, entityType.EntityCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task InspectAsync_PublicSyntheticWallDxf_ReturnsMappedDocument()
    {
        var inspector = new ACadSharpDxfInspector();
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "synthetic",
            "public-synthetic-wall.dxf");

        var result = await inspector.InspectAsync(
            new CadInspectionRequest
            {
                SourcePath = fixturePath,
                SourceFormat = CadSourceFormat.Dxf
            },
            CancellationToken.None);

        Assert.Equal(CadInspectionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadDocumentModel>(result.Document);
        Assert.Equal(CadSourceFormat.Dxf, document.SourceFormat);

        var wallLayer = Assert.Single(document.Layers.Where(layer => layer.Name == "WALL"));
        Assert.Equal(1, wallLayer.EntityCount);
        Assert.Equal(new CadBounds(0, 0, 0, 100, 50, 0), document.Bounds);
    }

    [Fact]
    public async Task InspectAsync_PublicSyntheticBlockSummaryDxf_ReturnsSortedBlockAndModelspaceEntitySummaries()
    {
        var result = await InspectFixtureAsync("public-synthetic-block-summary.dxf");

        Assert.Equal(CadInspectionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadDocumentModel>(result.Document);
        Assert.Collection(
            document.Blocks,
            block =>
            {
                Assert.Equal("SYN_BLOCK_A", block.Name);
                Assert.Equal(1, block.EntityCount);
                Assert.Equal(CadBoundsState.Computed, block.Bounds.State);
            },
            block =>
            {
                Assert.Equal("SYN_EMPTY_BLOCK", block.Name);
                Assert.Equal(0, block.EntityCount);
                Assert.Equal(CadBoundsState.Empty, block.Bounds.State);
            });
        Assert.Collection(
            document.EntityTypes,
            entityType =>
            {
                Assert.Equal("INSERT", entityType.Type);
                Assert.Equal(1, entityType.EntityCount);
            },
            entityType =>
            {
                Assert.Equal("LINE", entityType.Type);
                Assert.Equal(1, entityType.EntityCount);
            });
        Assert.DoesNotContain(document.EntityTypes, entityType => entityType.Type == "BLOCK_LINE");
    }

    [Fact]
    public async Task InspectAsync_PublicSyntheticBlockSummaryDxf_RepeatedExecutionReturnsStableSummaries()
    {
        var first = await InspectFixtureAsync("public-synthetic-block-summary.dxf");
        var second = await InspectFixtureAsync("public-synthetic-block-summary.dxf");

        var firstDocument = Assert.IsType<CadDocumentModel>(first.Document);
        var secondDocument = Assert.IsType<CadDocumentModel>(second.Document);

        Assert.Equal(firstDocument.Blocks, secondDocument.Blocks);
        Assert.Equal(firstDocument.EntityTypes, secondDocument.EntityTypes);
        Assert.Equal(firstDocument.Bounds.State, secondDocument.Bounds.State);
    }

    [Fact]
    public async Task InspectAsync_MissingSource_ReturnsSourceNotFoundDiagnostic()
    {
        var inspector = new ACadSharpDxfInspector();

        var result = await inspector.InspectAsync(
            new CadInspectionRequest
            {
                SourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.dxf"),
                SourceFormat = CadSourceFormat.Dxf
            },
            CancellationToken.None);

        Assert.Equal(CadInspectionStatus.Failed, result.Status);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "DXF_SOURCE_NOT_FOUND");
    }

    [Fact]
    public async Task InspectAsync_MalformedDxf_ReturnsParseFailedDiagnostic()
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dxf");
        await File.WriteAllTextAsync(temporaryPath, "This is not a DXF document.");

        try
        {
            var inspector = new ACadSharpDxfInspector();

            var result = await inspector.InspectAsync(
                new CadInspectionRequest
                {
                    SourcePath = temporaryPath,
                    SourceFormat = CadSourceFormat.Dxf
                },
                CancellationToken.None);

            Assert.Equal(CadInspectionStatus.Failed, result.Status);
            Assert.Null(result.Document);
            var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "DXF_PARSE_FAILED");
            Assert.Equal("The DXF source could not be parsed.", diagnostic.Message);
            Assert.DoesNotContain(temporaryPath, diagnostic.Message);
            Assert.DoesNotContain("ACadSharp", diagnostic.Message);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    [Fact]
    public async Task InspectAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var inspector = new ACadSharpDxfInspector();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectAsync(
            new CadInspectionRequest
            {
                SourcePath = "unused.dxf",
                SourceFormat = CadSourceFormat.Dxf
            },
            cancellationSource.Token));
    }

    private static Task<CadInspectionResult> InspectFixtureAsync(string fixtureName)
    {
        var inspector = new ACadSharpDxfInspector();
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "synthetic",
            fixtureName);

        return inspector.InspectAsync(
            new CadInspectionRequest
            {
                SourcePath = fixturePath,
                SourceFormat = CadSourceFormat.Dxf
            },
            CancellationToken.None);
    }
}
