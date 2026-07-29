using SceneBuilder.Application;
using SceneBuilder.Domain;

namespace SceneBuilder.Application.Tests;

public sealed class SceneDraftApplicationServiceTests
{
    [Fact]
    public void Build_uses_the_explicit_repaired_contour_document_without_repeating_repair_or_classification()
    {
        var repairedSegment = new CadLineSegment2(
            sourceOrder: 1,
            segmentOrder: 0,
            sourceLayer: "SYN_ROAD",
            sourceEntityType: "LINE",
            start: new CadPoint3(0, 0, 0),
            end: new CadPoint3(1, 0, 0));
        var repairedDocument = new CadContourDocument { OpenSegments = [repairedSegment] };
        var subject = Assert.Single(new CadClassificationSubjectBuilder().Build(new CadClassificationInput
        {
            Contours = repairedDocument,
            Geometry = new NormalizedCadGeometryDocument()
        }));

        var result = new SceneDraftApplicationService().Build(new SceneDraftApplicationRequest
        {
            DraftId = "draft:application:001",
            SourceDocument = new CadDocumentModel(),
            Geometry = new NormalizedCadGeometryDocument(),
            OriginalContours = new CadContourDocument(),
            RepairResult = new CadGeometryRepairResult { RepairedDocument = repairedDocument },
            Classification = new CadClassificationResult
            {
                Status = CadClassificationStatus.Succeeded,
                Objects =
                [
                    new CadObjectClassification
                    {
                        Subject = subject,
                        Classification = CadSemanticClassification.Road,
                        MatchedRuleId = "synthetic-road",
                        MatchRank = 390,
                        Priority = 1
                    }
                ]
            }
        });

        var draft = Assert.IsType<SceneDraft>(result.Draft);
        Assert.Equal(SceneDraftBuildStatus.Succeeded, result.Status);
        Assert.IsType<CadRoadObject>(Assert.Single(draft.SemanticObjects));
    }
}
