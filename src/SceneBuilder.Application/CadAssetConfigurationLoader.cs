using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record CadAssetConfigurationLoadResult
{
    public CadAssetConfiguration? Configuration { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();

    public bool IsSuccess => Configuration is not null && Diagnostics.All(diagnostic => diagnostic.Severity is not DiagnosticSeverity.Error);
}

public sealed class CadAssetConfigurationLoader
{
    public const string ContractVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public CadAssetConfigurationLoadResult Load(string catalogJson, string bindingsJson)
    {
        if (string.IsNullOrWhiteSpace(catalogJson) || string.IsNullOrWhiteSpace(bindingsJson))
        {
            return Failed();
        }

        try
        {
            var catalogDto = JsonSerializer.Deserialize<CatalogDto>(catalogJson, SerializerOptions);
            var bindingsDto = JsonSerializer.Deserialize<BindingSetDto>(bindingsJson, SerializerOptions);
            if (catalogDto is null || bindingsDto is null || catalogDto.Assets is null || bindingsDto.Bindings is null)
            {
                return Failed();
            }

            var catalog = new CadAssetCatalog
            {
                ContractVersion = catalogDto.ContractVersion ?? string.Empty,
                Assets = catalogDto.Assets.Select(MapAsset).ToArray()
            };
            var bindings = new CadAssetBindingSet
            {
                ContractVersion = bindingsDto.ContractVersion ?? string.Empty,
                Bindings = bindingsDto.Bindings.Select(MapBinding).ToArray()
            };

            return IsValid(catalog, bindings)
                ? new CadAssetConfigurationLoadResult { Configuration = new CadAssetConfiguration { Catalog = catalog, Bindings = bindings } }
                : Failed();
        }
        catch (JsonException)
        {
            return Failed();
        }
        catch (ArgumentException)
        {
            return Failed();
        }
    }

    private static CadAssetDefinition MapAsset(AssetDto dto) => new()
    {
        AssetId = dto.AssetId ?? string.Empty,
        Kind = ParseKind(dto.Kind),
        RelativeGlbPath = dto.RelativeGlbPath ?? string.Empty
    };

    private static CadAssetBinding MapBinding(BindingDto dto) => new()
    {
        Id = dto.Id ?? string.Empty,
        Enabled = dto.Enabled ?? throw new ArgumentException("The enabled property is required."),
        Priority = dto.Priority ?? throw new ArgumentException("The priority property is required."),
        Kind = ParseKind(dto.Kind),
        Selector = new CadAssetBindingSelector
        {
            SemanticObjectId = dto.Selector?.SemanticObjectId,
            Block = dto.Selector?.Block
        },
        AssetId = dto.AssetId ?? string.Empty
    };

    private static CadAssetKind ParseKind(string? value) => value switch
    {
        "static-facility" => CadAssetKind.StaticFacility,
        "dynamic-equipment" => CadAssetKind.DynamicEquipment,
        _ => throw new ArgumentException("The asset kind text is not supported.")
    };

    private static bool IsValid(CadAssetCatalog catalog, CadAssetBindingSet bindings)
    {
        if (!string.Equals(catalog.ContractVersion, ContractVersion, StringComparison.Ordinal) ||
            !string.Equals(bindings.ContractVersion, ContractVersion, StringComparison.Ordinal))
        {
            return false;
        }

        var assetsById = new Dictionary<string, CadAssetDefinition>(StringComparer.Ordinal);
        foreach (var asset in catalog.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.AssetId) ||
                !IsSafeRelativeGlbPathSyntax(asset.RelativeGlbPath) ||
                !Enum.IsDefined(asset.Kind) ||
                !assetsById.TryAdd(asset.AssetId, asset))
            {
                return false;
            }
        }

        var bindingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings.Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Id) ||
                !bindingIds.Add(binding.Id) ||
                string.IsNullOrWhiteSpace(binding.AssetId) ||
                !Enum.IsDefined(binding.Kind) ||
                !HasSelector(binding.Selector) ||
                !assetsById.TryGetValue(binding.AssetId, out var asset) ||
                asset.Kind != binding.Kind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSelector(CadAssetBindingSelector selector) =>
        !string.IsNullOrWhiteSpace(selector.SemanticObjectId) || !string.IsNullOrWhiteSpace(selector.Block);

    private static bool IsSafeRelativeGlbPathSyntax(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
            !string.Equals(Path.GetExtension(value), ".glb", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

        return value.Split(['\\', '/'], StringSplitOptions.None)
            .All(segment => !string.IsNullOrWhiteSpace(segment) && segment is not "." and not "..");
    }

    private static CadAssetConfigurationLoadResult Failed() => new()
    {
        Diagnostics =
        [
            new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "ASSET_CONFIG_INVALID",
                Message = "The asset catalog or binding configuration is invalid."
            }
        ]
    };

    private sealed record CatalogDto
    {
        [JsonPropertyName("contractVersion")]
        public string? ContractVersion { get; init; }

        [JsonPropertyName("assets")]
        public IReadOnlyList<AssetDto>? Assets { get; init; }
    }

    private sealed record AssetDto
    {
        [JsonPropertyName("assetId")]
        public string? AssetId { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("relativeGlbPath")]
        public string? RelativeGlbPath { get; init; }
    }

    private sealed record BindingSetDto
    {
        [JsonPropertyName("contractVersion")]
        public string? ContractVersion { get; init; }

        [JsonPropertyName("bindings")]
        public IReadOnlyList<BindingDto>? Bindings { get; init; }
    }

    private sealed record BindingDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; init; }

        [JsonPropertyName("priority")]
        public int? Priority { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("selector")]
        public SelectorDto? Selector { get; init; }

        [JsonPropertyName("assetId")]
        public string? AssetId { get; init; }
    }

    private sealed record SelectorDto
    {
        [JsonPropertyName("semanticObjectId")]
        public string? SemanticObjectId { get; init; }

        [JsonPropertyName("block")]
        public string? Block { get; init; }
    }
}
