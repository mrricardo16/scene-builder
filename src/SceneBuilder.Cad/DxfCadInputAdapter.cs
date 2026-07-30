using SceneBuilder.Application;
using SceneBuilder.Domain;

namespace SceneBuilder.Cad;

public sealed class DxfCadInputAdapter : ICadInputAdapter
{
    private readonly IDxfInspector _inspector;
    private readonly ICadGeometryExtractor _geometryExtractor;
    private readonly CadGeometryNormalizer _normalizer;
    private readonly CadContourBuilder _contourBuilder;
    private readonly CadGeometryRepairAnalyzer _repairAnalyzer;

    public DxfCadInputAdapter(
        IDxfInspector inspector,
        ICadGeometryExtractor geometryExtractor,
        CadGeometryNormalizer? normalizer = null,
        CadContourBuilder? contourBuilder = null,
        CadGeometryRepairAnalyzer? repairAnalyzer = null)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _geometryExtractor = geometryExtractor ?? throw new ArgumentNullException(nameof(geometryExtractor));
        _normalizer = normalizer ?? new CadGeometryNormalizer();
        _contourBuilder = contourBuilder ?? new CadContourBuilder();
        _repairAnalyzer = repairAnalyzer ?? new CadGeometryRepairAnalyzer();
    }

    public string AdapterId => "DXF";

    public bool CanHandle(CadInputDescriptor input) => input.SourceFormat is CadSourceFormat.Dxf &&
        string.Equals(input.Extension, ".dxf", StringComparison.OrdinalIgnoreCase);

    public async Task<CadAdapterAnalysisResult> AnalyzeAsync(
        CadAdapterAnalysisRequest request,
        IProgress<SceneOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inspectionRequest = new CadInspectionRequest { SourcePath = request.ControlledInputPath, SourceFormat = CadSourceFormat.Dxf };
        Report(progress, "ANALYZE_INSPECT_DOCUMENT");
        cancellationToken.ThrowIfCancellationRequested();
        var inspected = await _inspector.InspectAsync(inspectionRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (inspected.Status is not CadInspectionStatus.Succeeded || inspected.Document is null)
        {
            return Failed(inspected.Diagnostics);
        }

        Report(progress, "ANALYZE_EXTRACT_GEOMETRY");
        var extracted = await _geometryExtractor.ExtractAsync(inspectionRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (extracted.Status is CadGeometryExtractionStatus.Failed || extracted.Document is null)
        {
            return Failed(inspected.Diagnostics.Concat(extracted.Diagnostics));
        }

        var wasUnitOverridden = request.UnitOverride is not null && inspected.Document.Unit is CadUnit.Unknown;
        var summary = !wasUnitOverridden
            ? inspected.Document
            : inspected.Document with { Unit = request.UnitOverride.GetValueOrDefault() };
        var geometryInput = extracted.Document with { Summary = summary };
        Report(progress, "ANALYZE_NORMALIZE_GEOMETRY");
        var normalized = _normalizer.Normalize(geometryInput);
        cancellationToken.ThrowIfCancellationRequested();
        if (normalized.Status is not CadGeometryNormalizationStatus.Succeeded || normalized.Document is null)
        {
            return Failed(inspected.Diagnostics.Concat(extracted.Diagnostics).Concat(normalized.Diagnostics));
        }

        Report(progress, "ANALYZE_BUILD_CONTOURS");
        var contours = _contourBuilder.Build(normalized.Document);
        cancellationToken.ThrowIfCancellationRequested();
        if (contours.Status is CadContourBuildStatus.Failed || contours.Document is null)
        {
            return Failed(inspected.Diagnostics.Concat(extracted.Diagnostics).Concat(normalized.Diagnostics).Concat(ToSceneDiagnostics(contours.Diagnostics)));
        }

        Report(progress, "ANALYZE_VALIDATE_CONTOURS");
        Report(progress, "ANALYZE_PLAN_REPAIRS");
        var repair = _repairAnalyzer.Analyze(contours.Document);
        return new CadAdapterAnalysisResult
        {
            Status = contours.Status is CadContourBuildStatus.PartiallySucceeded || extracted.Status is CadGeometryExtractionStatus.PartiallySucceeded
                ? SceneOperationStatus.PartiallySucceeded
                : SceneOperationStatus.Succeeded,
            SourceDocument = summary,
            Geometry = normalized.Document,
            Contours = contours.Document,
            RepairPlan = repair,
            WasUnitOverridden = wasUnitOverridden,
            Diagnostics = inspected.Diagnostics.Concat(extracted.Diagnostics).Concat(normalized.Diagnostics)
                .Concat(ToSceneDiagnostics(contours.Diagnostics)).Concat(repair.Diagnostics)
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal).ToArray()
        };
    }

    private static CadAdapterAnalysisResult Failed(IEnumerable<SceneDiagnostic> diagnostics) => new()
    {
        Status = SceneOperationStatus.Failed,
        Diagnostics = diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal).ToArray()
    };

    private static IEnumerable<SceneDiagnostic> ToSceneDiagnostics(IEnumerable<CadContourDiagnostic> diagnostics) => diagnostics.Select(diagnostic => new SceneDiagnostic
    {
        Severity = diagnostic.Severity,
        Code = diagnostic.Code,
        Message = diagnostic.Message
    });

    private static void Report(IProgress<SceneOperationProgress>? progress, string stageCode) => progress?.Report(new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = stageCode });
}
