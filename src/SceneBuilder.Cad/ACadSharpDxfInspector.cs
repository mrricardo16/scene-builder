using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
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

            return new CadInspectionResult
            {
                Status = CadInspectionStatus.Succeeded,
                Document = MapDocument(document, sourcePath)
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
        var entities = sourceDocument.Entities.ToArray();
        var layers = entities
            .GroupBy(entity => entity.Layer?.Name ?? "0", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CadLayerModel
            {
                Name = group.Key,
                EntityCount = group.Count(),
                Bounds = MapBounds(group)
            })
            .ToArray();

        return new CadDocumentModel
        {
            SourcePath = sourcePath,
            SourceFormat = CadSourceFormat.Dxf,
            Unit = MapUnit(sourceDocument.Header.InsUnits),
            Bounds = MapBounds(entities),
            Layers = layers
        };
    }

    private static CadBounds MapBounds(IEnumerable<Entity> entities)
    {
        var points = entities
            .Select(entity => entity.GetBoundingBox())
            .SelectMany(bounds => new[] { bounds.Min, bounds.Max })
            .Where(point =>
                double.IsFinite(point.X) &&
                double.IsFinite(point.Y) &&
                double.IsFinite(point.Z))
            .ToArray();

        return points.Length == 0
            ? CadBounds.Empty
            : new CadBounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Min(point => point.Z),
                points.Max(point => point.X),
                points.Max(point => point.Y),
                points.Max(point => point.Z));
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
}
