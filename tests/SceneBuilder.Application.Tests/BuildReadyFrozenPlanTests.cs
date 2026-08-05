using System.Text.Json;
using System.Text;
using SceneBuilder.Application;
using SceneBuilder.Composition;
using SceneBuilder.Domain;

namespace SceneBuilder.Application.Tests;

public sealed class BuildReadyFrozenPlanTests
{
    [Fact]
    public async Task AnalysisV2_DefaultPlanFlow_PublishesFrozenPlanV2WithBuildConfiguration()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        try
        {
            var host = SceneBuilderComposition.CreateDefault();
            var analysis = await host.CadImportAnalysisHandler!.ExecuteAsync(
                new CadImportAnalysisRequest
                {
                    InputPath = sourcePath,
                    OutputRootDirectory = outputRoot,
                    UnitOverride = CadUnit.Meters
                },
                progress: null,
                CancellationToken.None);
            Assert.Equal(SceneOperationStatus.Succeeded, analysis.Status);

            var service = host.ConversionPlanService!;
            var draft = await service.CreateDraftAsync(
                new CreateConversionPlanDraftRequest
                {
                    AnalysisPath = Path.Combine(outputRoot, "analysis", "cad-analysis.json"),
                    OutputRootDirectory = outputRoot
                },
                CancellationToken.None);
            Assert.Equal("2.0", draft.Draft!.ContractVersion);

            var planPath = Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json");
            var validation = await service.ValidateAsync(
                new ValidateConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot },
                CancellationToken.None);
            Assert.Equal(ConversionPlanValidationStatus.Valid, validation.ValidationStatus);

