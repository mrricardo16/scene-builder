using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.Tables;
using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

internal static class ACadSharpDxfDocumentMapper
{
    internal static CadDocumentModel MapSummary(CadDocument sourceDocument, string sourcePath)
    {
        var snapshots = CreateSnapshots(sourceDocument);
        return MapSummary(sourceDocument, sourcePath, snapshots);
    }

    internal static CadGeometryExtractionResult MapGeometry(CadDocument sourceDocument, string sourcePath)
    {
        var snapshots = CreateSnapshots(sourceDocument);
        var summary = MapSummary(sourceDocument, sourcePath, snapshots);
        var diagnostics = new List<SceneDiagnostic>(summary.Diagnostics);
        var geometry = new List<CadGeometryEntity>();
        var partiallySucceeded = false;

        foreach (var snapshot in snapshots)
        {
            if (!IsSupported(snapshot.Entity))
            {
                partiallySucceeded = true;
                continue;
            }

            try
            {
                geometry.Add(MapGeometryEntity(snapshot));
            }
            catch (Exception)
            {
                partiallySucceeded = true;
                diagnostics.Add(new SceneDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "GEOMETRY_ENTITY_MAPPING_FAILED",
                    Message = $"A supported {snapshot.EntityType} entity could not be mapped to the geometry contract."
                });
            }
        }

        var geometryDocument = new CadGeometryDocument
        {
            Summary = summary,
            ModelSpaceEntities = geometry,
            Diagnostics = diagnostics
        };

