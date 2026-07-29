using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

public sealed class ACadSharpDxfInspector : IDxfInspector
{
    private const string DxfParseFailedMessage = "The DXF source could not be parsed.";

    public Task<CadInspectionResult> InspectAsync(
        CadInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SourceFormat is not CadSourceFormat.Dxf)
        {
            return Task.FromResult(Failed(
                "DXF_SOURCE_FORMAT_INVALID",
                "The DXF inspector accepts only DXF source format requests.",
                request.SourcePath));
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
        {
            return Task.FromResult(Failed(
                "DXF_SOURCE_NOT_FOUND",
                "The requested DXF source file does not exist.",
                request.SourcePath));
        }

        return Task.Run(() => InspectSource(request.SourcePath, cancellationToken), cancellationToken);
    }

    private static CadInspectionResult InspectSource(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = DxfReader.Read(sourcePath, null);
            cancellationToken.ThrowIfCancellationRequested();

            var mappedDocument = MapDocument(document, sourcePath);

            return new CadInspectionResult
            {
                Status = CadInspectionStatus.Succeeded,
                Document = mappedDocument,
                Diagnostics = mappedDocument.Diagnostics
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                "DXF_PARSE_FAILED",
                DxfParseFailedMessage,
                sourcePath);
        }
    }

    private static CadDocumentModel MapDocument(CadDocument sourceDocument, string sourcePath)
    {
        var entitySnapshots = sourceDocument.Entities
            .Select(CreateEntitySnapshot)
            .ToArray();

        var layers = entitySnapshots
            .GroupBy(snapshot => snapshot.LayerName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CadLayerModel
            {
                Name = group.Key,
                EntityCount = group.Count(),
                Bounds = CadBoundsAggregator.Aggregate(group.Select(snapshot => snapshot.Bounds))
            })
            .ToArray();

        var entityTypes = entitySnapshots
            .GroupBy(snapshot => snapshot.EntityType, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CadEntityTypeSummary(group.Key, group.Count()))
            .ToArray();

        return new CadDocumentModel
        {
            SourcePath = sourcePath,
            SourceFormat = CadSourceFormat.Dxf,
            Unit = MapUnit(sourceDocument.Header.InsUnits),
            Bounds = CadBoundsAggregator.Aggregate(entitySnapshots.Select(snapshot => snapshot.Bounds)),
            Layers = layers,
            Blocks = MapBlocks(sourceDocument),
            EntityTypes = entityTypes,
            Diagnostics = MapDiagnostics(entitySnapshots, sourceDocument.Header.InsUnits, sourcePath)
        };
    }

    private static IReadOnlyList<CadBlockModel> MapBlocks(CadDocument sourceDocument)
    {
        return sourceDocument.BlockRecords
            .Where(IsOrdinaryBlock)
            .OrderBy(block => block.Name, StringComparer.Ordinal)
            .Select(MapBlock)
            .ToArray();
    }

    private static CadBlockModel MapBlock(BlockRecord block)
    {
        var directEntityBounds = block.Entities
            .Select(EvaluateBounds)
            .ToArray();

        return new CadBlockModel(
            block.Name,
            directEntityBounds.Length,
            CadBoundsAggregator.Aggregate(directEntityBounds));
    }

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
        IReadOnlyCollection<EntityInspectionSnapshot> entitySnapshots,
        ACadSharp.Types.Units.UnitsType sourceUnit,
        string sourcePath)
    {
        var diagnostics = new List<SceneDiagnostic>();

        if (entitySnapshots.Count == 0)
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

        foreach (var runtimeTypeName in entitySnapshots
                     .Where(snapshot => snapshot.Entity is not Line and not LwPolyline)
                     .Select(snapshot => snapshot.RuntimeTypeName)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            diagnostics.Add(new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "DXF_ENTITY_UNSUPPORTED",
                Message = $"The DXF entity type '{runtimeTypeName}' has no SB-03 mapping.",
                SourcePath = sourcePath
            });
        }

        return diagnostics;
    }

    private static EntityInspectionSnapshot CreateEntitySnapshot(Entity entity)
    {
        return new EntityInspectionSnapshot(
            entity,
            entity.Layer?.Name ?? "0",
            NormalizeDxfEntityType(entity),
            entity.GetType().Name,
            EvaluateBounds(entity));
    }

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

    private static CadUnit MapUnit(ACadSharp.Types.Units.UnitsType unit)
    {
        return unit switch
        {
            ACadSharp.Types.Units.UnitsType.Unitless => CadUnit.Unitless,
            ACadSharp.Types.Units.UnitsType.Millimeters => CadUnit.Millimeters,
            ACadSharp.Types.Units.UnitsType.Centimeters => CadUnit.Centimeters,
            ACadSharp.Types.Units.UnitsType.Meters => CadUnit.Meters,
            ACadSharp.Types.Units.UnitsType.Inches => CadUnit.Inches,
            ACadSharp.Types.Units.UnitsType.Feet => CadUnit.Feet,
            _ => CadUnit.Unknown
        };
    }

    private static CadInspectionResult Failed(string code, string message, string sourcePath)
    {
        return new CadInspectionResult
        {
            Status = CadInspectionStatus.Failed,
            Diagnostics =
            [
                new SceneDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = code,
                    Message = message,
                    SourcePath = sourcePath
                }
            ]
        };
    }

    private sealed record EntityInspectionSnapshot(
        Entity Entity,
        string LayerName,
        string EntityType,
        string RuntimeTypeName,
        CadBounds Bounds);
}
