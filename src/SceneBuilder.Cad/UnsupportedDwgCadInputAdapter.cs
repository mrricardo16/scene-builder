using SceneBuilder.Application;
using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

public sealed class UnsupportedDwgCadInputAdapter(IDwgInspector inspector) : ICadInputAdapter
{
    private readonly IDwgInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));

    public string AdapterId => "DWG_UNSUPPORTED";

    public bool CanHandle(CadInputDescriptor input) => input.SourceFormat is CadSourceFormat.Dwg &&
        string.Equals(input.Extension, ".dwg", StringComparison.OrdinalIgnoreCase);

    public async Task<CadAdapterAnalysisResult> AnalyzeAsync(
        CadAdapterAnalysisRequest request,
        IProgress<SceneOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _inspector.ProbeAsync(new CadInspectionRequest
        {
            SourcePath = request.ControlledInputPath,
            SourceFormat = CadSourceFormat.Dwg
        }, cancellationToken);
        return new CadAdapterAnalysisResult
        {
            Status = SceneOperationStatus.Unsupported,
            Diagnostics = result.Diagnostics
        };
    }
}
