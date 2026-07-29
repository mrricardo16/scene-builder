using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

public enum CadInspectionStatus
{
    NotImplemented = 0,
    Succeeded = 1,
    Failed = 2
}

public enum CadGeometryExtractionStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2
}

public enum DwgProbeStatus
{
    Unsupported = 0,
    Available = 1,
    Unavailable = 2
}

public sealed record CadInspectionRequest
{
    public string SourcePath { get; init; } = string.Empty;

    public CadSourceFormat SourceFormat { get; init; } = CadSourceFormat.Unknown;
}

public sealed record CadInspectionResult
{
    public CadInspectionStatus Status { get; init; } = CadInspectionStatus.NotImplemented;

    public CadDocumentModel? Document { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record CadGeometryExtractionResult
{
    public CadGeometryExtractionStatus Status { get; init; } = CadGeometryExtractionStatus.Failed;

    public CadGeometryDocument? Document { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record DwgProbeResult
{
    public DwgProbeStatus Status { get; init; } = DwgProbeStatus.Unsupported;

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface IDxfInspector
{
    Task<CadInspectionResult> InspectAsync(
        CadInspectionRequest request,
        CancellationToken cancellationToken);
}

public interface ICadGeometryExtractor
{
    Task<CadGeometryExtractionResult> ExtractAsync(
        CadInspectionRequest request,
        CancellationToken cancellationToken);
}

public interface IDwgInspector
{
    Task<DwgProbeResult> ProbeAsync(
        CadInspectionRequest request,
        CancellationToken cancellationToken);
}

public sealed class UnsupportedDwgProbe : IDwgInspector
{
    public Task<DwgProbeResult> ProbeAsync(
        CadInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new DwgProbeResult
        {
            Status = DwgProbeStatus.Unsupported,
            Diagnostics =
            [
                new SceneDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "DWG_UNSUPPORTED",
                    Message = "DWG inspection is not supported by the current adapter."
                }
            ]
        });
    }
}
