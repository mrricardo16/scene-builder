namespace SceneBuilder.Cad.Tests;

public sealed class ACadSharpDxfInspectorTests
{
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
}
