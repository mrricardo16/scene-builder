using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed record CadRuleSetLoadResult
{
    public CadRuleSet? RuleSet { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();

    public bool IsSuccess => RuleSet is not null && Diagnostics.All(diagnostic => diagnostic.Severity is not DiagnosticSeverity.Error);
}

public sealed class CadRuleSetJsonLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly CadRuleSetValidator _validator;

    public CadRuleSetJsonLoader(CadRuleSetValidator? validator = null)
    {
        _validator = validator ?? new CadRuleSetValidator();
    }

    public CadRuleSetLoadResult Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failed();
        }

        try
        {
            var dto = JsonSerializer.Deserialize<RuleSetDto>(json, SerializerOptions);
            if (dto is null || dto.Rules is null)
            {
                return Failed();
            }

            var ruleSet = new CadRuleSet
            {
                ContractVersion = dto.ContractVersion ?? string.Empty,
                Rules = dto.Rules.Select(MapRule).ToArray()
            };
            return _validator.TryValidate(ruleSet, out var diagnostics)
                ? new CadRuleSetLoadResult { RuleSet = ruleSet }
                : new CadRuleSetLoadResult { Diagnostics = diagnostics };
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

    private static CadClassificationRule MapRule(RuleDto dto) => new()
    {
        Id = dto.Id ?? string.Empty,
        Enabled = dto.Enabled ?? throw new ArgumentException("The enabled property is required."),
        Priority = dto.Priority ?? throw new ArgumentException("The priority property is required."),
        Classification = ParseClassification(dto.Classification),
        Match = new CadRuleMatch
        {
            Layer = dto.Match?.Layer,
            Block = dto.Match?.Block,
            EntityTypes = dto.Match?.EntityTypes?.Select(entityType => entityType?.ToUpperInvariant() ?? string.Empty).ToArray() ?? Array.Empty<string>()
        },
        GeometryDefaults = dto.GeometryDefaults is null ? null : new CadRuleGeometryDefaults { HeightMeters = dto.GeometryDefaults.HeightMeters }
    };

    private static CadSemanticClassification ParseClassification(string? value) => value switch
    {
        "wall" => CadSemanticClassification.Wall,
        "column" => CadSemanticClassification.Column,
        "floor" => CadSemanticClassification.Floor,
        "road" => CadSemanticClassification.Road,
        "static-facility" => CadSemanticClassification.StaticFacility,
        "dynamic-equipment" => CadSemanticClassification.DynamicEquipment,
        "unclassified" => CadSemanticClassification.Unclassified,
        _ => throw new ArgumentException("The classification text is not supported.")
    };

    private static CadRuleSetLoadResult Failed() => new()
    {
        Diagnostics = [CadRuleSetValidator.ConfigDiagnostic()]
    };

    private sealed record RuleSetDto
    {
        [JsonPropertyName("contractVersion")]
        public string? ContractVersion { get; init; }

        [JsonPropertyName("rules")]
        public IReadOnlyList<RuleDto>? Rules { get; init; }
    }

    private sealed record RuleDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; init; }

        [JsonPropertyName("priority")]
        public int? Priority { get; init; }

        [JsonPropertyName("match")]
        public MatchDto? Match { get; init; }

        [JsonPropertyName("classification")]
        public string? Classification { get; init; }

        [JsonPropertyName("geometryDefaults")]
        public GeometryDefaultsDto? GeometryDefaults { get; init; }
    }

    private sealed record MatchDto
    {
        [JsonPropertyName("layer")]
        public string? Layer { get; init; }

        [JsonPropertyName("block")]
        public string? Block { get; init; }

        [JsonPropertyName("entityTypes")]
        public IReadOnlyList<string?>? EntityTypes { get; init; }
    }

    private sealed record GeometryDefaultsDto
    {
        [JsonPropertyName("heightMeters")]
        public double? HeightMeters { get; init; }
    }
}
