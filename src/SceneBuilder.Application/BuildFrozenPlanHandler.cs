using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed class BuildFrozenPlanHandler : ISceneOperationHandler<BuildFrozenPlanRequest, SceneBuildResult>
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly IOutputRootPolicy _outputRootPolicy;
    private readonly FrozenPlanV2Serializer _serializer;
    private readonly FrozenPlanBuildReadinessValidator _readinessValidator;
    private readonly FrozenPlanSceneDraftBuilder _draftBuilder;
    private readonly IBlenderSceneGenerator? _blender;
    private readonly IScenePackageBuildGenerator? _packageGenerator;
    private readonly ITilesetBuildGenerator? _tilesetGenerator;

    public BuildFrozenPlanHandler(IOutputRootPolicy outputRootPolicy, FrozenPlanV2Serializer? serializer = null, FrozenPlanBuildReadinessValidator? readinessValidator = null, FrozenPlanSceneDraftBuilder? draftBuilder = null, IBlenderSceneGenerator? blender = null, IScenePackageBuildGenerator? packageGenerator = null, ITilesetBuildGenerator? tilesetGenerator = null)
    {
        _outputRootPolicy = outputRootPolicy ?? throw new ArgumentNullException(nameof(outputRootPolicy));
        _serializer = serializer ?? new FrozenPlanV2Serializer();
        _readinessValidator = readinessValidator ?? new FrozenPlanBuildReadinessValidator(new ConversionPlanRuleSetSnapshotter());
        _draftBuilder = draftBuilder ?? new FrozenPlanSceneDraftBuilder();
        _blender = blender;
        _packageGenerator = packageGenerator;
        _tilesetGenerator = tilesetGenerator;
    }

    public async Task<SceneBuildResult> ExecuteAsync(BuildFrozenPlanRequest request, IProgress<SceneOperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            Report(progress, "BUILD_VALIDATE_REQUEST");
            cancellationToken.ThrowIfCancellationRequested();
            var rootValidation = _outputRootPolicy.Validate(request.OutputRootDirectory);
            if (!rootValidation.IsValid || rootValidation.NormalizedPath is null || !TryFrozenPath(request.FrozenPlanPath, rootValidation.NormalizedPath, out var frozenPath)) return Failed("BUILD_FROZEN_PLAN_NOT_FOUND");
            Report(progress, "BUILD_READ_FROZEN_PLAN");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(frozenPath, Utf8, cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || root.GetProperty("contractVersion").GetString() != "2.0" || root.GetProperty("frozenPlanId").GetString() is not { Length: > 0 }) return Failed("FROZEN_PLAN_NOT_BUILD_READY");
            if (!root.TryGetProperty("buildInput", out _)) return Failed("FROZEN_PLAN_BUILD_SNAPSHOT_MISSING");
            if (!root.TryGetProperty("buildConfiguration", out _)) return Failed("FROZEN_PLAN_BUILD_CONFIGURATION_MISSING");
            var plan = await _serializer.ReadValidatedAsync(frozenPath, cancellationToken);
            var readiness = await _readinessValidator.ValidateAsync(plan, rootValidation.NormalizedPath, cancellationToken);
            if (readiness.Status is not FrozenPlanBuildReadinessStatus.Ready) return new SceneBuildResult { Status = SceneOperationStatus.Failed, Diagnostics = readiness.Diagnostics };

            // Readiness is deliberately repeated at Build time; only the validated Snapshot is consumed below.
            var snapshotPath = Path.Combine(rootValidation.NormalizedPath, plan.BuildInput!.SnapshotArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var snapshot = await CadBuildInputSnapshotSerializer.ReadValidatedAsync(snapshotPath, cancellationToken);
            var allocation = Allocate(rootValidation.NormalizedPath);
            try
            {
                return await BuildAndPublishAsync(request, plan, snapshot, allocation, progress, cancellationToken);
            }
            finally
            {
                TryDeleteFile(allocation.ClaimPath);
                TryDeleteDirectory(allocation.StagingPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SceneBuildResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("BUILD_CANCELLED", DiagnosticSeverity.Warning)] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return Failed("BUILD_FROZEN_PLAN_INVALID");
        }
    }

    private async Task<SceneBuildResult> BuildAndPublishAsync(BuildFrozenPlanRequest request, FrozenConversionPlan plan, CadBuildInputSnapshot snapshot, BuildAllocation allocation, IProgress<SceneOperationProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(allocation.StagingPath);
        var outputs = new List<SceneBuildOutputResult>();
        var artifacts = new List<SceneArtifactDescriptor>();
        var configuration = plan.BuildConfiguration!;
        Report(progress, "BUILD_SCENE_DRAFT");
        var draftResult = _draftBuilder.Build(plan, snapshot);
        if (draftResult.Draft is null)
        {
            outputs.Add(Output(SceneBuildOutputKind.SceneDraft, SceneBuildOutputStatus.Failed, diagnostics: draftResult.Diagnostics));
            foreach (var kind in RequestedKinds(configuration.Outputs))
            {
                outputs.Add(Output(kind, SceneBuildOutputStatus.SkippedDependencyFailed, diagnostics: draftResult.Diagnostics));
            }
            AddNotRequestedOutputs(outputs, configuration.Outputs);
            return await PublishResultAsync(SceneOperationStatus.Failed, plan, snapshot, request, allocation, outputs, artifacts, draftResult.Diagnostics, cancellationToken);
        }

        var draftPath = Path.Combine(allocation.StagingPath, "scene-draft.json");
        await File.WriteAllTextAsync(draftPath, JsonSerializer.Serialize(draftResult.Draft, BuildReadyPlanJson.Options), Utf8, cancellationToken);
        outputs.Add(Output(SceneBuildOutputKind.SceneDraft, SceneBuildOutputStatus.Succeeded, allocation.Relative("scene-draft.json"), draftResult.Diagnostics));
        artifacts.Add(Artifact(SceneArtifactKind.SceneDraft, allocation.Relative("scene-draft.json")));

        var requiresBlender = configuration.Outputs.GenerateSingleGlb || configuration.Outputs.PublishScenePackageArtifact || configuration.Outputs.Generate3DTiles;
        if (requiresBlender && (string.IsNullOrWhiteSpace(request.BlenderExecutablePath) || _blender is null || _packageGenerator is null))
        {
            var diagnostic = Diagnostic("BUILD_BLENDER_NOT_CONFIGURED", DiagnosticSeverity.Error);
            foreach (var kind in RequestedKinds(configuration.Outputs)) outputs.Add(Output(kind, SceneBuildOutputStatus.NotConfigured, diagnostics: [diagnostic]));
            AddNotRequestedOutputs(outputs, configuration.Outputs);
            return await PublishResultAsync(SceneOperationStatus.NotConfigured, plan, snapshot, request, allocation, outputs, artifacts, [diagnostic], cancellationToken);
        }

        var tool = new BlenderToolOptions { ExecutablePath = request.BlenderExecutablePath!, Timeout = request.BlenderTimeout ?? TimeSpan.FromMinutes(10) };
        var assets = CreateAssets(configuration.Assets, snapshot, request.OutputRootDirectory);
        if (configuration.Outputs.GenerateSingleGlb)
        {
            Report(progress, "BUILD_SINGLE_GLB");
            var singleDirectory = Path.Combine(allocation.StagingPath, "single-glb");
            var generated = await _blender!.GenerateAsync(new BlenderGenerationRequest { Draft = draftResult.Draft, Tool = tool, OutputDirectory = singleDirectory, OutputFileName = "scene.glb", AssetGeneration = assets }, cancellationToken);
            var status = Map(generated.Status);
            var relative = status is SceneBuildOutputStatus.Succeeded or SceneBuildOutputStatus.PartiallySucceeded ? allocation.Relative("single-glb/scene.glb") : null;
            outputs.Add(Output(SceneBuildOutputKind.SingleGlb, status, relative, generated.Diagnostics));
            if (relative is not null) artifacts.Add(Artifact(SceneArtifactKind.Glb, relative));
        }

        ScenePackageBuildResult? package = null;
        if (configuration.Outputs.PublishScenePackageArtifact || configuration.Outputs.GenerateScenePackageAsDependency)
        {
            Report(progress, "BUILD_SCENE_PACKAGE");
            package = await _packageGenerator!.GenerateAsync(new ScenePackageBuildRequest { Draft = draftResult.Draft, OutputRootDirectory = allocation.StagingPath, BlenderTool = tool, AssetGeneration = assets, Partition = configuration.Partition }, cancellationToken);
            var packageRelative = package.Status is SceneBuildOutputStatus.Succeeded or SceneBuildOutputStatus.PartiallySucceeded ? allocation.Relative("scene-package/scene-package.json") : null;
            if (configuration.Outputs.PublishScenePackageArtifact)
            {
                outputs.Add(Output(SceneBuildOutputKind.ScenePackage, package.Status, packageRelative, package.Diagnostics));
                if (packageRelative is not null) artifacts.Add(Artifact(SceneArtifactKind.ScenePackage, packageRelative));
            }
        }

        if (configuration.Outputs.Generate3DTiles)
        {
            if (package?.PackageDirectory is null || package.Status is not (SceneBuildOutputStatus.Succeeded or SceneBuildOutputStatus.PartiallySucceeded))
            {
                outputs.Add(Output(SceneBuildOutputKind.ThreeDTiles, SceneBuildOutputStatus.SkippedDependencyFailed, diagnostics: package?.Diagnostics));
            }
            else if (_tilesetGenerator is null)
            {
                outputs.Add(Output(SceneBuildOutputKind.ThreeDTiles, SceneBuildOutputStatus.NotConfigured, diagnostics: [Diagnostic("BUILD_3D_TILES_NOT_CONFIGURED", DiagnosticSeverity.Error)]));
            }
            else
            {
                Report(progress, "BUILD_3D_TILES");
                var tiles = await _tilesetGenerator.GenerateAsync(new TilesetBuildRequest { ScenePackageDirectory = package.PackageDirectory, Configuration = configuration.ThreeDTiles }, cancellationToken);
                var relative = tiles.Status is SceneBuildOutputStatus.Succeeded or SceneBuildOutputStatus.PartiallySucceeded ? allocation.Relative("scene-package/tileset.json") : null;
                outputs.Add(Output(SceneBuildOutputKind.ThreeDTiles, tiles.Status, relative, tiles.Diagnostics));
                if (relative is not null) artifacts.Add(Artifact(SceneArtifactKind.Tileset, relative));
            }
        }

        var operationStatus = Aggregate(outputs);
        AddNotRequestedOutputs(outputs, configuration.Outputs);
        return await PublishResultAsync(operationStatus, plan, snapshot, request, allocation, outputs, artifacts, outputs.SelectMany(item => item.Diagnostics).ToArray(), cancellationToken);
    }

    private static async Task<SceneBuildResult> PublishResultAsync(SceneOperationStatus status, FrozenConversionPlan plan, CadBuildInputSnapshot snapshot, BuildFrozenPlanRequest request, BuildAllocation allocation, IReadOnlyList<SceneBuildOutputResult> outputs, IReadOnlyList<SceneArtifactDescriptor> artifacts, IReadOnlyList<SceneDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var resultArtifact = Artifact(SceneArtifactKind.Report, allocation.Relative("build-result.json"));
        var result = new SceneBuildResult { Status = status, BuildJobId = allocation.JobId, BuildContentId = ContentId(plan, snapshot, request.BlenderExecutablePath), FrozenPlanId = plan.FrozenPlanId, Outputs = outputs, Artifacts = artifacts.Append(resultArtifact).ToArray(), Diagnostics = diagnostics };
        await File.WriteAllTextAsync(Path.Combine(allocation.StagingPath, "build-result.json"), JsonSerializer.Serialize(result, BuildReadyPlanJson.Options), Utf8, cancellationToken);
        Directory.Move(allocation.StagingPath, allocation.FinalPath);
        return result;
    }

    private static BlenderAssetGenerationContext? CreateAssets(FrozenAssetConfiguration assets, CadBuildInputSnapshot snapshot, string root)
    {
        if (assets.Catalog.Count == 0) return null;
        var candidateSubjects = snapshot.AssetCandidates.ToDictionary(item => item.AssetCandidateId, item => item.ClassificationSubjectId, StringComparer.Ordinal);
        return new BlenderAssetGenerationContext
        {
            AssetRootDirectory = root,
            Policy = new BlenderAssetGenerationPolicy { MissingAssetBehavior = assets.MissingAssetBehavior },
            Configuration = new CadAssetConfiguration
            {
                Catalog = new CadAssetCatalog { ContractVersion = "1.0", Assets = assets.Catalog.Select(item => new CadAssetDefinition { AssetId = item.AssetId, Kind = item.Kind, RelativeGlbPath = item.ResourceRelativePath }).ToArray() },
                Bindings = new CadAssetBindingSet { ContractVersion = "1.0", Bindings = assets.Bindings.Select((item, index) => new CadAssetBinding { Id = $"frozen-binding-{index:D4}", Enabled = true, Priority = 0, Kind = item.Kind, AssetId = item.AssetId, Selector = new CadAssetBindingSelector { SemanticObjectId = $"semantic:{(item.Kind == CadAssetKind.StaticFacility ? "static-facility" : "dynamic-equipment")}:{candidateSubjects[item.AssetCandidateId]}" } }).ToArray() }
            }
        };
    }

    private static BuildAllocation Allocate(string root)
    {
        var builds = Path.Combine(root, "builds");
        Directory.CreateDirectory(builds);
        for (var number = 1; number < int.MaxValue; number++)
        {
            var jobId = $"build-{number:D4}";
            var final = Path.Combine(builds, jobId);
            var claim = Path.Combine(builds, $".{jobId}.claim");
            if (Directory.Exists(final)) continue;
            try
            {
                using (new FileStream(claim, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                if (Directory.Exists(final)) { TryDeleteFile(claim); continue; }
                return new BuildAllocation(jobId, final, Path.Combine(builds, $".staging-{jobId}-{Guid.NewGuid():N}"), claim);
            }
            catch (IOException) { }
        }
        throw new IOException("No build job identifier is available.");
    }

    private static string ContentId(FrozenConversionPlan plan, CadBuildInputSnapshot snapshot, string? blenderPath)
    {
        var version = "not-configured";
        if (!string.IsNullOrWhiteSpace(blenderPath))
        {
            try { version = FileVersionInfo.GetVersionInfo(blenderPath).FileVersion ?? "unknown"; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FileNotFoundException) { version = "unknown"; }
        }

        var configuration = plan.BuildConfiguration!;
        var assetHashes = configuration.Assets.Catalog.OrderBy(item => item.AssetId, StringComparer.Ordinal).Select(item => $"{item.AssetId}:{item.ContentHash}");
        var value = string.Join('|',
            "CORE04C_V2",
            $"plan={plan.FrozenPlanContentHash}",
            $"snapshot={snapshot.ContentHash}",
            $"rules={configuration.Classification.ContentHash}",
            $"assets={string.Join(',', assetHashes)}",
            "generators=scene-draft-v1,blender-glb-v1,scene-package-v1,tiles-v1",
            $"blender={version}");
        return "build-content-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static SceneOperationStatus Aggregate(IReadOnlyList<SceneBuildOutputResult> outputs)
    {
        if (outputs.Any(item => item.Status == SceneBuildOutputStatus.Cancelled)) return SceneOperationStatus.Cancelled;
        if (outputs.Any(item => item.Status == SceneBuildOutputStatus.NotConfigured)) return outputs.Any(item => item.Status is SceneBuildOutputStatus.Succeeded or SceneBuildOutputStatus.PartiallySucceeded) ? SceneOperationStatus.PartiallySucceeded : SceneOperationStatus.NotConfigured;
        var succeeded = outputs.Any(item => item.Status is SceneBuildOutputStatus.Succeeded or SceneBuildOutputStatus.PartiallySucceeded);
        var failed = outputs.Any(item => item.Status is SceneBuildOutputStatus.Failed or SceneBuildOutputStatus.SkippedDependencyFailed);
        return succeeded && failed ? SceneOperationStatus.PartiallySucceeded : failed ? SceneOperationStatus.Failed : outputs.Any(item => item.Status == SceneBuildOutputStatus.PartiallySucceeded) ? SceneOperationStatus.PartiallySucceeded : SceneOperationStatus.Succeeded;
    }

    private static IEnumerable<SceneBuildOutputKind> RequestedKinds(FrozenOutputConfiguration output)
    {
        if (output.GenerateSingleGlb) yield return SceneBuildOutputKind.SingleGlb;
        if (output.PublishScenePackageArtifact) yield return SceneBuildOutputKind.ScenePackage;
        if (output.Generate3DTiles) yield return SceneBuildOutputKind.ThreeDTiles;
    }

    private static void AddNotRequestedOutputs(ICollection<SceneBuildOutputResult> outputs, FrozenOutputConfiguration configuration)
    {
        var existing = outputs.Select(item => item.Kind).ToHashSet();
        foreach (var kind in Enum.GetValues<SceneBuildOutputKind>().Where(kind => kind is not SceneBuildOutputKind.SceneDraft && !existing.Contains(kind)))
        {
            outputs.Add(Output(kind, SceneBuildOutputStatus.NotRequested));
        }
    }

    private static SceneBuildOutputStatus Map(BlenderGenerationStatus status) => status switch { BlenderGenerationStatus.Succeeded => SceneBuildOutputStatus.Succeeded, BlenderGenerationStatus.PartiallySucceeded => SceneBuildOutputStatus.PartiallySucceeded, BlenderGenerationStatus.Cancelled => SceneBuildOutputStatus.Cancelled, _ => SceneBuildOutputStatus.Failed };
    private static SceneBuildOutputResult Output(SceneBuildOutputKind kind, SceneBuildOutputStatus status, string? path = null, IReadOnlyList<SceneDiagnostic>? diagnostics = null) => new() { Kind = kind, Status = status, ArtifactRelativePath = path, Diagnostics = diagnostics ?? Array.Empty<SceneDiagnostic>() };
    private static SceneArtifactDescriptor Artifact(SceneArtifactKind kind, string path) => new() { Kind = kind, RelativePath = path, IsValidated = true };

    private static bool TryFrozenPath(string candidate, string outputRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) || candidate.Contains("://", StringComparison.Ordinal) || candidate.StartsWith("\\\\", StringComparison.Ordinal)) return false;
        var normalized = Path.GetFullPath(candidate);
        var frozenRoot = Path.GetFullPath(Path.Combine(outputRoot, "plans", "frozen")) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(frozenRoot, StringComparison.OrdinalIgnoreCase) || !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(normalized)) return false;
        path = normalized;
        return true;
    }

    private static SceneBuildResult Failed(string code) => new() { Status = SceneOperationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Frozen plan build did not complete normally." };
    private static void Report(IProgress<SceneOperationProgress>? progress, string stageCode) => progress?.Report(new SceneOperationProgress { Phase = SceneWorkflowPhase.Build, StageCode = stageCode });
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private sealed record BuildAllocation(string JobId, string FinalPath, string StagingPath, string ClaimPath)
    {
        public string Relative(string path) => $"builds/{JobId}/{path.Replace('\\', '/')}";
    }
}
