using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Blender;
using SceneBuilder.Domain;
using SceneBuilder.Pipeline;

namespace SceneBuilder.Tiles;

public sealed class TilesetValidator : ITilesetValidator
{
    private const double Tolerance = 1e-9d;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly ScenePackageValidator _scenePackageValidator;
    private readonly BinaryGlbValidator _glbValidator;

    public TilesetValidator(ScenePackageValidator? scenePackageValidator = null, BinaryGlbValidator? glbValidator = null)
    {
        _glbValidator = glbValidator ?? new BinaryGlbValidator();
        _scenePackageValidator = scenePackageValidator ?? new ScenePackageValidator(_glbValidator);
    }

    public async Task<TilesetValidationResult> ValidateAsync(string scenePackageDirectory, string tilesetPath, CancellationToken cancellationToken)
    {
        try
        {
            var package = await _scenePackageValidator.ValidateAsync(scenePackageDirectory, cancellationToken);
            if (!package.IsValid || package.Index is null || !File.Exists(tilesetPath)) return Failed("TILESET_INPUT_PACKAGE_INVALID");
            var json = await File.ReadAllTextAsync(tilesetPath, cancellationToken);
            using var source = JsonDocument.Parse(json);
            if (!HasRequiredDocumentFields(source.RootElement)) return Failed("TILESET_JSON_INVALID");
            var document = JsonSerializer.Deserialize<TilesetDocument>(json, JsonOptions);
            if (document is null || !IsValidDocument(document)) return Failed("TILESET_JSON_INVALID");
            var leaves = document.Root.Children!;
            var eligiblePartitions = package.Index.Partitions.Where(TilesetGenerator.IsEligiblePartition).ToArray();
            var expectedUris = eligiblePartitions.Select(partition => partition.ArtifactPath!).OrderBy(uri => uri, StringComparer.Ordinal).ToArray();
            var actualUris = leaves.Select(leaf => leaf.Content!.Uri).OrderBy(uri => uri, StringComparer.Ordinal).ToArray();
            var contentBoundsByUri = eligiblePartitions
                .ToDictionary(partition => partition.ArtifactPath!, partition => partition.ContentBounds, StringComparer.Ordinal);
            if (!expectedUris.SequenceEqual(actualUris, StringComparer.Ordinal) ||
                leaves.Any(leaf => !Contains(document.Root.BoundingVolume.Box, leaf.BoundingVolume.Box) || !Contains(leaf.BoundingVolume.Box, contentBoundsByUri[leaf.Content!.Uri]))) return Failed("TILESET_VALIDATION_FAILED");
            var root = Path.GetFullPath(scenePackageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var uri in actualUris)
            {
                var path = Path.GetFullPath(Path.Combine(root, uri));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !_glbValidator.Validate(path).IsValid) return Failed("TILESET_CONTENT_GLB_INVALID");
            }

            return new TilesetValidationResult { IsValid = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Failed("TILESET_JSON_INVALID");
        }
    }

    private static bool IsValidDocument(TilesetDocument document) => document.Asset.Version is "1.1" && double.IsFinite(document.GeometricError) && document.GeometricError > 0d &&
        document.Root.Content is null && document.Root.Refine is "ADD" && document.Root.GeometricError == document.GeometricError && IsBox(document.Root.BoundingVolume.Box) &&
        document.Root.Children is { Count: > 0 } && document.Root.Children.All(leaf => leaf.Refine is null && leaf.Children is null && leaf.Content is not null && TilesetGenerator.IsSafeRelativeGlbUri(leaf.Content.Uri) && leaf.GeometricError == 0d && IsBox(leaf.BoundingVolume.Box)) &&
        document.Root.Children.Select(leaf => leaf.Content!.Uri).Distinct(StringComparer.Ordinal).Count() == document.Root.Children.Count;

    private static bool IsBox(IReadOnlyList<double> box) => box.Count == 12 && box.All(double.IsFinite) && box[3] > 0d && box[7] > 0d && box[11] > 0d &&
        box[4] == 0d && box[5] == 0d && box[6] == 0d && box[8] == 0d && box[9] == 0d && box[10] == 0d;

    private static bool Contains(IReadOnlyList<double> root, IReadOnlyList<double> child) =>
        Math.Abs(child[0] - root[0]) + child[3] <= root[3] + Tolerance &&
        Math.Abs(child[1] - root[1]) + child[7] <= root[7] + Tolerance &&
        Math.Abs(child[2] - root[2]) + child[11] <= root[11] + Tolerance;

    private static bool Contains(IReadOnlyList<double> box, CadBounds bounds) => bounds.State is CadBoundsState.Computed &&
        bounds.MinX >= box[0] - box[3] - Tolerance && bounds.MaxX <= box[0] + box[3] + Tolerance &&
        bounds.MinY >= box[1] - box[7] - Tolerance && bounds.MaxY <= box[1] + box[7] + Tolerance &&
        bounds.MinZ >= box[2] - box[11] - Tolerance && bounds.MaxZ <= box[2] + box[11] + Tolerance;

    private static bool HasRequiredDocumentFields(JsonElement document)
    {
        if (document.ValueKind is not JsonValueKind.Object || !HasProperties(document, "asset", "geometricError", "root") ||
            !IsObjectWithProperties(document.GetProperty("asset"), "version") || !IsRoot(document.GetProperty("root")))
        {
            return false;
        }

        return document.GetProperty("root").GetProperty("children").EnumerateArray().All(IsLeaf);
    }

    private static bool IsRoot(JsonElement root) => root.ValueKind is JsonValueKind.Object &&
        HasProperties(root, "boundingVolume", "geometricError", "refine", "children") &&
        IsBoundingVolume(root.GetProperty("boundingVolume")) && root.GetProperty("children").ValueKind is JsonValueKind.Array;

    private static bool IsLeaf(JsonElement leaf) => leaf.ValueKind is JsonValueKind.Object && !leaf.TryGetProperty("children", out _) && !leaf.TryGetProperty("refine", out _) &&
        HasProperties(leaf, "boundingVolume", "geometricError", "content") &&
        IsBoundingVolume(leaf.GetProperty("boundingVolume")) && IsObjectWithProperties(leaf.GetProperty("content"), "uri");

    private static bool IsBoundingVolume(JsonElement volume) => IsObjectWithProperties(volume, "box") && volume.GetProperty("box").ValueKind is JsonValueKind.Array;

    private static bool IsObjectWithProperties(JsonElement element, params string[] properties) => element.ValueKind is JsonValueKind.Object && HasProperties(element, properties);

    private static bool HasProperties(JsonElement element, params string[] properties) => properties.All(property => element.TryGetProperty(property, out _));

    private static TilesetValidationResult Failed(string code) => new() { IsValid = false, Diagnostics = [new SceneDiagnostic { Code = code, Severity = DiagnosticSeverity.Error, Message = "Tileset validation did not complete normally." }] };
}
