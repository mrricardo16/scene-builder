using SceneBuilder.Application;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class ConversionPlanServiceTests
{
    [Fact]
    public async Task CreateDraftAsync_WithValidatedAnalysis_PublishesRevisionOne()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-plan-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        try
        {
            var host = SceneBuilderComposition.CreateDefault();
            var analysis = await host.CadImportAnalysisHandler!.ExecuteAsync(
                new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = outputRoot }, null, CancellationToken.None);
            Assert.Equal(SceneOperationStatus.Succeeded, analysis.Status);

            var draft = await host.ConversionPlanService!.CreateDraftAsync(
                new CreateConversionPlanDraftRequest
                {
                    AnalysisPath = Path.Combine(outputRoot, "analysis", "cad-analysis.json"),
                    OutputRootDirectory = outputRoot
                },
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Succeeded, draft.Status);
            Assert.Equal(1, draft.Draft!.Revision);
            Assert.Equal(ConversionPlanValidationStatus.NotValidated, draft.Draft.ValidationStatus);
            Assert.True(File.Exists(Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json")));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveValidateAndFreezeAsync_PublishesIndependentRevisionArtifacts()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-plan-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        try
        {
            var host = SceneBuilderComposition.CreateDefault();
            await host.CadImportAnalysisHandler!.ExecuteAsync(new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = outputRoot }, null, CancellationToken.None);
            var service = host.ConversionPlanService!;
            var initial = await service.CreateDraftAsync(new CreateConversionPlanDraftRequest { AnalysisPath = Path.Combine(outputRoot, "analysis", "cad-analysis.json"), OutputRootDirectory = outputRoot }, CancellationToken.None);
            var revision = await service.SaveRevisionAsync(new SaveConversionPlanRevisionRequest
            {
                PreviousPlanPath = Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json"),
                OutputRootDirectory = outputRoot,
                Draft = initial.Draft! with { Outputs = new OutputConfigurationPlan { GenerateScenePackage = true } }
            }, CancellationToken.None);
            var planPath = Path.Combine(outputRoot, "plans", "revision-0002", "plan-draft.json");
            var validation = await service.ValidateAsync(new ValidateConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
            var frozen = await service.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);

            Assert.Equal(2, revision.Draft!.Revision);
            Assert.Equal(ConversionPlanValidationStatus.Valid, validation.ValidationStatus);
            Assert.Equal(SceneOperationStatus.Succeeded, frozen.Status);
            Assert.True(File.Exists(Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "plans", "revision-0002", "validation.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "plans", "frozen", "revision-0002.json")));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }
}
