using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record SceneDraftApplicationRequest
{
    public string DraftId { get; init; } = string.Empty;

    public CadDocumentModel SourceDocument { get; init; } = new();

    public NormalizedCadGeometryDocument Geometry { get; init; } = new();

    public CadContourDocument OriginalContours { get; init; } = new();

    public CadGeometryRepairResult? RepairResult { get; init; }

    public CadClassificationResult Classification { get; init; } = new();
}

public sealed class SceneDraftApplicationService
{
    private readonly SceneDraftBuilder _sceneDraftBuilder;

    public SceneDraftApplicationService(SceneDraftBuilder? sceneDraftBuilder = null)
    {
        _sceneDraftBuilder = sceneDraftBuilder ?? new SceneDraftBuilder();
    }

    public SceneDraftBuildResult Build(SceneDraftApplicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selectedContours = request.RepairResult?.RepairedDocument ?? request.OriginalContours;

        return _sceneDraftBuilder.Build(new SceneDraftBuildRequest
        {
            DraftId = request.DraftId,
            SourceDocument = request.SourceDocument,
            Geometry = request.Geometry,
            Contours = selectedContours,
            Classification = request.Classification
        });
    }
}
