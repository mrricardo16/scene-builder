using SceneBuilder.Application;
using SceneBuilder.Application.Doctor;
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

        return new SceneBuilderHost(
            doctorService,
            CreateCapabilityRegistry(),
            new OutputRootPolicy());
    }

    private static ISceneCapabilityRegistry CreateCapabilityRegistry() => new SceneCapabilityRegistry(
    [
        new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "APPLICATION_HOST", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "CLI_FRAMEWORK", State = SceneCapabilityState.Available },
        new SceneCapability { Code = "ANALYZE", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "PLAN_VALIDATE", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "PLAN_FREEZE", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "BUILD_GLB", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "BUILD_SCENE_PACKAGE", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "BUILD_3D_TILES", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "AVALONIA_DESKTOP", State = SceneCapabilityState.Planned },
        new SceneCapability { Code = "DWG_INPUT", State = SceneCapabilityState.Unsupported }
    ]);
}
