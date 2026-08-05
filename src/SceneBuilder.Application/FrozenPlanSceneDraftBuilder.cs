using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record FrozenPlanSceneDraftResult
{
    public SceneDraft? Draft { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed class FrozenPlanSceneDraftBuilder
{
    private readonly CadGeometryRepairApplier _repairApplier = new();
    private readonly CadRuleEngine _ruleEngine = new();
    private readonly SceneDraftApplicationService _sceneDraftService = new();

    public FrozenPlanSceneDraftResult Build(FrozenConversionPlan plan, CadBuildInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        var configuration = plan.BuildConfiguration ?? throw new InvalidDataException("Frozen build configuration is missing.");
        var transformedSnapshot = TransformSnapshot(snapshot, configuration.InputInterpretation);
        var source = CreateSourceDocument(transformedSnapshot);
        var geometry = new NormalizedCadGeometryDocument
        {
            Summary = source,
            CoordinateContext = transformedSnapshot.CoordinateSystem,
            Bounds = transformedSnapshot.Bounds,
            Entities = transformedSnapshot.GeometryObjects.OrderBy(item => item.Geometry.GeometryObjectOrder()).Select(item => item.Geometry).ToArray(),
            Diagnostics = transformedSnapshot.Diagnostics
        };
        var originalContours = new CadContourDocument
        {
            Contours = transformedSnapshot.Contours.Select(item => item.Contour).ToArray(),
            OpenSegments = GetOpenSegments(transformedSnapshot),
            Diagnostics = Array.Empty<CadContourDiagnostic>()
        };
        CadGeometryRepairResult? repair = null;
        if (configuration.Repair.EnabledActions.Count > 0)
        {
            repair = _repairApplier.Apply(originalContours, new CadGeometryRepairPlan
            {
                Id = "frozen-selected-repairs",
                Status = CadGeometryRepairPlanStatus.Ready,
                Actions = transformedSnapshot.RepairCandidates
                    .Where(candidate => configuration.Repair.EnabledActions.Any(item => item.RepairActionId == candidate.RepairActionId))
                    .Select(item => item.Action)
                    .ToArray()
            });
            if (repair.Status is CadGeometryRepairStatus.Failed)
            {
                return new FrozenPlanSceneDraftResult { Diagnostics = repair.Diagnostics };
            }
        }

        var selectedContours = repair?.RepairedDocument ?? originalContours;
        var classification = Classify(configuration, transformedSnapshot, source, geometry, selectedContours);
        if (classification.Status is CadClassificationStatus.Failed)
        {
            return new FrozenPlanSceneDraftResult { Diagnostics = classification.Diagnostics };
        }

        var result = _sceneDraftService.Build(new SceneDraftApplicationRequest
        {
            DraftId = "scene-draft-" + plan.FrozenPlanContentHash,
            SourceDocument = source,
            Geometry = geometry,
            OriginalContours = originalContours,
            RepairResult = repair,
            Classification = classification
        });
        return new FrozenPlanSceneDraftResult { Draft = result.Draft, Diagnostics = result.Diagnostics };
    }

    private CadClassificationResult Classify(FrozenBuildConfiguration configuration, CadBuildInputSnapshot snapshot, CadDocumentModel source, NormalizedCadGeometryDocument geometry, CadContourDocument contours)
    {
        if (configuration.Classification.RuleSet.Rules.Count > 0)
        {
            var classified = _ruleEngine.Classify(new CadClassificationInput { Summary = source, Geometry = geometry, Contours = contours, RuleSet = configuration.Classification.RuleSet });
            return classified with { Objects = classified.Objects.Select(item => ApplyGeometryDefaults(item, configuration.Geometry)).ToArray() };
        }

        var byId = snapshot.ClassificationSubjects.ToDictionary(item => item.ClassificationSubjectId, StringComparer.Ordinal);
        return new CadClassificationResult
        {
            Status = CadClassificationStatus.Succeeded,
            Objects = snapshot.AnalyzeTimeClassifications.Select(item => new CadObjectClassification
            {
                Subject = byId[item.ClassificationSubjectId].Subject,
                Classification = item.Classification,
                MatchedRuleId = item.MatchedRuleId,
                MatchRank = item.MatchedRuleId is null ? 0 : 1,
                Priority = item.Priority,
                CandidateRuleIds = item.CandidateRuleIds
            }).Select(item => ApplyGeometryDefaults(item, configuration.Geometry)).ToArray()
        };
    }

    private static CadObjectClassification ApplyGeometryDefaults(CadObjectClassification item, GeometryAdjustmentPlan geometry) => item.Classification switch
    {
        CadSemanticClassification.Wall => item with { GeometryDefaults = new CadRuleGeometryDefaults { HeightMeters = geometry.WallHeightMeters } },
        CadSemanticClassification.Column => item with { GeometryDefaults = new CadRuleGeometryDefaults { HeightMeters = geometry.ColumnHeightMeters } },
        _ => item
    };

    private static CadDocumentModel CreateSourceDocument(CadBuildInputSnapshot snapshot) => new()
    {
        SourceFormat = CadSourceFormat.Dxf,
        Unit = CadUnit.Meters,
        Bounds = snapshot.Bounds,
        Layers = snapshot.GeometryObjects.GroupBy(item => item.Geometry.LayerName, StringComparer.Ordinal).Select(group => new CadLayerModel { Name = group.Key, EntityCount = group.Count() }).ToArray(),
        EntityTypes = snapshot.GeometryObjects.GroupBy(item => item.Geometry.EntityType, StringComparer.Ordinal).Select(group => new CadEntityTypeSummary(group.Key, group.Count())).ToArray(),
        Diagnostics = snapshot.Diagnostics
    };

    private static IReadOnlyList<CadCurveSegment2> GetOpenSegments(CadBuildInputSnapshot snapshot)
    {
        var contourSegmentIds = snapshot.Contours.SelectMany(item => item.Contour is CadSegmentContour segment ? segment.Segments.Select(value => value.Id) : Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var subjectIds = snapshot.ClassificationSubjects.Where(item => item.Subject.Kind == CadClassificationSubjectKind.OpenSegment).Select(item => item.ClassificationSubjectId).ToHashSet(StringComparer.Ordinal);
        return snapshot.GeometryObjects.Select(item => ToSegment(item.Geometry))
            .Where(item => item is not null && subjectIds.Contains(item.Id) && !contourSegmentIds.Contains(item.Id)).Cast<CadCurveSegment2>().ToArray();
    }

    private static CadCurveSegment2? ToSegment(CadGeometryEntity geometry) => geometry switch
    {
        CadLineGeometry line => new CadLineSegment2(line.SourceOrder, 0, line.LayerName, line.EntityType, line.Start, line.End),
        CadArcGeometry arc => new CadArcSegment2(arc.SourceOrder, 0, arc.LayerName, arc.EntityType, arc.Center, arc.Radius, arc.StartAngleDegrees, arc.EndAngleDegrees, CadCurveDirection.CounterClockwise),
        _ => null
    };

    private static CadBuildInputSnapshot TransformSnapshot(CadBuildInputSnapshot snapshot, FrozenInputInterpretation input)
    {
        var transform = new FrozenInputTransform(input);
        return snapshot with
        {
            Bounds = TransformBounds(snapshot.Bounds, transform),
            GeometryObjects = snapshot.GeometryObjects.Select(item => item with { Geometry = TransformGeometry(item.Geometry, transform) }).ToArray(),
            Contours = snapshot.Contours.Select(item => item with { Contour = TransformContour(item.Contour, transform) }).ToArray(),
            RepairCandidates = snapshot.RepairCandidates.Select(item => item with { Action = TransformAction(item.Action, transform) }).ToArray(),
            ClassificationSubjects = snapshot.ClassificationSubjects.Select(item => item with { Subject = item.Subject with { Bounds = TransformBounds(item.Subject.Bounds, transform) } }).ToArray(),
            AssetCandidates = snapshot.AssetCandidates.Select(item => item with
            {
                Position = transform.Apply(item.Position),
                RotationDegrees = NormalizeAngle(item.RotationDegrees + input.YawDegrees)
            }).ToArray()
        };
    }

    private static CadGeometryEntity TransformGeometry(CadGeometryEntity geometry, FrozenInputTransform transform) => geometry switch
    {
        CadLineGeometry line => new CadLineGeometry(line.SourceOrder, line.LayerName, transform.Apply(line.Start), transform.Apply(line.End), TransformBounds(line.Bounds, transform)),
        CadPolylineGeometry polyline => new CadPolylineGeometry(polyline.SourceOrder, polyline.LayerName, polyline.Vertices.Select(vertex => new CadPolylineVertex(transform.Apply(vertex.Position), vertex.Bulge)).ToArray(), polyline.IsClosed, TransformBounds(polyline.Bounds, transform)),
        CadArcGeometry arc => new CadArcGeometry(arc.SourceOrder, arc.LayerName, transform.Apply(arc.Center), arc.Radius, NormalizeAngle(arc.StartAngleDegrees + transform.YawDegrees), NormalizeAngle(arc.EndAngleDegrees + transform.YawDegrees), TransformBounds(arc.Bounds, transform)),
        CadCircleGeometry circle => new CadCircleGeometry(circle.SourceOrder, circle.LayerName, transform.Apply(circle.Center), circle.Radius, TransformBounds(circle.Bounds, transform)),
        CadInsertGeometry insert => new CadInsertGeometry(insert.SourceOrder, insert.LayerName, insert.BlockName, transform.Apply(insert.Position), NormalizeAngle(insert.RotationDegrees + transform.YawDegrees), insert.Scale, TransformBounds(insert.Bounds, transform)),
        _ => throw new ArgumentOutOfRangeException(nameof(geometry), "Unsupported frozen geometry entity.")
    };

    private static CadContour TransformContour(CadContour contour, FrozenInputTransform transform) => contour switch
    {
        CadSegmentContour segment => segment with
        {
            Segments = segment.Segments.Select(item => TransformSegment(item, transform)).ToArray(),
            Bounds = TransformBounds(segment.Bounds, transform)
        },
        CadCircleContour circle => new CadCircleContour(circle.Id, circle.SourceOrder, circle.SourceLayer, transform.Apply(circle.Center), circle.Radius) with
        {
            IsSourceDefinedClosed = circle.IsSourceDefinedClosed,
            IsClosed = circle.IsClosed,
            SignedAreaSquareMeters = circle.SignedAreaSquareMeters,
            Orientation = circle.Orientation,
            ValidationState = circle.ValidationState,
            Diagnostics = circle.Diagnostics
        },
        _ => throw new ArgumentOutOfRangeException(nameof(contour), "Unsupported frozen contour.")
    };

    private static CadCurveSegment2 TransformSegment(CadCurveSegment2 segment, FrozenInputTransform transform) => segment switch
    {
        CadLineSegment2 line => new CadLineSegment2(line.SourceOrder, line.SegmentOrder, line.SourceLayer, line.SourceEntityType, transform.Apply(line.Start), transform.Apply(line.End)),
        CadArcSegment2 arc => new CadArcSegment2(arc.SourceOrder, arc.SegmentOrder, arc.SourceLayer, arc.SourceEntityType, transform.Apply(arc.Center), arc.Radius, NormalizeAngle(arc.StartAngleDegrees + transform.YawDegrees), NormalizeAngle(arc.EndAngleDegrees + transform.YawDegrees), arc.Direction, transform.Apply(arc.Start), transform.Apply(arc.End)),
        CadGeneratedLineSegment2 generated => new CadGeneratedLineSegment2(generated.RepairActionId, generated.DerivedOrder, generated.SourceLayer, transform.Apply(generated.Start), transform.Apply(generated.End)),
        _ => throw new ArgumentOutOfRangeException(nameof(segment), "Unsupported frozen contour segment.")
    };

    private static CadGeometryRepairAction TransformAction(CadGeometryRepairAction action, FrozenInputTransform transform) => action with
    {
        BeforePoints = action.BeforePoints.Select(transform.Apply).ToArray(),
        AfterPoints = action.AfterPoints.Select(transform.Apply).ToArray()
    };

    private static CadBounds TransformBounds(CadBounds bounds, FrozenInputTransform transform)
    {
        if (bounds.State is not CadBoundsState.Computed)
        {
            return bounds;
        }

        var corners = new[]
        {
            new CadPoint3(bounds.MinX, bounds.MinY, bounds.MinZ), new CadPoint3(bounds.MinX, bounds.MinY, bounds.MaxZ),
            new CadPoint3(bounds.MinX, bounds.MaxY, bounds.MinZ), new CadPoint3(bounds.MinX, bounds.MaxY, bounds.MaxZ),
            new CadPoint3(bounds.MaxX, bounds.MinY, bounds.MinZ), new CadPoint3(bounds.MaxX, bounds.MinY, bounds.MaxZ),
            new CadPoint3(bounds.MaxX, bounds.MaxY, bounds.MinZ), new CadPoint3(bounds.MaxX, bounds.MaxY, bounds.MaxZ)
        }.Select(transform.Apply).ToArray();
        return CadBounds.Computed(corners.Min(point => point.X), corners.Min(point => point.Y), corners.Min(point => point.Z), corners.Max(point => point.X), corners.Max(point => point.Y), corners.Max(point => point.Z));
    }

    private static double NormalizeAngle(double value)
    {
        var normalized = value % 360d;
        return normalized < 0d ? normalized + 360d : normalized;
    }

    private sealed class FrozenInputTransform
    {
        private readonly CadPoint3 _origin;

        public FrozenInputTransform(FrozenInputInterpretation input)
        {
            _origin = input.LocalOriginStrategy == ConversionPlanLocalOriginStrategy.ExplicitOffset ? input.LocalOriginMeters : new CadPoint3(0, 0, 0);
            YawDegrees = input.YawDegrees;
            ZOffsetMeters = input.ZOffsetMeters;
        }

        public double YawDegrees { get; }

        public double ZOffsetMeters { get; }

        public CadPoint3 Apply(CadPoint3 point)
        {
            var x = point.X - _origin.X;
            var y = point.Y - _origin.Y;
            var radians = YawDegrees * Math.PI / 180d;
            return new CadPoint3(
                (x * Math.Cos(radians)) - (y * Math.Sin(radians)),
                (x * Math.Sin(radians)) + (y * Math.Cos(radians)),
                point.Z - _origin.Z + ZOffsetMeters);
        }
    }
}

internal static class CadBuildGeometryExtensions
{
    public static int GeometryObjectOrder(this CadGeometryEntity entity) => entity.SourceOrder;
}