        return new CadGeometryExtractionResult
        {
            Status = partiallySucceeded
                ? CadGeometryExtractionStatus.PartiallySucceeded
                : CadGeometryExtractionStatus.Succeeded,
            Document = geometryDocument,
            Diagnostics = diagnostics
        };
    }

    private static CadDocumentModel MapSummary(
        CadDocument sourceDocument,
        string sourcePath,
        IReadOnlyList<EntityInspectionSnapshot> snapshots)
    {
        var layers = snapshots
            .GroupBy(snapshot => snapshot.LayerName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CadLayerModel
            {
                Name = group.Key,
                EntityCount = group.Count(),
                Bounds = CadBoundsAggregator.Aggregate(group.Select(snapshot => snapshot.Bounds))
            })
            .ToArray();
        var entityTypes = snapshots
            .GroupBy(snapshot => snapshot.EntityType, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CadEntityTypeSummary(group.Key, group.Count()))
            .ToArray();

        return new CadDocumentModel
        {
            SourcePath = sourcePath,
            SourceFormat = CadSourceFormat.Dxf,
            Unit = MapUnit(sourceDocument.Header.InsUnits),
            Bounds = CadBoundsAggregator.Aggregate(snapshots.Select(snapshot => snapshot.Bounds)),
            Layers = layers,
            Blocks = MapBlocks(sourceDocument),
            EntityTypes = entityTypes,
            Diagnostics = MapDiagnostics(snapshots, sourceDocument.Header.InsUnits, sourcePath)
        };
    }

    private static IReadOnlyList<EntityInspectionSnapshot> CreateSnapshots(CadDocument sourceDocument) =>
        sourceDocument.Entities
            .Select((entity, sourceOrder) => new EntityInspectionSnapshot(
                sourceOrder,
                entity,
                entity.Layer?.Name ?? "0",
                NormalizeDxfEntityType(entity),
                entity.GetType().Name,
                EvaluateBounds(entity)))
            .ToArray();

    private static CadGeometryEntity MapGeometryEntity(EntityInspectionSnapshot snapshot) =>
        snapshot.Entity switch
        {
            Line line => new CadLineGeometry(
                snapshot.SourceOrder,
                snapshot.LayerName,
                MapPoint(line.StartPoint),
                MapPoint(line.EndPoint),
                snapshot.Bounds),
            LwPolyline polyline => new CadPolylineGeometry(
                snapshot.SourceOrder,
                snapshot.LayerName,
                polyline.Vertices.Select(vertex => new CadPolylineVertex(
                    new CadPoint3(vertex.Location.X, vertex.Location.Y, polyline.Elevation),
                    vertex.Bulge)).ToArray(),
                polyline.IsClosed,
                snapshot.Bounds),
            Arc arc => new CadArcGeometry(
                snapshot.SourceOrder,
                snapshot.LayerName,
                MapPoint(arc.Center),
                arc.Radius,
                ToDegrees(arc.StartAngle),
                ToDegrees(arc.EndAngle),
                snapshot.Bounds),
            Circle circle => new CadCircleGeometry(
                snapshot.SourceOrder,
                snapshot.LayerName,
                MapPoint(circle.Center),
                circle.Radius,
                snapshot.Bounds),
            Insert insert => new CadInsertGeometry(
                snapshot.SourceOrder,
                snapshot.LayerName,
                insert.Block?.Name ?? string.Empty,
                MapPoint(insert.InsertPoint),
                ToDegrees(insert.Rotation),
                new CadScale3(insert.XScale, insert.YScale, insert.ZScale),
                snapshot.Bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot))
        };

    private static IReadOnlyList<CadBlockModel> MapBlocks(CadDocument sourceDocument) =>
        sourceDocument.BlockRecords
            .Where(IsOrdinaryBlock)
            .OrderBy(block => block.Name, StringComparer.Ordinal)
            .Select(block => new CadBlockModel(
                block.Name,
                block.Entities.Count,
                CadBoundsAggregator.Aggregate(block.Entities.Select(EvaluateBounds))))
            .ToArray();

    private static bool IsOrdinaryBlock(BlockRecord block)
    {
        if (block.Layout is not null)
        {
            return false;
        }

        var blockFlags = block.BlockEntity?.Flags ?? BlockTypeFlags.None;
        return (blockFlags & (BlockTypeFlags.XRef | BlockTypeFlags.XRefOverlay)) == BlockTypeFlags.None;
    }

    private static IReadOnlyList<SceneDiagnostic> MapDiagnostics(
        IReadOnlyCollection<EntityInspectionSnapshot> snapshots,
        ACadSharp.Types.Units.UnitsType sourceUnit,
        string sourcePath)
    {
        var diagnostics = new List<SceneDiagnostic>();
        if (snapshots.Count == 0)
        {
            diagnostics.Add(new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Information,
                Code = "DXF_DOCUMENT_EMPTY",
                Message = "The DXF document contains no entities.",
                SourcePath = sourcePath
            });
        }

        if (MapUnit(sourceUnit) is CadUnit.Unknown or CadUnit.Unitless)
        {
            diagnostics.Add(new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "DXF_UNIT_UNKNOWN",
                Message = "The DXF insertion unit cannot be normalized to a known length unit.",
                SourcePath = sourcePath
            });
        }

        foreach (var runtimeTypeName in snapshots
                     .Where(snapshot => !IsSupported(snapshot.Entity))
                     .Select(snapshot => snapshot.RuntimeTypeName)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            diagnostics.Add(new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "DXF_ENTITY_UNSUPPORTED",
                Message = $"The DXF entity type '{runtimeTypeName}' has no SB-05 geometry mapping.",
                SourcePath = sourcePath
            });
        }

        return diagnostics;
    }

    private static bool IsSupported(Entity entity) =>
        entity is Line or LwPolyline or Arc or Circle or Insert;

    private static CadPoint3 MapPoint(CSMath.XYZ point) => new(point.X, point.Y, point.Z);

    private static double ToDegrees(double radians) => radians * (180d / Math.PI);

    private static string NormalizeDxfEntityType(Entity entity) =>
        entity.ObjectType.ToString().ToUpperInvariant();

    private static CadBounds EvaluateBounds(Entity entity)
    {
        try
        {
            var bounds = entity.GetBoundingBox();
            return CadBounds.Computed(
                bounds.Min.X,
                bounds.Min.Y,
                bounds.Min.Z,
                bounds.Max.X,
                bounds.Max.Y,
                bounds.Max.Z);
        }
        catch (Exception)
        {
            return CadBounds.NotEvaluated;
        }
    }

    private static CadUnit MapUnit(ACadSharp.Types.Units.UnitsType unit) =>
        unit switch
        {
            ACadSharp.Types.Units.UnitsType.Unitless => CadUnit.Unitless,
            ACadSharp.Types.Units.UnitsType.Millimeters => CadUnit.Millimeters,
            ACadSharp.Types.Units.UnitsType.Centimeters => CadUnit.Centimeters,
            ACadSharp.Types.Units.UnitsType.Meters => CadUnit.Meters,
            ACadSharp.Types.Units.UnitsType.Inches => CadUnit.Inches,
            ACadSharp.Types.Units.UnitsType.Feet => CadUnit.Feet,
            _ => CadUnit.Unknown
        };

    private sealed record EntityInspectionSnapshot(
        int SourceOrder,
        Entity Entity,
        string LayerName,
        string EntityType,
        string RuntimeTypeName,
        CadBounds Bounds);
}
