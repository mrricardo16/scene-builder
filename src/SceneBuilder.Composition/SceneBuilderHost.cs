using SceneBuilder.Application;
using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Composition;

public sealed class SceneBuilderHost(
    DoctorService doctorService,
    ISceneCapabilityRegistry capabilityRegistry,
    IOutputRootPolicy? outputRootPolicy = null,
    CadImportAnalysisHandler? cadImportAnalysisHandler = null)
{
    public DoctorService DoctorService { get; } = doctorService ?? throw new ArgumentNullException(nameof(doctorService));

    public ISceneCapabilityRegistry CapabilityRegistry { get; } = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));

    public IOutputRootPolicy OutputRootPolicy { get; } = outputRootPolicy ?? new OutputRootPolicy();

    public CadImportAnalysisHandler? CadImportAnalysisHandler { get; } = cadImportAnalysisHandler;
}
