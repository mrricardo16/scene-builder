using ACadSharp.IO;
using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

public sealed class ACadSharpDxfGeometryExtractor : ICadGeometryExtractor
{
    public Task<CadGeometryExtractionResult> ExtractAsync(
        CadInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SourceFormat is not CadSourceFormat.Dxf)
        {
            return Task.FromResult(Failed(
                "DXF_SOURCE_FORMAT_INVALID",
                "The DXF geometry extractor accepts only DXF source format requests."));
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
        {
            return Task.FromResult(Failed(
                "DXF_SOURCE_NOT_FOUND",
                "The requested DXF source file does not exist."));
        }

        return Task.Run(() => ExtractSource(request.SourcePath, cancellationToken), cancellationToken);
    }

    private static CadGeometryExtractionResult ExtractSource(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = DxfReader.Read(sourcePath, null);
            cancellationToken.ThrowIfCancellationRequested();

            return ACadSharpDxfDocumentMapper.MapGeometry(document, sourcePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed("DXF_PARSE_FAILED", "The DXF source could not be parsed.");
        }
    }

    private static CadGeometryExtractionResult Failed(string code, string message) =>
        new()
        {
            Status = CadGeometryExtractionStatus.Failed,
            Diagnostics =
            [
                new SceneDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = code,
                    Message = message
                }
            ]
        };
}
