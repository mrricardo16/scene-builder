namespace SceneBuilder.Application;

public enum CadAssetKind
{
    StaticFacility = 0,
    DynamicEquipment = 1
}

public enum MissingAssetBehavior
{
    Skip = 0,
    Placeholder = 1,
    Fail = 2
}

public sealed record CadAssetCatalog
{
    public string ContractVersion { get; init; } = string.Empty;

    public IReadOnlyList<CadAssetDefinition> Assets { get; init; } = Array.Empty<CadAssetDefinition>();
}

public sealed record CadAssetConfiguration
{
    public CadAssetCatalog Catalog { get; init; } = new();

    public CadAssetBindingSet Bindings { get; init; } = new();
}

public sealed record CadAssetDefinition
{
    public string AssetId { get; init; } = string.Empty;

    public CadAssetKind Kind { get; init; }

    public string RelativeGlbPath { get; init; } = string.Empty;
}

public sealed record CadAssetBindingSet
{
    public string ContractVersion { get; init; } = string.Empty;

    public IReadOnlyList<CadAssetBinding> Bindings { get; init; } = Array.Empty<CadAssetBinding>();
}

public sealed record CadAssetBinding
{
    public string Id { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public int Priority { get; init; }

    public CadAssetKind Kind { get; init; }

    public CadAssetBindingSelector Selector { get; init; } = new();

    public string AssetId { get; init; } = string.Empty;
}

public sealed record CadAssetBindingSelector
{
    public string? SemanticObjectId { get; init; }

    public string? Block { get; init; }
}

public sealed record BlenderAssetGenerationPolicy
{
    public MissingAssetBehavior MissingAssetBehavior { get; init; } = MissingAssetBehavior.Skip;
}
