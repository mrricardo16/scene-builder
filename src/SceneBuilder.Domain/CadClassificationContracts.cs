namespace SceneBuilder.Domain;

public enum CadSemanticClassification
{
    Unclassified = 0,
    Wall = 1,
    Column = 2,
    Floor = 3,
    Road = 4,
    StaticFacility = 5,
    DynamicEquipment = 6
}

public enum CadClassificationSubjectKind
{
    Contour = 0,
    OpenSegment = 1,
    Insert = 2
}

public enum CadClassificationStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2
}

public static class CadClassificationSubjectIdentity
{
    public static string ForInsert(int sourceOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrder);
        return $"insert:{sourceOrder:D6}";
    }
}

public sealed record CadRuleGeometryDefaults
{
    public double? HeightMeters { get; init; }
}

public sealed record CadRuleMatch
{
    public string? Layer { get; init; }

    public string? Block { get; init; }

    public IReadOnlyList<string> EntityTypes { get; init; } = Array.Empty<string>();
}

public sealed record CadClassificationRule
{
    public string Id { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public CadRuleMatch Match { get; init; } = new();

    public CadSemanticClassification Classification { get; init; }

    public int Priority { get; init; }

    public CadRuleGeometryDefaults? GeometryDefaults { get; init; }
}

public sealed record CadRuleSet
{
    public string ContractVersion { get; init; } = string.Empty;

    public IReadOnlyList<CadClassificationRule> Rules { get; init; } = Array.Empty<CadClassificationRule>();
}

public sealed record CadClassificationSubject
{
    public string Id { get; init; } = string.Empty;

    public CadClassificationSubjectKind Kind { get; init; }

    public string SourceLayer { get; init; } = string.Empty;

    public string SourceEntityType { get; init; } = string.Empty;

    public string? BlockName { get; init; }

    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;

    public bool IsEligibleForClassification { get; init; } = true;
}

public sealed record CadClassificationInput
{
    public CadDocumentModel Summary { get; init; } = new();

    public NormalizedCadGeometryDocument Geometry { get; init; } = new();

    public CadContourDocument Contours { get; init; } = new();

    public CadRuleSet RuleSet { get; init; } = new();
}

public sealed record CadObjectClassification
{
    public CadClassificationSubject Subject { get; init; } = new();

    public CadSemanticClassification Classification { get; init; } = CadSemanticClassification.Unclassified;

    public string? MatchedRuleId { get; init; }

    public int MatchRank { get; init; }

    public int? Priority { get; init; }

    public CadRuleGeometryDefaults? GeometryDefaults { get; init; }

    public IReadOnlyList<string> CandidateRuleIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record CadClassificationResult
{
    public CadClassificationStatus Status { get; init; } = CadClassificationStatus.Failed;

    public IReadOnlyList<CadObjectClassification> Objects { get; init; } = Array.Empty<CadObjectClassification>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}
