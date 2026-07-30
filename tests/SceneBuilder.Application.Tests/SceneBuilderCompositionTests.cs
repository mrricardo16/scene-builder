using SceneBuilder.Application.Doctor;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class SceneBuilderCompositionTests
{
    [Fact]
    public void CreateDefault_resolves_doctor_and_the_stable_product_capability_registry_without_side_effects()
    {
        var temporaryOutput = Path.Combine(Path.GetTempPath(), "scene-builder-composition", Guid.NewGuid().ToString("N"));

        var host = SceneBuilderComposition.CreateDefault();

        Assert.NotNull(host.DoctorService);
        Assert.IsType<DoctorService>(host.DoctorService);
        Assert.NotNull(host.OutputRootPolicy);
        Assert.Equal(
        [
            "DOCTOR",
            "APPLICATION_HOST",
            "CLI_FRAMEWORK",
            "ANALYZE",
            "DXF_ANALYZE",
            "PLAN_CREATE",
            "PLAN_VALIDATE",
            "PLAN_FREEZE",
            "BUILD_GLB",
            "BUILD_SCENE_PACKAGE",
            "BUILD_3D_TILES",
            "AVALONIA_DESKTOP",
            "DWG_INPUT"
        ],
        host.CapabilityRegistry.GetCapabilities().Select(item => item.Code));
        Assert.False(Directory.Exists(temporaryOutput));
    }
}
