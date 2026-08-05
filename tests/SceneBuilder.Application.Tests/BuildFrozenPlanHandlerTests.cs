using SceneBuilder.Application;
using SceneBuilder.Composition;
using SceneBuilder.Domain;

namespace SceneBuilder.Application.Tests;

public sealed class BuildFrozenPlanHandlerTests
{
    [Fact]
    public void FrozenPlanSceneDraftBuilder_AppliesOriginYawAndZOffsetBeforeDraftBuild()
    {
        var segments = new CadCurveSegment2[]
        {
            new CadLineSegment2(0, 0, "WALL", "LINE", new CadPoint3(1, 0, 0), new CadPoint3(2, 0, 0)),
            new CadLineSegment2(1, 0, "WALL", "LINE", new CadPoint3(2, 0, 0), new CadPoint3(2, 1, 0)),
            new CadLineSegment2(2, 0, "WALL", "LINE", new CadPoint3(2, 1, 0), new CadPoint3(1, 1, 0)),
            new CadLineSegment2(3, 0, "WALL", "LINE", new CadPoint3(1, 1, 0), new CadPoint3(1, 0, 0))
        };
        var contour = new CadSegmentContour("contour-0001", segments, isSourceDefinedClosed: true) with
        {
            IsClosed = true,
            ValidationState = CadContourValidationState.Valid,
            Orientation = CadContourOrientation.CounterClockwise
        };
        var subject = new CadClassificationSubject
        {
            Id = contour.Id,
            Kind = CadClassificationSubjectKind.Contour,
            SourceLayer = "WALL",
            SourceEntityType = "LINE",
            Bounds = contour.Bounds
        };
        var snapshot = new CadBuildInputSnapshot
        {
            SnapshotId = "snapshot-test",
            AnalysisId = "analysis-test",
            SourceFingerprint = "fingerprint-test",
            SourceBounds = contour.Bounds,
            Bounds = contour.Bounds,
            GeometryObjects = segments.Select((segment, index) => new CadBuildGeometryObject($"geometry-{index:D6}", new CadLineGeometry(index, "WALL", segment.Start, segment.End))).ToArray(),
            Contours = [new CadBuildContour(contour.Id, contour, segments.Select(segment => $"geometry-{segment.SourceOrder:D6}").ToArray())],
            ClassificationSubjects = [new CadBuildClassificationSubject(subject.Id, subject, Array.Empty<string>(), [contour.Id])]
        };
        var plan = new FrozenConversionPlan
        {
            ContractVersion = "2.0",
            FrozenPlanId = "frozen-plan-test",
            FrozenPlanContentHash = "test-hash",
            BuildConfiguration = new FrozenBuildConfiguration
            {
                InputInterpretation = new FrozenInputInterpretation
                {
                    SourceUnit = CadUnit.Meters,
                    TargetUnit = CadUnit.Meters,
                    UnitConfirmation = ConversionPlanUnitConfirmation.UseSourceUnit,
                    LocalOriginStrategy = ConversionPlanLocalOriginStrategy.ExplicitOffset,
                    LocalOriginMeters = new CadPoint3(1, 0, 0),
                    YawDegrees = 90,
                    ZOffsetMeters = 2
                },
                Geometry = new GeometryAdjustmentPlan { WallHeightMeters = 3, ColumnHeightMeters = 3 },
                Classification = new ConversionPlanRuleSetSnapshot
                {
                    RuleSet = new CadRuleSet
                    {
                        ContractVersion = "1.0",
                        Rules = [new CadClassificationRule { Id = "wall-rule", Enabled = true, Priority = 1, Classification = CadSemanticClassification.Wall, Match = new CadRuleMatch { Layer = "WALL", EntityTypes = ["LINE"] } }]
                    }
                },
                Outputs = new FrozenOutputConfiguration { GenerateSingleGlb = true }
            }
        };

        var result = new FrozenPlanSceneDraftBuilder().Build(plan, snapshot);

        var wall = Assert.Single(result.Draft!.SemanticObjects.OfType<CadWallObject>());
        var first = Assert.IsType<CadSegmentContour>(wall.Profile).Segments[0];
        Assert.Equal(0, first.Start.X, 6);
        Assert.Equal(0, first.Start.Y, 6);
        Assert.Equal(2, first.Start.Z, 6);
        Assert.Equal(0, first.End.X, 6);
        Assert.Equal(1, first.End.Y, 6);
        Assert.Equal(2, first.End.Z, 6);
    }

