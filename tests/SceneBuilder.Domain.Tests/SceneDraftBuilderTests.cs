namespace SceneBuilder.Domain.Tests;

public sealed class SceneDraftBuilderTests
{
    [Fact]
    public void Build_maps_all_supported_classifications_to_semantic_objects_and_nodes()
    {
        var input = CreateCompleteInput();

        var result = new SceneDraftBuilder().Build(input);

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.Succeeded, result.Status);
        Assert.Equal(6, draft.SemanticObjects.Count);
        Assert.Equal(6, draft.Nodes.Count);
        Assert.Contains(draft.SemanticObjects, semanticObject => semanticObject is CadWallObject { GeometryKind: CadWallGeometryKind.ClosedProfile, HeightMeters: 4 });
        Assert.Contains(draft.SemanticObjects, semanticObject => semanticObject is CadFloorObject);
        Assert.Contains(draft.SemanticObjects, semanticObject => semanticObject is CadColumnObject { HeightMeters: 5 });
        Assert.Contains(draft.SemanticObjects, semanticObject => semanticObject is CadRoadObject { GeometryKind: CadRoadGeometryKind.Centerline });
        Assert.Contains(draft.SemanticObjects, semanticObject => semanticObject is CadStaticFacilityObject);
        Assert.Contains(draft.SemanticObjects, semanticObject => semanticObject is CadDynamicEquipmentObject);
        Assert.Equal(
            [
                SceneNodeContentKind.ProceduralStaticGeometry,
                SceneNodeContentKind.ProceduralStaticGeometry,
                SceneNodeContentKind.ProceduralStaticGeometry,
                SceneNodeContentKind.ProceduralStaticGeometry,
                SceneNodeContentKind.StaticAssetReference,
                SceneNodeContentKind.DynamicAssetReference
            ],
            draft.Nodes.Select(node => node.ContentKind).OrderBy(kind => kind));
        Assert.All(draft.Nodes.Where(node => node.ContentKind is SceneNodeContentKind.ProceduralStaticGeometry), node => Assert.Null(node.Transform));
        Assert.All(draft.Nodes.Where(node => node.ContentKind is not SceneNodeContentKind.ProceduralStaticGeometry), node => Assert.NotNull(node.Transform));
        Assert.DoesNotContain(draft.SemanticObjects, semanticObject => semanticObject.Classification is CadSemanticClassification.Unclassified);
    }

    [Fact]
    public void Build_keeps_valid_objects_and_skips_missing_or_incompatible_sources()
    {
        var input = CreateCompleteInput();
        var validFloor = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Floor);
        var missingWall = Classification("contour:999999", CadClassificationSubjectKind.Contour, CadSemanticClassification.Wall);
        var incompatibleFacility = Classification(
            input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Wall).Subject,
            CadSemanticClassification.StaticFacility);

        var result = new SceneDraftBuilder().Build(input with
        {
            Classification = new CadClassificationResult
            {
                Status = CadClassificationStatus.Succeeded,
                Objects = [validFloor, missingWall, incompatibleFacility]
            }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.Single(draft.SemanticObjects);
        Assert.IsType<CadFloorObject>(Assert.Single(draft.SemanticObjects));
        Assert.Equal([input.Contours.Contours[0].Id, "contour:999999"], result.SkippedSubjectIds);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_SOURCE_SUBJECT_NOT_FOUND");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE");
    }

    [Fact]
    public void Build_rejects_failed_classification_and_duplicate_source_identifiers_as_core_input_failures()
    {
        var input = CreateCompleteInput();
        var failed = new SceneDraftBuilder().Build(input with
        {
            Classification = new CadClassificationResult { Status = CadClassificationStatus.Failed }
        });
        var duplicate = new SceneDraftBuilder().Build(input with
        {
            Contours = input.Contours with { Contours = [input.Contours.Contours[0], input.Contours.Contours[0]] }
        });

        Assert.Equal(SceneDraftBuildStatus.Failed, failed.Status);
        Assert.Null(failed.Draft);
        Assert.Equal(SceneDraftBuildStatus.Failed, duplicate.Status);
        Assert.Null(duplicate.Draft);
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Code is "SCENE_DRAFT_INPUT_INVALID");
    }

    [Fact]
    public void Build_rejects_forged_subject_metadata_without_creating_a_node()
    {
        var input = CreateCompleteInput();
        var floor = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Floor);
        var forged = floor with { Subject = floor.Subject with { Kind = CadClassificationSubjectKind.Insert } };

        var result = new SceneDraftBuilder().Build(input with
        {
            Classification = input.Classification with { Objects = [forged] }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.Empty(draft.SemanticObjects);
        Assert.Empty(draft.Nodes);
        Assert.Equal([floor.Subject.Id], result.SkippedSubjectIds);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_CLASSIFICATION_SOURCE_MISMATCH");
    }

    [Fact]
    public void Build_skips_duplicate_classification_subject_results_without_choosing_one()
    {
        var input = CreateCompleteInput();
        var floor = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Floor);

        var result = new SceneDraftBuilder().Build(input with
        {
            Classification = input.Classification with { Objects = [floor, floor] }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.Empty(draft.SemanticObjects);
        Assert.Equal([floor.Subject.Id], result.SkippedSubjectIds);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_DUPLICATE_SUBJECT_RESULT");
    }

    [Fact]
    public void Build_preserves_missing_wall_height_without_guessing_a_default()
    {
        var input = CreateCompleteInput();
        var classification = input.Classification.Objects
            .Select(item => item.Classification is CadSemanticClassification.Wall
                ? item with { GeometryDefaults = null }
                : item)
            .ToArray();

        var result = new SceneDraftBuilder().Build(input with
        {
            Classification = input.Classification with { Objects = classification }
        });

        var wall = Assert.IsType<CadWallObject>(Assert.Single(Assert.IsType<SceneDraft>(result.Draft).SemanticObjects.Where(item => item is CadWallObject)));
        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.Null(wall.HeightMeters);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_GEOMETRY_DEFAULT_MISSING");
    }

    [Fact]
    public void Build_rejects_nonfinite_geometry_defaults_from_a_forged_classification_result()
    {
        var input = CreateCompleteInput();
        var wall = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Wall);

        var result = new SceneDraftBuilder().Build(input with
        {
            Classification = input.Classification with
            {
                Objects = [wall with { GeometryDefaults = new CadRuleGeometryDefaults { HeightMeters = double.PositiveInfinity } }]
            }
        });

        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.Empty(Assert.IsType<SceneDraft>(result.Draft).SemanticObjects);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_CLASSIFICATION_SOURCE_MISMATCH");
    }

    [Fact]
    public void Build_skips_an_invalid_open_segment_without_losing_a_valid_classified_object()
    {
        var input = CreateCompleteInput();
        var floor = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Floor);
        var zeroLengthSegment = Line(90, 0, "SYN_ROAD", 1, 1, 1, 1);
        var zeroLengthSubject = Assert.Single(new CadClassificationSubjectBuilder().Build(new CadClassificationInput
        {
            Contours = new CadContourDocument { OpenSegments = [zeroLengthSegment] },
            Geometry = new NormalizedCadGeometryDocument()
        }));

        var result = new SceneDraftBuilder().Build(input with
        {
            Contours = input.Contours with { OpenSegments = [zeroLengthSegment] },
            Classification = input.Classification with
            {
                Objects = [floor, Classification(zeroLengthSubject, CadSemanticClassification.Road)]
            }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.IsType<CadFloorObject>(Assert.Single(draft.SemanticObjects));
        Assert.Equal([zeroLengthSegment.Id], result.SkippedSubjectIds);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE");
    }

    [Theory]
    [InlineData(5, CadSemanticClassification.StaticFacility)]
    [InlineData(6, CadSemanticClassification.DynamicEquipment)]
    public void Build_skips_insert_references_without_computed_bounds(int sourceOrder, CadSemanticClassification semanticClassification)
    {
        var input = CreateCompleteInput();
        var originalInsert = Assert.IsType<CadInsertGeometry>(input.Geometry.Entities.Single(entity => entity.SourceOrder == sourceOrder));
        var invalidInsert = new CadInsertGeometry(
            originalInsert.SourceOrder,
            originalInsert.LayerName,
            originalInsert.BlockName,
            originalInsert.Position,
            originalInsert.RotationDegrees,
            originalInsert.Scale);
        var geometry = input.Geometry with
        {
            Entities = input.Geometry.Entities.Select(entity => entity.SourceOrder == sourceOrder ? invalidInsert : entity).ToArray()
        };
        var subject = new CadClassificationSubjectBuilder().Build(new CadClassificationInput { Geometry = geometry, Contours = input.Contours })
            .Single(candidate => candidate.Id == CadClassificationSubjectIdentity.ForInsert(sourceOrder));
        var floor = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Floor);

        var result = new SceneDraftBuilder().Build(input with
        {
            Geometry = geometry,
            Classification = input.Classification with { Objects = [floor, Classification(subject, semanticClassification)] }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.PartiallySucceeded, result.Status);
        Assert.IsType<CadFloorObject>(Assert.Single(draft.SemanticObjects));
        Assert.Equal([subject.Id], result.SkippedSubjectIds);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE");
    }

    [Fact]
    public void Semantic_object_constructors_enforce_source_geometry_and_height_invariants()
    {
        var contour = Rectangle("contour:000050", 50, "SYN");
        var segment = Line(51, 0, "SYN", 0, 0, 1, 0);

        Assert.Throws<ArgumentException>(() => new CadWallObject("semantic:wall:bad", "subject", CadClassificationSubjectKind.Contour, Bounds(), null, contour, segment, 4));
        Assert.Throws<ArgumentException>(() => new CadWallObject("semantic:wall:bad", "subject", CadClassificationSubjectKind.Contour, Bounds(), null, null, null, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadWallObject("semantic:wall:bad", "subject", CadClassificationSubjectKind.Contour, Bounds(), null, contour, null, double.NaN));
        Assert.Throws<ArgumentException>(() => new CadFloorObject("semantic:floor:bad", "subject", CadClassificationSubjectKind.Contour, Bounds(), null, new CadSegmentContour("contour:bad", [], true)));
        Assert.Throws<ArgumentException>(() => new CadRoadObject("semantic:road:bad", "subject", CadClassificationSubjectKind.OpenSegment, Bounds(), null, contour, segment));
        Assert.Throws<ArgumentException>(() => new CadStaticFacilityObject("semantic:static:bad", "subject", Bounds(), null, "", new CadPoint3(0, 0, 0), 0, CadScale3.Identity));
    }

    [Fact]
    public void Build_is_deterministic_for_shuffled_inputs_and_leaves_all_inputs_unchanged()
    {
        var input = CreateCompleteInput();
        var originalClassification = input.Classification;
        var originalContours = input.Contours;
        var originalGeometry = input.Geometry;
        var shuffled = input with
        {
            Geometry = input.Geometry with { Entities = input.Geometry.Entities.Reverse().ToArray() },
            Contours = input.Contours with
            {
                Contours = input.Contours.Contours.Reverse().ToArray(),
                OpenSegments = input.Contours.OpenSegments.Reverse().ToArray()
            },
            Classification = input.Classification with { Objects = input.Classification.Objects.Reverse().ToArray() }
        };

        var first = new SceneDraftBuilder().Build(input);
        var second = new SceneDraftBuilder().Build(shuffled);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.SkippedSubjectIds, second.SkippedSubjectIds);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(Assert.IsType<SceneDraft>(first.Draft).SemanticObjects, Assert.IsType<SceneDraft>(second.Draft).SemanticObjects);
        Assert.Equal(Assert.IsType<SceneDraft>(first.Draft).Nodes, Assert.IsType<SceneDraft>(second.Draft).Nodes);
        Assert.Equal(originalClassification, input.Classification);
        Assert.Equal(originalContours, input.Contours);
        Assert.Equal(originalGeometry, input.Geometry);
        Assert.Equal(
            Assert.IsType<SceneDraft>(first.Draft).SemanticObjects.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal),
            Assert.IsType<SceneDraft>(first.Draft).SemanticObjects.Select(item => item.Id));
        Assert.Equal(
            Assert.IsType<SceneDraft>(first.Draft).Nodes.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal),
            Assert.IsType<SceneDraft>(first.Draft).Nodes.Select(item => item.Id));
    }

    [Fact]
    public void Build_handles_a_thousand_classifications_with_stable_output()
    {
        var input = CreateCompleteInput();
        var road = input.Classification.Objects.Single(item => item.Classification is CadSemanticClassification.Road);
        var segments = Enumerable.Range(0, 1000)
            .Select(index => Line(index, 0, "SYN_ROAD", index, 0, index + 1, 0))
            .Reverse()
            .ToArray();
        var subjects = segments
            .Select(segment => road with
            {
                Subject = road.Subject with { Id = segment.Id, Bounds = segment.Bounds }
            })
            .ToArray();

        var result = new SceneDraftBuilder().Build(input with
        {
            Contours = new CadContourDocument { OpenSegments = segments },
            Geometry = new NormalizedCadGeometryDocument(),
            Classification = new CadClassificationResult { Status = CadClassificationStatus.Succeeded, Objects = subjects.Reverse().ToArray() }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.Succeeded, result.Status);
        Assert.Equal(1000, draft.SemanticObjects.Count);
        Assert.Equal(draft.SemanticObjects.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal), draft.SemanticObjects.Select(item => item.Id));
    }

    private static SceneDraftBuildRequest CreateCompleteInput()
    {
        var wall = Rectangle("contour:000001", 1, "SYN_WALL");
        var floor = Rectangle("contour:000002", 2, "SYN_FLOOR");
        var column = new CadContourValidator().Validate(new CadCircleContour("contour:000003", 3, "SYN_COLUMN", new CadPoint3(5, 5, 0), 0.5));
        var road = Line(4, 0, "SYN_ROAD", 0, 10, 10, 10);
        var staticInsert = new CadInsertGeometry(5, "SYN_STATIC", "SYN_STATIC_BLOCK", new CadPoint3(20, 20, 0), 30, new CadScale3(2, 2, 1), Bounds());
        var dynamicInsert = new CadInsertGeometry(6, "SYN_DYNAMIC", "SYN_DYNAMIC_BLOCK", new CadPoint3(30, 30, 0), 45, CadScale3.Identity, Bounds());
        var contours = new CadContourDocument { Contours = [wall, floor, column], OpenSegments = [road] };
        var geometry = new NormalizedCadGeometryDocument { Entities = [staticInsert, dynamicInsert] };
        var subjects = new CadClassificationSubjectBuilder().Build(new CadClassificationInput { Contours = contours, Geometry = geometry });

        return new SceneDraftBuildRequest
        {
            DraftId = "draft:synthetic:001",
            SourceDocument = new CadDocumentModel { SourceFormat = CadSourceFormat.Dxf, Unit = CadUnit.Meters, Bounds = Bounds() },
            Geometry = geometry,
            Contours = contours,
            Classification = new CadClassificationResult
            {
                Status = CadClassificationStatus.Succeeded,
                Objects =
                [
                    Classification(subjects.Single(subject => subject.Id == wall.Id), CadSemanticClassification.Wall, 4),
                    Classification(subjects.Single(subject => subject.Id == floor.Id), CadSemanticClassification.Floor),
                    Classification(subjects.Single(subject => subject.Id == column.Id), CadSemanticClassification.Column, 5),
                    Classification(subjects.Single(subject => subject.Id == road.Id), CadSemanticClassification.Road),
                    Classification(subjects.Single(subject => subject.Id == "insert:000005"), CadSemanticClassification.StaticFacility),
                    Classification(subjects.Single(subject => subject.Id == "insert:000006"), CadSemanticClassification.DynamicEquipment),
                    new CadObjectClassification { Subject = new CadClassificationSubject { Id = "segment:999999:000000", Kind = CadClassificationSubjectKind.OpenSegment }, Classification = CadSemanticClassification.Unclassified }
                ]
            }
        };
    }

    private static CadObjectClassification Classification(CadClassificationSubject subject, CadSemanticClassification classification, double? heightMeters = null) =>
        new()
        {
            Subject = subject,
            Classification = classification,
            MatchedRuleId = "synthetic-rule",
            MatchRank = 390,
            Priority = 1,
            GeometryDefaults = heightMeters is null ? null : new CadRuleGeometryDefaults { HeightMeters = heightMeters }
        };

    private static CadObjectClassification Classification(string subjectId, CadClassificationSubjectKind kind, CadSemanticClassification classification) =>
        Classification(new CadClassificationSubject { Id = subjectId, Kind = kind }, classification);

    private static CadSegmentContour Rectangle(string id, int sourceOrder, string layer) =>
        Assert.IsType<CadSegmentContour>(new CadContourValidator().Validate(new CadSegmentContour(
            id,
            [
                Line(sourceOrder, 0, layer, 0, 0, 2, 0),
                Line(sourceOrder, 1, layer, 2, 0, 2, 2),
                Line(sourceOrder, 2, layer, 2, 2, 0, 2),
                Line(sourceOrder, 3, layer, 0, 2, 0, 0)
            ],
            isSourceDefinedClosed: true)));

    private static CadLineSegment2 Line(int sourceOrder, int segmentOrder, string layer, double startX, double startY, double endX, double endY) =>
        new(sourceOrder, segmentOrder, layer, "LWPOLYLINE", new CadPoint3(startX, startY, 0), new CadPoint3(endX, endY, 0));

    private static CadBounds Bounds() => CadBounds.Computed(0, 0, 0, 100, 100, 10);
}
