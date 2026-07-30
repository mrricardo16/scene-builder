using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Blender;
using SceneBuilder.Domain;

namespace SceneBuilder.Pipeline;

public sealed record ScenePackageValidationResult
{
    public bool IsValid { get; init; }
    public ScenePackageIndex? Index { get; init; }
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed class ScenePackageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly BinaryGlbValidator _glbValidator;

    public ScenePackageValidator(BinaryGlbValidator? glbValidator = null)
    {
        _glbValidator = glbValidator ?? new BinaryGlbValidator();
    }

    public async Task<ScenePackageValidationResult> ValidateAsync(string packagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !Directory.Exists(packagePath))
        {
            return Failed("SCENE_PACKAGE_INDEX_INVALID");
        }

        ScenePackageIndex? index;
        try
        {
            var indexPath = Path.Combine(packagePath, "scene-package.json");
            var json = await File.ReadAllTextAsync(indexPath, cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!HasRequiredIndexFields(document.RootElement))
            {
                return Failed("SCENE_PACKAGE_INDEX_INVALID");
            }
            index = JsonSerializer.Deserialize<ScenePackageIndex>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failed("SCENE_PACKAGE_INDEX_INVALID");
        }

        if (index is null || !IsValidIndex(index))
        {
            return Failed("SCENE_PACKAGE_INDEX_INVALID");
        }

