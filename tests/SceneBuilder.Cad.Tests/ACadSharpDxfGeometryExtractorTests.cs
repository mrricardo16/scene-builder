namespace SceneBuilder.Cad.Tests;

public sealed class ACadSharpDxfGeometryExtractorTests
{
    [Fact]
    public async Task ExtractAsync_PublicSyntheticBasicGeometryDxf_MapsSupportedModelspaceEntitiesInSourceOrder()
    {
        var result = await ExtractFixtureAsync("public-synthetic-basic-geometry.dxf");

        Assert.Equal(CadGeometryExtractionStatus.Succeeded, result.Status);
        var document = Assert.IsType<CadGeometryDocument>(result.Document);
        Assert.Collection(
            document.ModelSpaceEntities,
            entity => Assert.IsType<CadLineGeometry>(entity),
            entity => Assert.IsType<CadPolylineGeometry>(entity),
            entity => Assert.IsType<CadArcGeometry>(entity),
            entity => Assert.IsType<CadCircleGeometry>(entity),
            entity => Assert.IsType<CadInsertGeometry>(entity));
        Assert.Equal([0, 1, 2, 3, 4], document.ModelSpaceEntities.Select(entity => entity.SourceOrder));

        var line = Assert.IsType<CadLineGeometry>(document.ModelSpaceEntities[0]);
        Assert.Equal(new CadPoint3(101000, 202000, 3000), line.Start);
        Assert.Equal(new CadPoint3(102000, 203000, 4000), line.End);

        var polyline = Assert.IsType<CadPolylineGeometry>(document.ModelSpaceEntities[1]);
        Assert.True(polyline.IsClosed);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge);
        Assert.Equal(3000, polyline.Vertices[0].Position.Z);

        var arc = Assert.IsType<CadArcGeometry>(document.ModelSpaceEntities[2]);
        Assert.Equal(new CadPoint3(103000, 204000, 3000), arc.Center);
        Assert.Equal(500, arc.Radius);
        Assert.Equal(0, arc.StartAngleDegrees, precision: 10);
        Assert.Equal(90, arc.EndAngleDegrees, precision: 10);

        var circle = Assert.IsType<CadCircleGeometry>(document.ModelSpaceEntities[3]);
        Assert.Equal(new CadPoint3(104000, 205000, 3000), circle.Center);
        Assert.Equal(250, circle.Radius);

