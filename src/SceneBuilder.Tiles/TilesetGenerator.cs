using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Blender;
using SceneBuilder.Domain;
using SceneBuilder.Pipeline;

namespace SceneBuilder.Tiles;

public sealed class TilesetGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly ScenePackageValidator _scenePackageValidator;
    private readonly BinaryGlbValidator _glbValidator;
    private readonly TilesetBoundingVolumeBuilder _boundsBuilder;
    private readonly ITilesetValidator _tilesetValidator;

    public TilesetGenerator(ScenePackageValidator? scenePackageValidator = null, BinaryGlbValidator? glbValidator = null, TilesetBoundingVolumeBuilder? boundsBuilder = null, ITilesetValidator? tilesetValidator = null)
    {
        _glbValidator = glbValidator ?? new BinaryGlbValidator();
        _scenePackageValidator = scenePackageValidator ?? new ScenePackageValidator(_glbValidator);
        _boundsBuilder = boundsBuilder ?? new TilesetBoundingVolumeBuilder();
        _tilesetValidator = tilesetValidator ?? new TilesetValidator(_scenePackageValidator, _glbValidator);
    }

    public async Task<TilesetGenerationResult> GenerateAsync(TilesetGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidPolicy(request.Policy)) return Failed("TILESET_POLICY_INVALID");
        if (string.IsNullOrWhiteSpace(request.ScenePackageDirectory) || !Path.IsPathFullyQualified(request.ScenePackageDirectory) || !Directory.Exists(request.ScenePackageDirectory)) return Failed("TILESET_INPUT_PACKAGE_INVALID");

        var packageDirectory = Path.GetFullPath(request.ScenePackageDirectory);
        var outputPath = Path.Combine(packageDirectory, "tileset.json");
        var temporaryPath = outputPath + ".staging-" + Guid.NewGuid().ToString("N");
        if (request.Policy.OverwriteExistingTileset || File.Exists(outputPath)) return Failed("TILESET_ALREADY_EXISTS");

        try
        {
            var package = await _scenePackageValidator.ValidateAsync(packageDirectory, cancellationToken);
            if (!package.IsValid || package.Index is null) return Failed("TILESET_INPUT_PACKAGE_INVALID");
            var partitions = package.Index.Partitions
                .Where(IsEligiblePartition)
                .OrderBy(partition => partition.Id is "partition-global")
                .ThenBy(partition => partition.XIndex)
                .ThenBy(partition => partition.YIndex)
                .ToArray();
            var excluded = package.Index.Partitions.Count - partitions.Length;
            if (partitions.Length == 0) return Failed("TILESET_NO_VALID_PARTITIONS");
            if (excluded > 0 && !request.Policy.AllowPartialScenePackage) return Failed("TILESET_PARTIAL");
            if (partitions.Any(partition => !IsValidArtifact(packageDirectory, partition.ArtifactPath!))) return Failed("TILESET_CONTENT_GLB_INVALID");

            var rootBounds = CombineBounds(partitions.Select(partition => partition.ContentBounds));
            var leaves = partitions.Select(partition => new TilesetTile
            {
                BoundingVolume = new TilesetBoundingVolume { Box = _boundsBuilder.CreateBox(partition.ContentBounds, request.Policy.MinimumBoundingHalfExtentMeters) },
                GeometricError = 0d,
                Content = new TilesetContent { Uri = partition.ArtifactPath! }
            }).ToArray();
            var document = new TilesetDocument
            {
                GeometricError = request.Policy.RootGeometricErrorMeters,
                Root = new TilesetTile
                {
                    BoundingVolume = new TilesetBoundingVolume { Box = _boundsBuilder.CreateBox(rootBounds, request.Policy.MinimumBoundingHalfExtentMeters) },
                    GeometricError = request.Policy.RootGeometricErrorMeters,
                    Refine = "ADD",
                    Children = leaves
                }
            };
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
            var validation = await _tilesetValidator.ValidateAsync(packageDirectory, temporaryPath, cancellationToken);
            if (!validation.IsValid) return Failed("TILESET_VALIDATION_FAILED");
            File.Move(temporaryPath, outputPath, overwrite: false);
            return new TilesetGenerationResult
            {
                Status = excluded > 0 ? TilesetGenerationStatus.PartiallySucceeded : TilesetGenerationStatus.Succeeded,
                TilesetPath = outputPath,
                TileCount = leaves.Length + 1,
                IncludedPartitionCount = leaves.Length,
                ExcludedPartitionCount = excluded
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new TilesetGenerationResult { Status = TilesetGenerationStatus.Cancelled, Diagnostics = [Diagnostic("TILESET_GENERATION_CANCELLED", DiagnosticSeverity.Warning)] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or JsonException)
        {
            return Failed("TILESET_PUBLICATION_FAILED");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static bool IsEligiblePartition(ScenePackagePartitionIndex partition) =>
        partition.Status is ScenePackagePartitionStatus.Succeeded &&
        IsSafeRelativeGlbUri(partition.ArtifactPath) &&
        partition.ContentBounds.State is CadBoundsState.Computed;

    internal static bool IsSafeRelativeGlbUri(string? value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains("..", StringComparison.Ordinal) && value.IndexOfAny(['\\']) < 0 && value.Split('/').All(segment => !string.IsNullOrWhiteSpace(segment)) && value.StartsWith("partitions/", StringComparison.Ordinal) && value.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    private bool IsValidArtifact(string packageDirectory, string uri)
    {
        var root = packageDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, uri));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && _glbValidator.Validate(path).IsValid;
    }

    private static CadBounds CombineBounds(IEnumerable<CadBounds> source)
    {
        var bounds = source.ToArray();
        return CadBounds.Computed(bounds.Min(item => item.MinX), bounds.Min(item => item.MinY), bounds.Min(item => item.MinZ), bounds.Max(item => item.MaxX), bounds.Max(item => item.MaxY), bounds.Max(item => item.MaxZ));
    }

    private static bool IsValidPolicy(TilesetGenerationPolicy policy) => policy is not null && double.IsFinite(policy.RootGeometricErrorMeters) && policy.RootGeometricErrorMeters > 0d && double.IsFinite(policy.MinimumBoundingHalfExtentMeters) && policy.MinimumBoundingHalfExtentMeters > 0d && !policy.OverwriteExistingTileset;
    private static TilesetGenerationResult Failed(string code) => new() { Status = TilesetGenerationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };
    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Tileset generation did not complete normally." };
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
