using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public enum CadAssetResolutionStatus
{
    Resolved = 0,
    Unmapped = 1,
    Conflict = 2
}

public sealed record CadAssetResolution
{
    public string SemanticObjectId { get; init; } = string.Empty;

    public CadAssetKind Kind { get; init; }

    public CadAssetResolutionStatus Status { get; init; }

    public CadAssetDefinition? Asset { get; init; }

    public string? BindingId { get; init; }
}

public sealed record CadAssetBindingResolutionResult
{
    public IReadOnlyList<CadAssetResolution> Resolutions { get; init; } = Array.Empty<CadAssetResolution>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed class CadAssetBindingResolver
{
    public CadAssetBindingResolutionResult Resolve(
        IEnumerable<CadSemanticObject> semanticObjects,
        CadAssetConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(semanticObjects);
        ArgumentNullException.ThrowIfNull(configuration);

        var assetsById = configuration.Catalog.Assets.ToDictionary(asset => asset.AssetId, StringComparer.Ordinal);
        var resolutions = new List<CadAssetResolution>();
        var diagnostics = new List<SceneDiagnostic>();

        foreach (var semanticObject in semanticObjects.OrderBy(semanticObject => semanticObject.Id, StringComparer.Ordinal))
        {
            if (!TryGetAssetSubject(semanticObject, out var kind, out var blockName))
            {
                continue;
            }

            var matches = configuration.Bindings.Bindings
                .Where(binding => binding.Enabled && binding.Kind == kind && assetsById.TryGetValue(binding.AssetId, out var asset) && asset.Kind == kind)
                .Select(binding => new Candidate(binding, assetsById[binding.AssetId], GetMatchRank(binding.Selector, semanticObject.Id, blockName)))
                .Where(candidate => candidate.MatchRank > 0)
                .ToArray();

            if (matches.Length == 0)
            {
                resolutions.Add(Unmapped(semanticObject.Id, kind));
                continue;
            }

            var highestRank = matches.Max(candidate => candidate.MatchRank);
            var highestPriority = matches
                .Where(candidate => candidate.MatchRank == highestRank)
                .Max(candidate => candidate.Binding.Priority);
            var tiedMatches = matches
                .Where(candidate => candidate.MatchRank == highestRank && candidate.Binding.Priority == highestPriority)
                .OrderBy(candidate => candidate.Binding.Id, StringComparer.Ordinal)
                .ToArray();
            var assetIds = tiedMatches.Select(candidate => candidate.Asset.AssetId).Distinct(StringComparer.Ordinal).ToArray();

            if (assetIds.Length > 1)
            {
                resolutions.Add(new CadAssetResolution
                {
                    SemanticObjectId = semanticObject.Id,
                    Kind = kind,
                    Status = CadAssetResolutionStatus.Conflict
                });
                diagnostics.Add(Diagnostic("ASSET_BINDING_CONFLICT", "Asset binding resolution found conflicting explicit mappings."));
                continue;
            }

            var selected = tiedMatches[0];
            resolutions.Add(new CadAssetResolution
            {
                SemanticObjectId = semanticObject.Id,
                Kind = kind,
                Status = CadAssetResolutionStatus.Resolved,
                Asset = selected.Asset,
                BindingId = selected.Binding.Id
            });

            if (tiedMatches.Length > 1)
            {
                diagnostics.Add(Diagnostic("ASSET_BINDING_DUPLICATE_MATCH", "Asset binding resolution found duplicate explicit mappings."));
            }
        }

        return new CadAssetBindingResolutionResult
        {
            Resolutions = resolutions,
            Diagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static CadAssetResolution Unmapped(string semanticObjectId, CadAssetKind kind) => new()
    {
        SemanticObjectId = semanticObjectId,
        Kind = kind,
        Status = CadAssetResolutionStatus.Unmapped
    };

    private static bool TryGetAssetSubject(CadSemanticObject semanticObject, out CadAssetKind kind, out string blockName)
    {
        switch (semanticObject)
        {
            case CadStaticFacilityObject facility:
                kind = CadAssetKind.StaticFacility;
                blockName = facility.BlockName;
                return true;
            case CadDynamicEquipmentObject equipment:
                kind = CadAssetKind.DynamicEquipment;
                blockName = equipment.BlockName;
                return true;
            default:
                kind = default;
                blockName = string.Empty;
                return false;
        }
    }

    private static int GetMatchRank(CadAssetBindingSelector selector, string semanticObjectId, string blockName)
    {
        var hasSemanticSelector = !string.IsNullOrWhiteSpace(selector.SemanticObjectId);
        var hasBlockSelector = !string.IsNullOrWhiteSpace(selector.Block);
        if ((hasSemanticSelector && !string.Equals(selector.SemanticObjectId, semanticObjectId, StringComparison.OrdinalIgnoreCase)) ||
            (hasBlockSelector && !WildcardMatch(selector.Block!, blockName)))
        {
            return 0;
        }

        if (hasSemanticSelector)
        {
            return 300;
        }

        return selector.Block!.IndexOfAny(['*', '?']) < 0 ? 200 : 100;
    }

    private static bool WildcardMatch(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var starIndex = -1;
        var restartValueIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                restartValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++restartValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static SceneDiagnostic Diagnostic(string code, string message) => new()
    {
        Severity = DiagnosticSeverity.Warning,
        Code = code,
        Message = message
    };

    private sealed record Candidate(CadAssetBinding Binding, CadAssetDefinition Asset, int MatchRank);
}
