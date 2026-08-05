using SceneBuilder.Application;
using SceneBuilder.Application.Doctor;
using SceneBuilder.Cad;
using SceneBuilder.Infrastructure.Doctor;

namespace SceneBuilder.Composition;

public static class SceneBuilderComposition
{
    private static readonly TimeSpan ExecutableVersionTimeout = TimeSpan.FromSeconds(10);

    public static SceneBuilderHost CreateDefault()
    {
        var fileSystem = new SystemFileSystem();
        var versionReader = new ProcessExecutableVersionReader(ExecutableVersionTimeout);
        var doctorService = new DoctorService(
        [
            new DotNetRuntimeProbe(),
            new ConfiguredExecutableProbe(DoctorTool.Blender, fileSystem, versionReader),
            new ConfiguredExecutableProbe(DoctorTool.TilesConverter, fileSystem, versionReader)
        ]);

        var outputRootPolicy = new OutputRootPolicy();
        var analyzeHandler = new CadImportAnalysisHandler(
        [
            new DxfCadInputAdapter(new ACadSharpDxfInspector(), new ACadSharpDxfGeometryExtractor()),
            new UnsupportedDwgCadInputAdapter(new UnsupportedDwgProbe())
        ],
        outputRootPolicy,
        snapshotFactory: new CadBuildInputSnapshotFactory(),
        snapshotSerializer: new CadBuildInputSnapshotSerializer());
        var ruleSetSnapshotter = new ConversionPlanRuleSetSnapshotter();
        var defaultProfile = new ConversionPlanDefaultProfileV2(ruleSetSnapshotter);
        var configurationResolver = new FrozenBuildConfigurationResolver();
        var frozenSerializer = new FrozenPlanV2Serializer();
        var readinessValidator = new FrozenPlanBuildReadinessValidator(ruleSetSnapshotter);
        var assetImporter = new PlanAssetResourceImporter(outputRootPolicy);
        var planService = new ConversionPlanService(outputRootPolicy, ruleSetSnapshotter, defaultProfile, configurationResolver, readinessValidator, frozenSerializer);
        return new SceneBuilderHost(doctorService, CreateCapabilityRegistry(), outputRootPolicy, analyzeHandler, planService, new BuildFrozenPlanHandler(outputRootPolicy, frozenSerializer, readinessValidator), readinessValidator, frozenSerializer, ruleSetSnapshotter, assetImporter, configurationResolver);
    }

    private static ISceneCapabilityRegistry CreateCapabilityRegistry() => new SceneCapabilityRegistry(
    [
        new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "APPLICATION_HOST", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "CLI_FRAMEWORK", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "ANALYZE", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "DXF_ANALYZE", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "ANALYSIS_BUILD_SNAPSHOT", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "BUILD", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "PLAN_CREATE", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "PLAN_VALIDATE", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "PLAN_FREEZE", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "BUILD_READY_FROZEN_PLAN", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "BUILD_GLB", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "BUILD_SCENE_PACKAGE", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "BUILD_3D_TILES", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "AVALONIA_DESKTOP", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "DWG_INPUT", State = SceneCapabilityState.Unsupported }
    ]);
}
