namespace SceneBuilder.Domain.Tests;

public sealed class CadStructuralSummaryContractTests
{
    [Fact]
    public void Default_models_use_not_evaluated_bounds_and_empty_summary_collections()
    {
        var document = new CadDocumentModel();

        Assert.Equal(CadBounds.NotEvaluated, document.Bounds);
        Assert.Equal(CadBounds.NotEvaluated, new CadLayerModel().Bounds);
        Assert.Equal(CadBounds.NotEvaluated, new SceneNode().Bounds);
        Assert.Empty(document.Blocks);
        Assert.Empty(document.EntityTypes);
    }

    [Fact]
    public void Block_summary_defaults_to_not_evaluated_bounds()
    {
        var summary = new CadBlockModel("SYN_BLOCK", 0);

        Assert.Equal("SYN_BLOCK", summary.Name);
        Assert.Equal(0, summary.EntityCount);
        Assert.Equal(CadBounds.NotEvaluated, summary.Bounds);
    }

    [Fact]
    public void Structural_summaries_reject_invalid_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadBlockModel("SYN_BLOCK", -1));
        Assert.Throws<ArgumentException>(() => new CadEntityTypeSummary(" ", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CadEntityTypeSummary("LINE", -1));
    }
}