    [Fact]
    public void BuildRequest_ContainsOnlyFrozenInputAndRuntimeToolOptions()
    {
        Assert.Equal(
            ["BlenderExecutablePath", "BlenderTimeout", "FrozenPlanPath", "OutputRootDirectory"],
            typeof(BuildFrozenPlanRequest).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_WithBuildReadyPlan_PublishesIsolatedSceneDraftAndSingleGlbJobs()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-build-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var host = SceneBuilderComposition.CreateDefault(new TestBlenderGenerator());
            var frozenPath = await CreateFrozenPlanAsync(host, outputRoot);

            var first = await host.BuildFrozenPlanHandler!.ExecuteAsync(new BuildFrozenPlanRequest
            {
                FrozenPlanPath = frozenPath,
                OutputRootDirectory = outputRoot,
                BlenderExecutablePath = "test-blender.exe"
            }, null, CancellationToken.None);
            var second = await host.BuildFrozenPlanHandler.ExecuteAsync(new BuildFrozenPlanRequest
            {
                FrozenPlanPath = frozenPath,
                OutputRootDirectory = outputRoot,
                BlenderExecutablePath = "test-blender.exe"
            }, null, CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Succeeded, first.Status);
            Assert.Equal("build-0001", first.BuildJobId);
            Assert.Equal("build-0002", second.BuildJobId);
            Assert.Equal(first.BuildContentId, second.BuildContentId);
            Assert.Contains(first.Artifacts, artifact => artifact.Kind == SceneArtifactKind.SceneDraft && artifact.IsValidated);
            Assert.Contains(first.Artifacts, artifact => artifact.Kind == SceneArtifactKind.Glb && artifact.IsValidated);
            Assert.All(first.Artifacts, artifact => Assert.StartsWith("builds/build-0001/", artifact.RelativePath, StringComparison.Ordinal));
            Assert.Contains(first.Outputs, output => output.Kind == SceneBuildOutputKind.ScenePackage && output.Status == SceneBuildOutputStatus.NotRequested);
            Assert.Contains(first.Outputs, output => output.Kind == SceneBuildOutputKind.ThreeDTiles && output.Status == SceneBuildOutputStatus.NotRequested);
            Assert.True(File.Exists(Path.Combine(outputRoot, "builds", "build-0001", "build-result.json")));
            Assert.False(Directory.EnumerateDirectories(Path.Combine(outputRoot, "builds"), ".staging-*", SearchOption.TopDirectoryOnly).Any());
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithAllOutputs_ReusesPublishedScenePackageForTiles()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-build-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var host = SceneBuilderComposition.CreateDefault(new TestBlenderGenerator());
            var frozenPath = await CreateFrozenPlanAsync(host, outputRoot, allOutputs: true);

            var result = await host.BuildFrozenPlanHandler!.ExecuteAsync(new BuildFrozenPlanRequest { FrozenPlanPath = frozenPath, OutputRootDirectory = outputRoot, BlenderExecutablePath = "test-blender.exe" }, null, CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Succeeded, result.Status);
            Assert.Contains(result.Outputs, output => output.Kind == SceneBuildOutputKind.ScenePackage && output.Status == SceneBuildOutputStatus.Succeeded);
            Assert.Contains(result.Outputs, output => output.Kind == SceneBuildOutputKind.ThreeDTiles && output.Status == SceneBuildOutputStatus.Succeeded);
            Assert.True(File.Exists(Path.Combine(outputRoot, "builds", "build-0001", "scene-package", "tileset.json")));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithLegacyFrozenPlan_RejectsBuildReadiness()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-build-tests", Guid.NewGuid().ToString("N"));
        var frozenPath = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(frozenPath)!);
            await File.WriteAllTextAsync(frozenPath, "{\"contractVersion\":\"1.0\",\"frozenPlanId\":\"frozen-legacy\"}");

            var result = await SceneBuilderComposition.CreateDefault().BuildFrozenPlanHandler!.ExecuteAsync(
                new BuildFrozenPlanRequest { FrozenPlanPath = frozenPath, OutputRootDirectory = outputRoot },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Failed, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FROZEN_PLAN_NOT_BUILD_READY");
            Assert.Empty(result.Artifacts);
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
    public async Task ExecuteAsync_WithMissingFrozenPlan_ReturnsStableFailureWithoutArtifacts()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-build-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await SceneBuilderComposition.CreateDefault().BuildFrozenPlanHandler!.ExecuteAsync(
                new BuildFrozenPlanRequest
                {
                    FrozenPlanPath = Path.Combine(outputRoot, "plans", "frozen", "revision-0001.json"),
                    OutputRootDirectory = outputRoot
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(SceneOperationStatus.Failed, result.Status);
            Assert.Empty(result.Artifacts);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BUILD_FROZEN_PLAN_NOT_FOUND");
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static async Task<string> CreateFrozenPlanAsync(SceneBuilderHost host, string outputRoot, bool allOutputs = false)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "public-synthetic-wall.dxf");
        var analysis = await host.CadImportAnalysisHandler!.ExecuteAsync(new CadImportAnalysisRequest
        {
            InputPath = sourcePath,
            OutputRootDirectory = outputRoot,
            UnitOverride = CadUnit.Meters
        }, null, CancellationToken.None);
        Assert.Equal(SceneOperationStatus.Succeeded, analysis.Status);
        var planPath = Path.Combine(outputRoot, "plans", "revision-0001", "plan-draft.json");
        var service = host.ConversionPlanService!;
        var draft = await service.CreateDraftAsync(new CreateConversionPlanDraftRequest { AnalysisPath = Path.Combine(outputRoot, "analysis", "cad-analysis.json"), OutputRootDirectory = outputRoot }, CancellationToken.None);
        if (allOutputs)
        {
            var rules = new CadRuleSetLoadResult { RuleSet = new CadRuleSet
            {
                ContractVersion = "1.0",
                Rules = [new CadClassificationRule { Id = "frozen-road", Enabled = true, Priority = 100, Match = new CadRuleMatch { Layer = "WALL", EntityTypes = ["LINE"] }, Classification = CadSemanticClassification.Road }]
            } };
            var revised = await service.SaveRevisionAsync(new SaveConversionPlanRevisionRequest
            {
                PreviousPlanPath = planPath,
                OutputRootDirectory = outputRoot,
                Draft = draft.Draft! with
                {
                    RuleSet = new ConversionPlanRuleSetSnapshotter().Create(rules.RuleSet!),
                    Outputs = new OutputConfigurationPlan { GenerateSingleGlb = true, GenerateScenePackage = true, Generate3DTiles = true }
                }
            }, CancellationToken.None);
            Assert.Equal(SceneOperationStatus.Succeeded, revised.Status);
            planPath = Path.Combine(outputRoot, "plans", "revision-0002", "plan-draft.json");
        }
        var validation = await service.ValidateAsync(new ValidateConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
        Assert.Equal(ConversionPlanValidationStatus.Valid, validation.ValidationStatus);
        var frozen = await service.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = planPath, OutputRootDirectory = outputRoot }, CancellationToken.None);
        Assert.Equal(FrozenPlanBuildReadinessStatus.Ready, frozen.BuildReadiness);
        return Path.Combine(outputRoot, "plans", "frozen", $"revision-{(allOutputs ? 2 : 1):D4}.json");
    }

    private sealed class TestBlenderGenerator : IBlenderSceneGenerator
    {
        public async Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(request.OutputDirectory);
            var path = Path.Combine(request.OutputDirectory, request.OutputFileName);
            await File.WriteAllBytesAsync(path, CreateMinimalGlb(), cancellationToken);
            return new BlenderGenerationResult { Status = BlenderGenerationStatus.Succeeded, ArtifactPath = path, GeneratedObjectCount = request.Draft.Nodes.Count };
        }
    }

    private static byte[] CreateMinimalGlb()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"nodes\":[{}]}");
        var originalLength = json.Length;
        Array.Resize(ref json, (json.Length + 3) / 4 * 4);
        Array.Fill(json, (byte)0x20, originalLength, json.Length - originalLength);
        var bytes = new byte[20 + json.Length];
        BitConverter.GetBytes(0x46546C67u).CopyTo(bytes, 0);
        BitConverter.GetBytes(2u).CopyTo(bytes, 4);
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);
        BitConverter.GetBytes((uint)json.Length).CopyTo(bytes, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(bytes, 16);
        json.CopyTo(bytes, 20);
        return bytes;
    }
}