        var packageRoot = Path.GetFullPath(packagePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var partition in index.Partitions.Where(item => item.Status is ScenePackagePartitionStatus.Succeeded))
        {
            var artifactPath = Path.GetFullPath(Path.Combine(packageRoot, partition.ArtifactPath!));
            if (!artifactPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) || !_glbValidator.Validate(artifactPath).IsValid)
            {
                return Failed("SCENE_PACKAGE_ARTIFACT_INVALID");
            }
        }

        return new ScenePackageValidationResult { IsValid = true, Index = index };
    }

    internal static bool IsValidIndex(ScenePackageIndex index) => index.ContractVersion == "1.0" && index.Unit == "meters" &&
        index.Partitions.Count > 0 &&
        index.Partitions.All(item => !string.IsNullOrWhiteSpace(item.Id) && Enum.IsDefined(item.Status) && item.Status is ScenePackagePartitionStatus.Succeeded) &&
        index.Partitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == index.Partitions.Count &&
        index.Partitions.Select(item => item.ArtifactPath).Distinct(StringComparer.Ordinal).Count() == index.Partitions.Count &&
        index.Partitions.All(item => IsSafeRelativeArtifactPath(item.ArtifactPath) && item.ProceduralCount >= 0 && item.StaticAssetCount >= 0 && item.DynamicAssetCount >= 0) &&
        index.Partitions.All(item => item.Id is "partition-global" ? item.XIndex is null && item.YIndex is null : item.XIndex is not null && item.YIndex is not null) &&
        index.DynamicNodes.All(item => !string.IsNullOrWhiteSpace(item.SemanticObjectId) && !string.IsNullOrWhiteSpace(item.PartitionId)) &&
        index.DynamicNodes.Select(item => item.SemanticObjectId).Distinct(StringComparer.Ordinal).Count() == index.DynamicNodes.Count &&
        index.DynamicNodes.All(item => index.Partitions.Any(partition => partition.Id == item.PartitionId));

    internal static bool IsSafeRelativeArtifactPath(string? value) => !string.IsNullOrWhiteSpace(value) &&
        !Path.IsPathRooted(value) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value.IndexOfAny(['\\']) < 0 &&
        value.StartsWith("partitions/", StringComparison.Ordinal) &&
        value.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    private static bool HasRequiredIndexFields(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object || !HasProperties(root, "contractVersion", "unit", "sceneBounds", "partitions", "dynamicNodes") || !IsBounds(root.GetProperty("sceneBounds")) ||
            root.GetProperty("partitions").ValueKind is not JsonValueKind.Array || root.GetProperty("dynamicNodes").ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        foreach (var partition in root.GetProperty("partitions").EnumerateArray())
        {
            if (partition.ValueKind is not JsonValueKind.Object || !HasProperties(partition, "id", "status", "xIndex", "yIndex", "cellBounds", "contentBounds", "artifactPath", "proceduralCount", "staticAssetCount", "dynamicAssetCount") ||
                !IsBounds(partition.GetProperty("cellBounds")) || !IsBounds(partition.GetProperty("contentBounds")))
            {
                return false;
            }
        }

        foreach (var dynamicNode in root.GetProperty("dynamicNodes").EnumerateArray())
        {
            if (dynamicNode.ValueKind is not JsonValueKind.Object || !HasProperties(dynamicNode, "semanticObjectId", "partitionId", "position", "rotationDegrees", "scale") ||
                !IsPoint(dynamicNode.GetProperty("position")) || !IsPoint(dynamicNode.GetProperty("scale")) || !IsFiniteNumber(dynamicNode.GetProperty("rotationDegrees")))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasProperties(JsonElement element, params string[] names) => names.All(name => element.TryGetProperty(name, out _));

    private static bool IsBounds(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object || !HasProperties(element, "MinX", "MinY", "MinZ", "MaxX", "MaxY", "MaxZ", "State") ||
            !IsFiniteNumber(element.GetProperty("MinX")) || !IsFiniteNumber(element.GetProperty("MinY")) || !IsFiniteNumber(element.GetProperty("MinZ")) ||
            !IsFiniteNumber(element.GetProperty("MaxX")) || !IsFiniteNumber(element.GetProperty("MaxY")) || !IsFiniteNumber(element.GetProperty("MaxZ")) ||
            element.GetProperty("State").ValueKind is not JsonValueKind.Number || !element.GetProperty("State").TryGetInt32(out var state) || !Enum.IsDefined((CadBoundsState)state))
        {
            return false;
        }

        if (state is (int)CadBoundsState.Computed)
        {
            return element.GetProperty("MinX").GetDouble() <= element.GetProperty("MaxX").GetDouble() &&
                element.GetProperty("MinY").GetDouble() <= element.GetProperty("MaxY").GetDouble() &&
                element.GetProperty("MinZ").GetDouble() <= element.GetProperty("MaxZ").GetDouble();
        }

        return element.GetProperty("MinX").GetDouble() is 0 && element.GetProperty("MinY").GetDouble() is 0 && element.GetProperty("MinZ").GetDouble() is 0 &&
            element.GetProperty("MaxX").GetDouble() is 0 && element.GetProperty("MaxY").GetDouble() is 0 && element.GetProperty("MaxZ").GetDouble() is 0;
    }

    private static bool IsPoint(JsonElement element) => element.ValueKind is JsonValueKind.Object &&
        HasProperties(element, "X", "Y", "Z") && IsFiniteNumber(element.GetProperty("X")) && IsFiniteNumber(element.GetProperty("Y")) && IsFiniteNumber(element.GetProperty("Z"));

    private static bool IsFiniteNumber(JsonElement element) => element.ValueKind is JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new CadBoundsJsonConverter());
        return options;
    }

    private sealed class CadBoundsJsonConverter : JsonConverter<CadBounds>
    {
        public override CadBounds Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var element = document.RootElement;
            var state = (CadBoundsState)element.GetProperty("State").GetInt32();
            return state switch
            {
                CadBoundsState.NotEvaluated => CadBounds.NotEvaluated,
                CadBoundsState.Empty => CadBounds.Empty,
                CadBoundsState.Computed => CadBounds.Computed(
                    element.GetProperty("MinX").GetDouble(),
                    element.GetProperty("MinY").GetDouble(),
                    element.GetProperty("MinZ").GetDouble(),
                    element.GetProperty("MaxX").GetDouble(),
                    element.GetProperty("MaxY").GetDouble(),
                    element.GetProperty("MaxZ").GetDouble()),
                _ => throw new JsonException("Unsupported CadBounds state.")
            };
        }

        public override void Write(Utf8JsonWriter writer, CadBounds value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("MinX", value.MinX);
            writer.WriteNumber("MinY", value.MinY);
            writer.WriteNumber("MinZ", value.MinZ);
            writer.WriteNumber("MaxX", value.MaxX);
            writer.WriteNumber("MaxY", value.MaxY);
            writer.WriteNumber("MaxZ", value.MaxZ);
            writer.WriteNumber("State", (int)value.State);
            writer.WriteEndObject();
        }
    }

    private static ScenePackageValidationResult Failed(string code) => new()
    {
        IsValid = false,
        Diagnostics = [new SceneDiagnostic { Code = code, Severity = DiagnosticSeverity.Error, Message = "Scene package validation did not complete normally." }]
    };
}
