using SceneBuilder.Application;
using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Composition;

public sealed class SceneBuilderHost(
    DoctorService doctorService,
    ISceneCapabilityRegistry capabilityRegistry,
    IOutputRootPolicy? outputRootPolicy = null,
    CadImportAnalysisHandler? cadImportAnalysisHandler = null,
    IConversionPlanService? conversionPlanService = null,
    BuildFrozenPlanHandler? buildFrozenPlanHandler = null,
    FrozenPlanBuildReadinessValidator? frozenPlanBuildReadinessValidator = null,
    FrozenPlanV2Serializer? frozenPlanV2Serializer = null,
    ConversionPlanRuleSetSnapshotter? conversionPlanRuleSetSnapshotter = null,
    PlanAssetResourceImporter? planAssetResourceImporter = null,
    FrozenBuildConfigurationResolver? frozenBuildConfigurationResolver = null)
{
    public DoctorService DoctorService { get; } = doctorService ?? throw new ArgumentNullException(nameof(doctorService));

    public ISceneCapabilityRegistry CapabilityRegistry { get; } = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));

    public IOutputRootPolicy OutputRootPolicy { get; } = outputRootPolicy ?? new OutputRootPolicy();

    public CadImportAnalysisHandler? CadImportAnalysisHandler { get; } = cadImportAnalysisHandler;

    public IConversionPlanService? ConversionPlanService { get; } = conversionPlanService;

    public BuildFrozenPlanHandler? BuildFrozenPlanHandler { get; } = buildFrozenPlanHandler;

    public FrozenPlanBuildReadinessValidator? FrozenPlanBuildReadinessValidator { get; } = frozenPlanBuildReadinessValidator;

    public FrozenPlanV2Serializer? FrozenPlanV2Serializer { get; } = frozenPlanV2Serializer;

    public ConversionPlanRuleSetSnapshotter? ConversionPlanRuleSetSnapshotter { get; } = conversionPlanRuleSetSnapshotter;

    public PlanAssetResourceImporter? PlanAssetResourceImporter { get; } = planAssetResourceImporter;

    public FrozenBuildConfigurationResolver? FrozenBuildConfigurationResolver { get; } = frozenBuildConfigurationResolver;
}