        var insert = Assert.IsType<CadInsertGeometry>(document.ModelSpaceEntities[4]);
        Assert.Equal("SYN_BLOCK_A", insert.BlockName);
        Assert.Equal(new CadPoint3(105000, 205000, 3000), insert.Position);
        Assert.Equal(30, insert.RotationDegrees, precision: 10);
        Assert.Equal(new CadScale3(2, 3, 4), insert.Scale);
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedEntity_ReturnsPartialResultAndPreservesMappedEntities()
    {
        var result = await ExtractFixtureAsync("public-synthetic-unsupported-geometry.dxf");

        Assert.Equal(CadGeometryExtractionStatus.PartiallySucceeded, result.Status);
        var document = Assert.IsType<CadGeometryDocument>(result.Document);
        Assert.IsType<CadLineGeometry>(Assert.Single(document.ModelSpaceEntities));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "DXF_ENTITY_UNSUPPORTED");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "DXF_PARSE_FAILED");
    }

    [Fact]
    public async Task ExtractAsync_RepeatedExecution_ReturnsStableGeometryAndSummary()
    {
        var first = await ExtractFixtureAsync("public-synthetic-basic-geometry.dxf");
        var second = await ExtractFixtureAsync("public-synthetic-basic-geometry.dxf");

        var firstDocument = Assert.IsType<CadGeometryDocument>(first.Document);
        var secondDocument = Assert.IsType<CadGeometryDocument>(second.Document);
        Assert.Equal(firstDocument.Summary.Bounds, secondDocument.Summary.Bounds);
        Assert.Equal(firstDocument.Summary.Layers, secondDocument.Summary.Layers);
        Assert.Equal(firstDocument.Summary.Blocks, secondDocument.Summary.Blocks);
        Assert.Equal(firstDocument.Summary.EntityTypes, secondDocument.Summary.EntityTypes);
        Assert.Equal(
            firstDocument.ModelSpaceEntities.Select(entity => new
            {
                entity.SourceOrder,
                entity.LayerName,
                entity.EntityType,
                entity.Bounds
            }),
            secondDocument.ModelSpaceEntities.Select(entity => new
            {
                entity.SourceOrder,
                entity.LayerName,
                entity.EntityType,
                entity.Bounds
            }));
        var firstPolyline = Assert.IsType<CadPolylineGeometry>(firstDocument.ModelSpaceEntities[1]);
        var secondPolyline = Assert.IsType<CadPolylineGeometry>(secondDocument.ModelSpaceEntities[1]);
        Assert.Equal(firstPolyline.Vertices, secondPolyline.Vertices);
    }

    [Fact]
    public async Task ExtractAsync_PublicSyntheticContoursDxf_NormalizesAndBuildsSourceDefinedContours()
    {
        var extraction = await ExtractFixtureAsync("public-synthetic-contours.dxf");
        var geometry = Assert.IsType<CadGeometryDocument>(extraction.Document);

        var normalization = new CadGeometryNormalizer().Normalize(geometry);
        Assert.Equal(CadGeometryExtractionStatus.Succeeded, extraction.Status);
        Assert.Equal(CadGeometryNormalizationStatus.Succeeded, normalization.Status);
        var contours = new CadContourBuilder().Build(Assert.IsType<NormalizedCadGeometryDocument>(normalization.Document));

        Assert.Equal(CadContourBuildStatus.Succeeded, contours.Status);
        Assert.Collection(
            contours.Document!.Contours,
            contour =>
            {
                var polygon = Assert.IsType<CadSegmentContour>(contour);
                Assert.Equal(CadContourValidationState.Valid, polygon.ValidationState);
                Assert.Equal(0.005, polygon.SignedAreaSquareMeters, precision: 10);
            },
            contour => Assert.IsType<CadCircleContour>(contour));
        Assert.Single(contours.Document.OpenSegments);
    }

    [Fact]
    public async Task ExtractAsync_PublicSyntheticRepairableSegmentsDxf_RepairsSmallGapsIntoValidatedContour()
    {
        var extraction = await ExtractFixtureAsync("public-synthetic-repairable-segments.dxf");
        var geometry = Assert.IsType<CadGeometryDocument>(extraction.Document);
        var normalization = new CadGeometryNormalizer().Normalize(geometry);
        var contours = new CadContourBuilder().Build(Assert.IsType<NormalizedCadGeometryDocument>(normalization.Document));
        var policy = new CadGeometryRepairPolicy(endpointSnapToleranceMeters: 0.002, maximumBridgeGapMeters: 0.02);
        var plan = new CadGeometryRepairAnalyzer().Analyze(Assert.IsType<CadContourDocument>(contours.Document), policy);

        var repair = new CadGeometryRepairApplier().Apply(contours.Document!, plan, policy);

        Assert.Equal(CadGeometryExtractionStatus.Succeeded, extraction.Status);
        Assert.Equal(CadGeometryNormalizationStatus.Succeeded, normalization.Status);
        Assert.Equal(CadGeometryRepairPlanStatus.Ready, plan.Status);
        Assert.Equal(CadGeometryRepairStatus.Succeeded, repair.Status);
        Assert.Equal(CadContourValidationState.Valid, Assert.Single(repair.RepairedDocument!.Contours).ValidationState);
    }

    [Fact]
    public async Task ExtractAsync_PublicSyntheticRepairConflictsDxf_ReportsBranchConflictWithoutApplyingRepair()
    {
        var extraction = await ExtractFixtureAsync("public-synthetic-repair-conflicts.dxf");
        var geometry = Assert.IsType<CadGeometryDocument>(extraction.Document);
        var normalization = new CadGeometryNormalizer().Normalize(geometry);
        var contours = new CadContourBuilder().Build(Assert.IsType<NormalizedCadGeometryDocument>(normalization.Document));
        var plan = new CadGeometryRepairAnalyzer().Analyze(contours.Document!);

        var repair = new CadGeometryRepairApplier().Apply(contours.Document!, plan);

        Assert.Equal(CadGeometryRepairPlanStatus.HasConflicts, plan.Status);
        Assert.Equal(CadGeometryRepairStatus.PartiallySucceeded, repair.Status);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "REPAIR_CHAIN_BRANCHING_CONFLICT");
        Assert.Equal(3, repair.OriginalDocument!.OpenSegments.Count);
    }

    [Fact]
    public async Task ExtractAsync_PublicSyntheticBasicGeometryDxf_ClassifiesContourOpenSegmentAndInsertWithoutMutatingGeometry()
    {
        var extraction = await ExtractFixtureAsync("public-synthetic-basic-geometry.dxf");
        var geometry = Assert.IsType<CadGeometryDocument>(extraction.Document);
        var normalization = new CadGeometryNormalizer().Normalize(geometry);
        var normalized = Assert.IsType<NormalizedCadGeometryDocument>(normalization.Document);
        var contours = new CadContourBuilder().Build(normalized);
        var ruleSet = new CadRuleSet
        {
            ContractVersion = "1.0",
            Rules =
            [
                new CadClassificationRule
                {
                    Id = "synthetic-insert",
                    Enabled = true,
                    Classification = CadSemanticClassification.StaticFacility,
                    Match = new CadRuleMatch { Block = "SYN_BLOCK_A", EntityTypes = ["INSERT"] }
                },
                new CadClassificationRule
                {
                    Id = "synthetic-line",
                    Enabled = true,
                    Classification = CadSemanticClassification.Road,
                    Match = new CadRuleMatch { EntityTypes = ["LINE"] }
                }
            ]
        };

        var result = new CadRuleEngine().Classify(new CadClassificationInput
        {
            Summary = geometry.Summary,
            Geometry = normalized,
            Contours = Assert.IsType<CadContourDocument>(contours.Document),
            RuleSet = ruleSet
        });

        Assert.Equal(CadClassificationStatus.Succeeded, result.Status);
        Assert.Contains(result.Objects, item => item.Classification == CadSemanticClassification.StaticFacility && item.Subject.Kind == CadClassificationSubjectKind.Insert);
        Assert.Contains(result.Objects, item => item.Classification == CadSemanticClassification.Road && item.Subject.Kind == CadClassificationSubjectKind.OpenSegment);
        Assert.Contains(result.Objects, item => item.Subject.Kind == CadClassificationSubjectKind.Contour && item.Classification == CadSemanticClassification.Unclassified && !item.Subject.IsEligibleForClassification);
        Assert.Same(normalized, Assert.IsType<NormalizedCadGeometryDocument>(normalization.Document));
    }

    [Fact]
    public async Task ExtractAsync_PublicSyntheticContoursDxf_ClassifiesAValidContourAsWall()
    {
        var extraction = await ExtractFixtureAsync("public-synthetic-contours.dxf");
        var geometry = Assert.IsType<CadGeometryDocument>(extraction.Document);
        var normalization = new CadGeometryNormalizer().Normalize(geometry);
        var normalized = Assert.IsType<NormalizedCadGeometryDocument>(normalization.Document);
        var contours = new CadContourBuilder().Build(normalized);

        var result = new CadRuleEngine().Classify(new CadClassificationInput
        {
            Summary = geometry.Summary,
            Geometry = normalized,
            Contours = Assert.IsType<CadContourDocument>(contours.Document),
            RuleSet = new CadRuleSet
            {
                ContractVersion = "1.0",
                Rules = [new CadClassificationRule
                {
                    Id = "synthetic-wall-contour",
                    Enabled = true,
                    Classification = CadSemanticClassification.Wall,
                    Match = new CadRuleMatch { Layer = "SYN_CONTOUR", EntityTypes = ["LWPOLYLINE"] }
                }]
            }
        });

        Assert.Contains(result.Objects, item => item.Subject.Kind == CadClassificationSubjectKind.Contour && item.Classification == CadSemanticClassification.Wall);
    }

    private static Task<CadGeometryExtractionResult> ExtractFixtureAsync(string fixtureName)
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "synthetic",
            fixtureName);

        return new ACadSharpDxfGeometryExtractor().ExtractAsync(
            new CadInspectionRequest
            {
                SourcePath = fixturePath,
                SourceFormat = CadSourceFormat.Dxf
            },
            CancellationToken.None);
    }
}