            var frozen = await service.FreezeAsync(
                new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot },
                CancellationToken.None);
            Assert.Equal(SceneOperationStatus.Succeeded, frozen.Status);

            var frozenPath = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(frozenPath));
            Assert.Equal("2.0", document.RootElement.GetProperty("contractVersion").GetString());
            Assert.Equal("1.0", document.RootElement.GetProperty("buildInput").GetProperty("snapshotContractVersion").GetString());
            Assert.Equal("singleGlb", document.RootElement.GetProperty("buildConfiguration").GetProperty("outputs").GetProperty("primaryOutput").GetString());
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FreezeV2_IsDeterministic_AndRepeatedFreezeDoesNotOverwrite()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (host, planPath) = await CreateValidatedPlanAsync(outputRoot);
            var service = host.ConversionPlanService!;
            var first = await service.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
            var path = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json");
            var bytes = await File.ReadAllBytesAsync(path);
            var second = await service.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Succeeded, first.Status);
            Assert.Equal(SceneOperationStatus.Succeeded, second.Status);
            Assert.Equal(first.FrozenPlan!.FrozenPlanId, second.FrozenPlan!.FrozenPlanId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BuildHandler_StopsAtReadiness_AndDoesNotRunGenerators()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (host, planPath) = await CreateValidatedPlanAsync(outputRoot);
            await host.ConversionPlanService!.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
            var result = await host.BuildFrozenPlanHandler!.ExecuteAsync(
                new BuildFrozenPlanRequest { FrozenPlanPath = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json"), OutputRootDirectory = outputRoot },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Failed, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BUILD_NOT_IMPLEMENTED");
            Assert.Empty(Directory.EnumerateFiles(outputRoot, "*.glb", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FreezeV2_RejectsChangedSnapshotAfterValidation()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (host, planPath) = await CreateValidatedPlanAsync(outputRoot);
            var snapshotPath = Path.Combine(outputRoot, "analysis", "build-input-snapshot.json");
            var snapshot = await File.ReadAllTextAsync(snapshotPath, BuildReadyPlanJson.Utf8);
            using var snapshotDocument = JsonDocument.Parse(snapshot);
            var originalHash = snapshotDocument.RootElement.GetProperty("contentHash").GetString()!;
            await File.WriteAllTextAsync(snapshotPath, snapshot.Replace(originalHash, new string('0', 64), StringComparison.Ordinal), BuildReadyPlanJson.Utf8);

            var result = await host.ConversionPlanService!.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Failed, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PLAN_VALIDATION_STALE");
            Assert.False(File.Exists(Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json")));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FrozenPlanV1_RemainsNotBuildReady()
    {
        var readiness = await new FrozenPlanBuildReadinessValidator(new ConversionPlanRuleSetSnapshotter()).ValidateAsync(
            new FrozenConversionPlan { ContractVersion = "1.0", FrozenPlanId = "frozen-legacy", Draft = new ConversionPlanDraft() },
            Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests"),
            CancellationToken.None);

        Assert.Equal(FrozenPlanBuildReadinessStatus.NotReady, readiness.Status);
        Assert.Contains(readiness.Diagnostics, diagnostic => diagnostic.Code == "FROZEN_PLAN_NOT_BUILD_READY");
        Assert.Contains(readiness.Diagnostics, diagnostic => diagnostic.Code == "PLAN_REFREEZE_REQUIRED");
    }

    [Fact]
    public async Task AnalysisV1_StaysOnLegacyPlanPath_AndNeverBecomesBuildReady()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(outputRoot, "analysis"));
            await File.WriteAllTextAsync(Path.Combine(outputRoot, "analysis", "cad-analysis.json"), """
                {
                  "contractVersion": "1.0",
                  "analysisId": "analysis-legacy",
                  "sourceFingerprint": "fingerprint-legacy",
                  "status": "succeeded",
                  "input": { "unit": "meters" },
                  "artifacts": [{ "kind": "analysis", "relativePath": "analysis/cad-analysis.json", "isValidated": true }]
                }
                """, BuildReadyPlanJson.Utf8);
            var service = SceneBuilderComposition.CreateDefault().ConversionPlanService!;
            var draft = await service.CreateDraftAsync(new CreateConversionPlanDraftRequest { AnalysisPath = Path.Combine(outputRoot, "analysis", "cad-analysis.json"), OutputRootDirectory = outputRoot }, CancellationToken.None);
            var revised = await service.SaveRevisionAsync(new SaveConversionPlanRevisionRequest
            {
                PreviousPlanPath = Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json"),
                OutputRootDirectory = outputRoot,
                Draft = draft.Draft! with { Outputs = new OutputConfigurationPlan { GenerateSingleGlb = true } }
            }, CancellationToken.None);
            var planPath = Path.Combine(outputRoot, "plans", "revision-0002", "plan-draft.json");
            var validation = await service.ValidateAsync(new ValidateConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
            var frozen = await service.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);

            Assert.Equal("1.0", revised.Draft!.ContractVersion);
            Assert.Equal(ConversionPlanValidationStatus.Valid, validation.ValidationStatus);
            Assert.Equal(SceneOperationStatus.Succeeded, frozen.Status);
            Assert.Equal(FrozenPlanBuildReadinessStatus.NotReady, frozen.BuildReadiness);
            Assert.Equal("1.0", frozen.FrozenPlan!.ContractVersion);
            Assert.Equal(revised.Draft.PlanId, frozen.FrozenPlan.Draft!.PlanId);
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAssetResourceImporter_StoresValidatedGlbByContentHash()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-core-04b-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(outputRoot, "source.glb");
        try
        {
            Directory.CreateDirectory(outputRoot);
            await File.WriteAllBytesAsync(sourcePath, CreateMinimalGlb());
            var importer = SceneBuilderComposition.CreateDefault().PlanAssetResourceImporter!;
            var result = await importer.ImportAsync(new PlanAssetImportRequest(outputRoot, "facility", CadAssetKind.StaticFacility, sourcePath), CancellationToken.None);

            Assert.NotNull(result.Resource);
            Assert.Empty(result.Diagnostics);
            Assert.Equal($"plans/resources/assets/{result.Resource!.ContentHash}/asset.glb", result.Resource.ResourceRelativePath);
            Assert.True(File.Exists(Path.Combine(outputRoot, result.Resource.ResourceRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static async Task<(SceneBuilderHost Host, string PlanPath)> CreateValidatedPlanAsync(string outputRoot)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        var host = SceneBuilderComposition.CreateDefault();
        await host.CadImportAnalysisHandler!.ExecuteAsync(new CadImportAnalysisRequest { InputPath = sourcePath, OutputRootDirectory = outputRoot, UnitOverride = CadUnit.Meters }, null, CancellationToken.None);
        var service = host.ConversionPlanService!;
        var planPath = Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json");
        var draft = await service.CreateDraftAsync(new CreateConversionPlanDraftRequest { AnalysisPath = Path.Combine(outputRoot, "analysis", "cad-analysis.json"), OutputRootDirectory = outputRoot }, CancellationToken.None);
        Assert.Equal(SceneOperationStatus.Succeeded, draft.Status);
        var validation = await service.ValidateAsync(new ValidateConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
        Assert.Equal(ConversionPlanValidationStatus.Valid, validation.ValidationStatus);
        return (host, planPath);
    }

    private static byte[] CreateMinimalGlb()
    {
        var json = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"nodes\":[{}]}");
        var originalLength = json.Length;
        var paddedLength = (json.Length + 3) / 4 * 4;
        Array.Resize(ref json, paddedLength);
        Array.Fill(json, (byte)0x20, originalLength, paddedLength - originalLength);
        var bytes = new byte[20 + paddedLength];
        BitConverter.GetBytes(0x46546C67u).CopyTo(bytes, 0);
        BitConverter.GetBytes(2u).CopyTo(bytes, 4);
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);
        BitConverter.GetBytes((uint)paddedLength).CopyTo(bytes, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(bytes, 16);
        json.CopyTo(bytes, 20);
        return bytes;
    }
}
