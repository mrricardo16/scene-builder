using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Application;
using SceneBuilder.Blender;
using SceneBuilder.Domain;

namespace SceneBuilder.Pipeline;

public sealed class ScenePackageGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly IBlenderSceneGenerator _blender;
    private readonly BinaryGlbValidator _validator;
    private readonly ScenePackageValidator _packageValidator;
    private readonly ScenePartitionPlanner _planner;
    private readonly ScenePartitionDraftFactory _draftFactory;

    public ScenePackageGenerator(IBlenderSceneGenerator blender, BinaryGlbValidator? validator = null, ScenePartitionPlanner? planner = null, ScenePartitionDraftFactory? draftFactory = null, ScenePackageValidator? packageValidator = null)
    {
        _blender = blender ?? throw new ArgumentNullException(nameof(blender));
        _validator = validator ?? new BinaryGlbValidator();
        _packageValidator = packageValidator ?? new ScenePackageValidator(_validator);
        _planner = planner ?? new ScenePartitionPlanner();
        _draftFactory = draftFactory ?? new ScenePartitionDraftFactory();
    }

    public async Task<ScenePackageGenerationResult> GenerateAsync(ScenePackageGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetPaths(request, out var finalPath, out var stagingPath)) return Failed("SCENE_PACKAGE_PUBLICATION_FAILED");
        var planning = _planner.Plan(request.Draft, request.PartitionPolicy);
        if (planning.Plan is null) return new ScenePackageGenerationResult { Status = ScenePackageGenerationStatus.Failed, Diagnostics = planning.Diagnostics };
        if (planning.Plan.Partitions.Count == 0) return new ScenePackageGenerationResult { Status = ScenePackageGenerationStatus.Failed, Diagnostics = planning.Diagnostics.Append(Diagnostic("SCENE_PACKAGE_PARTIAL", DiagnosticSeverity.Error)).ToArray() };

        var partitionResults = new List<ScenePackagePartitionResult>();
        try
        {
            Directory.CreateDirectory(stagingPath);
            var partitionDirectory = Path.Combine(stagingPath, "partitions");
            Directory.CreateDirectory(partitionDirectory);
            foreach (var partition in planning.Plan.Partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = "partitions/" + partition.Id + ".glb";
                var outputPath = Path.Combine(stagingPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var generated = await _blender.GenerateAsync(new BlenderGenerationRequest
                {
                    Draft = _draftFactory.Create(request.Draft, partition),
                    Tool = request.BlenderTool,
                    AssetGeneration = request.AssetGeneration,
                    OutputDirectory = Path.GetDirectoryName(outputPath)!,
                    OutputFileName = Path.GetFileName(outputPath),
                    AllowOverwrite = false
                }, cancellationToken);
                var status = ToPartitionStatus(generated.Status);
                if (status is ScenePackagePartitionStatus.Succeeded && _validator.Validate(outputPath).IsValid)
                {
                    partitionResults.Add(new ScenePackagePartitionResult { PartitionId = partition.Id, Status = status, ArtifactPath = relativePath, Diagnostics = generated.Diagnostics });
                    continue;
                }

                partitionResults.Add(new ScenePackagePartitionResult { PartitionId = partition.Id, Status = status is ScenePackagePartitionStatus.Succeeded ? ScenePackagePartitionStatus.Failed : status, Diagnostics = generated.Diagnostics });
                if (!request.PublicationPolicy.ContinueAfterPartitionFailure) break;
            }

            var failed = partitionResults.Any(item => item.Status is not ScenePackagePartitionStatus.Succeeded);
            if (failed && !request.PublicationPolicy.PublishPartialPackage) return FailedWithPartitions(partitionResults, "SCENE_PACKAGE_PARTIAL");
            if (partitionResults.Count != planning.Plan.Partitions.Count && !request.PublicationPolicy.PublishPartialPackage) return FailedWithPartitions(partitionResults, "SCENE_PACKAGE_PARTIAL");
            if (failed && partitionResults.All(item => item.Status is not ScenePackagePartitionStatus.Succeeded)) return FailedWithPartitions(partitionResults, "SCENE_PACKAGE_PARTIAL");

            var index = BuildIndex(request.Draft, planning.Plan, partitionResults);
            if (!ScenePackageValidator.IsValidIndex(index)) return FailedWithPartitions(partitionResults, "SCENE_PACKAGE_INDEX_INVALID");
            await File.WriteAllTextAsync(Path.Combine(stagingPath, "scene-package.json"), JsonSerializer.Serialize(index, JsonOptions), cancellationToken);
            if (!(await _packageValidator.ValidateAsync(stagingPath, cancellationToken)).IsValid) return FailedWithPartitions(partitionResults, "SCENE_PACKAGE_INDEX_INVALID");
            Directory.Move(stagingPath, finalPath);
            var packageStatus = failed || planning.Status is ScenePartitionPlanStatus.PartiallySucceeded
                ? ScenePackageGenerationStatus.PartiallySucceeded
                : ScenePackageGenerationStatus.Succeeded;
            return new ScenePackageGenerationResult { Status = packageStatus, PackagePath = finalPath, Index = index, Partitions = partitionResults, Diagnostics = planning.Diagnostics };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ScenePackageGenerationResult { Status = ScenePackageGenerationStatus.Cancelled, Partitions = partitionResults, Diagnostics = [Diagnostic("PARTITION_GENERATION_FAILED", DiagnosticSeverity.Warning)] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FailedWithPartitions(partitionResults, "SCENE_PACKAGE_PUBLICATION_FAILED");
        }
        finally
        {
            TryDelete(stagingPath);
        }
    }

    private static ScenePackageIndex BuildIndex(SceneDraft draft, ScenePartitionPlan plan, IReadOnlyList<ScenePackagePartitionResult> results)
    {
        var byId = results.ToDictionary(item => item.PartitionId, StringComparer.Ordinal);
        var nodeBySemantic = draft.Nodes.ToDictionary(item => item.SemanticObjectId, StringComparer.Ordinal);
        var partitions = plan.Partitions.Where(partition => byId.TryGetValue(partition.Id, out var result) && result.Status is ScenePackagePartitionStatus.Succeeded).Select(partition =>
        {
            var result = byId[partition.Id];
            var nodes = partition.SemanticObjectIds.Select(id => nodeBySemantic[id]).ToArray();
            return new ScenePackagePartitionIndex
            {
                Id = partition.Id, Status = result.Status, XIndex = partition.XIndex, YIndex = partition.YIndex, CellBounds = partition.CellBounds, ContentBounds = partition.ContentBounds,
                ArtifactPath = result.Status is ScenePackagePartitionStatus.Succeeded ? result.ArtifactPath : null,
                ProceduralCount = nodes.Count(node => node.ContentKind is SceneNodeContentKind.ProceduralStaticGeometry),
                StaticAssetCount = nodes.Count(node => node.ContentKind is SceneNodeContentKind.StaticAssetReference),
                DynamicAssetCount = nodes.Count(node => node.ContentKind is SceneNodeContentKind.DynamicAssetReference)
            };
        }).ToArray();
        var dynamicNodes = plan.Assignments
            .Where(assignment => byId.TryGetValue(assignment.OwnerPartitionId, out var result) && result.Status is ScenePackagePartitionStatus.Succeeded && nodeBySemantic[assignment.SemanticObjectId].ContentKind is SceneNodeContentKind.DynamicAssetReference)
            .OrderBy(assignment => assignment.SemanticObjectId, StringComparer.Ordinal)
            .Select(assignment =>
            {
                var transform = nodeBySemantic[assignment.SemanticObjectId].Transform!;
                return new ScenePackageDynamicNodeIndex { SemanticObjectId = assignment.SemanticObjectId, PartitionId = assignment.OwnerPartitionId, Position = transform.Position, RotationDegrees = transform.RotationDegrees, Scale = transform.Scale };
            }).ToArray();
        return new ScenePackageIndex { SceneBounds = plan.SceneBounds, Partitions = partitions, DynamicNodes = dynamicNodes };
    }

    private static bool TryGetPaths(ScenePackageGenerationRequest request, out string finalPath, out string stagingPath)
    {
        finalPath = stagingPath = string.Empty;
        if (string.IsNullOrWhiteSpace(request.OutputRootDirectory) || !IsSafePackageName(request.PackageName) || request.PublicationPolicy.OverwriteExistingPackage) return false;
        try
        {
            var root = Path.GetFullPath(request.OutputRootDirectory);
            Directory.CreateDirectory(root);
            finalPath = Path.GetFullPath(Path.Combine(root, request.PackageName));
            if (!finalPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(finalPath)) return false;
            stagingPath = Path.Combine(root, "." + request.PackageName + ".staging-" + Guid.NewGuid().ToString("N"));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsSafePackageName(string value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains("..", StringComparison.Ordinal) && value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    private static ScenePackagePartitionStatus ToPartitionStatus(BlenderGenerationStatus status) => status switch { BlenderGenerationStatus.Succeeded => ScenePackagePartitionStatus.Succeeded, BlenderGenerationStatus.TimedOut => ScenePackagePartitionStatus.TimedOut, BlenderGenerationStatus.Cancelled => ScenePackagePartitionStatus.Cancelled, _ => ScenePackagePartitionStatus.Failed };
    private static ScenePackageGenerationResult Failed(string code) => new() { Status = ScenePackageGenerationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static ScenePackageGenerationResult FailedWithPartitions(IReadOnlyList<ScenePackagePartitionResult> partitions, string code) => new() { Status = ScenePackageGenerationStatus.Failed, Partitions = partitions, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Scene package generation did not complete normally." };
    private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
