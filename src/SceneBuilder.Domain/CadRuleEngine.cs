namespace SceneBuilder.Domain;

public sealed class CadRuleSetValidator
{
    public bool TryValidate(CadRuleSet ruleSet, out IReadOnlyList<SceneDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        var errors = new List<SceneDiagnostic>();
        if (!string.Equals(ruleSet.ContractVersion, "1.0", StringComparison.Ordinal) || ruleSet.Rules.Count == 0)
        {
            errors.Add(ConfigDiagnostic());
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in ruleSet.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !identifiers.Add(rule.Id) || rule.Match is null ||
                !Enum.IsDefined(rule.Classification) || rule.Classification is CadSemanticClassification.Unclassified ||
                !IsValidMatch(rule.Match) || !IsValidDefaults(rule.GeometryDefaults))
            {
                errors.Add(ConfigDiagnostic());
                break;
            }
        }

        diagnostics = errors.ToArray();
        return errors.Count == 0;
    }

    private static bool IsValidMatch(CadRuleMatch match)
    {
        if (string.IsNullOrWhiteSpace(match.Layer) && string.IsNullOrWhiteSpace(match.Block) && match.EntityTypes.Count == 0)
        {
            return false;
        }

        if ((match.Layer is not null && string.IsNullOrWhiteSpace(match.Layer)) ||
            (match.Block is not null && string.IsNullOrWhiteSpace(match.Block)) ||
            match.EntityTypes.Any(string.IsNullOrWhiteSpace) ||
            match.EntityTypes.Distinct(StringComparer.Ordinal).Count() != match.EntityTypes.Count)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidDefaults(CadRuleGeometryDefaults? defaults) =>
        defaults?.HeightMeters is not double height || (double.IsFinite(height) && height >= 0);

    public static SceneDiagnostic ConfigDiagnostic() => new()
    {
        Severity = DiagnosticSeverity.Error,
        Code = "RULE_CONFIG_INVALID",
        Message = "The CAD rule configuration is invalid."
    };
}

public sealed class CadClassificationSubjectBuilder
{
    public IReadOnlyList<CadClassificationSubject> Build(CadClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var subjects = new List<CadClassificationSubject>();
        subjects.AddRange(input.Contours.Contours.Select(CreateContourSubject));
        subjects.AddRange(input.Contours.OpenSegments.Select(segment => new CadClassificationSubject
        {
            Id = segment.Id,
            Kind = CadClassificationSubjectKind.OpenSegment,
            SourceLayer = segment.SourceLayer,
            SourceEntityType = segment.SourceEntityType,
            Bounds = segment.Bounds
        }));
        subjects.AddRange(input.Geometry.Entities.OfType<CadInsertGeometry>().Select(insert => new CadClassificationSubject
        {
            Id = CadClassificationSubjectIdentity.ForInsert(insert.SourceOrder),
            Kind = CadClassificationSubjectKind.Insert,
            SourceLayer = insert.LayerName,
            SourceEntityType = "INSERT",
            BlockName = insert.BlockName,
            Bounds = insert.Bounds
        }));
        return subjects.OrderBy(subject => subject.Id, StringComparer.Ordinal).ToArray();
    }

    private static CadClassificationSubject CreateContourSubject(CadContour contour)
    {
        return contour switch
        {
            CadCircleContour circle => new CadClassificationSubject
            {
                Id = circle.Id,
                Kind = CadClassificationSubjectKind.Contour,
                SourceLayer = circle.SourceLayer,
                SourceEntityType = "CIRCLE",
                Bounds = circle.Bounds,
                IsEligibleForClassification = circle.ValidationState is CadContourValidationState.Valid
            },
            CadSegmentContour segment => CreateSegmentContourSubject(segment),
            _ => new CadClassificationSubject { Id = contour.Id, Kind = CadClassificationSubjectKind.Contour, Bounds = contour.Bounds, IsEligibleForClassification = false }
        };
    }

    private static CadClassificationSubject CreateSegmentContourSubject(CadSegmentContour contour)
    {
        var first = contour.Segments.FirstOrDefault();
        var isUniform = first is not null && contour.Segments.All(segment =>
            string.Equals(segment.SourceLayer, first.SourceLayer, StringComparison.Ordinal) &&
            string.Equals(segment.SourceEntityType, first.SourceEntityType, StringComparison.Ordinal));
        return new CadClassificationSubject
        {
            Id = contour.Id,
            Kind = CadClassificationSubjectKind.Contour,
            SourceLayer = isUniform ? first!.SourceLayer : string.Empty,
            SourceEntityType = isUniform ? first!.SourceEntityType : string.Empty,
            Bounds = contour.Bounds,
            IsEligibleForClassification = isUniform && contour.ValidationState is CadContourValidationState.Valid
        };
    }
}

public sealed class CadRuleEngine
{
    private readonly CadRuleSetValidator _validator;
    private readonly CadClassificationSubjectBuilder _subjectBuilder;

    public CadRuleEngine(CadRuleSetValidator? validator = null, CadClassificationSubjectBuilder? subjectBuilder = null)
    {
        _validator = validator ?? new CadRuleSetValidator();
        _subjectBuilder = subjectBuilder ?? new CadClassificationSubjectBuilder();
    }

    public CadClassificationResult Classify(CadClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Classify(input.RuleSet, _subjectBuilder.Build(input));
    }

    public CadClassificationResult Classify(CadRuleSet ruleSet, IReadOnlyList<CadClassificationSubject> subjects)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(subjects);
        if (!_validator.TryValidate(ruleSet, out var validationDiagnostics))
        {
            return new CadClassificationResult { Status = CadClassificationStatus.Failed, Diagnostics = validationDiagnostics };
        }

        var objects = subjects.OrderBy(subject => subject.Id, StringComparer.Ordinal)
            .Select(subject => ClassifySubject(subject, ruleSet.Rules))
            .ToArray();
        var hasConflicts = objects.Any(item => item.Diagnostics.Any(diagnostic => diagnostic.Code == "RULE_CONFLICT"));
        return new CadClassificationResult
        {
            Status = hasConflicts ? CadClassificationStatus.PartiallySucceeded : CadClassificationStatus.Succeeded,
            Objects = objects,
            Diagnostics = objects.SelectMany(item => item.Diagnostics).OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal).ToArray()
        };
    }

    private static CadObjectClassification ClassifySubject(CadClassificationSubject subject, IReadOnlyList<CadClassificationRule> rules)
    {
        if (!subject.IsEligibleForClassification)
        {
            return new CadObjectClassification
            {
                Subject = subject,
                Diagnostics = [Diagnostic("RULE_SUBJECT_INVALID", DiagnosticSeverity.Warning, "A CAD subject is not eligible for rule classification.")]
            };
        }

        var candidates = rules.Where(rule => rule.Enabled)
            .Select(rule => new Candidate(rule, GetMatchRank(rule, subject)))
            .Where(candidate => candidate.Rank > 0)
            .OrderByDescending(candidate => candidate.Rank)
            .ThenByDescending(candidate => candidate.Rule.Priority)
            .ThenBy(candidate => candidate.Rule.Id, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new CadObjectClassification { Subject = subject };
        }

        var winnerGroup = candidates.Where(candidate => candidate.Rank == candidates[0].Rank && candidate.Rule.Priority == candidates[0].Rule.Priority).ToArray();
        var candidateIds = winnerGroup.Select(candidate => candidate.Rule.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (winnerGroup.Select(candidate => candidate.Rule.Classification).Distinct().Skip(1).Any())
        {
            return new CadObjectClassification
            {
                Subject = subject,
                MatchRank = winnerGroup[0].Rank,
                CandidateRuleIds = candidateIds,
                Diagnostics = [Diagnostic("RULE_CONFLICT", DiagnosticSeverity.Error, "Rules with equal rank and priority classify a subject differently.")]
            };
        }

        var winner = winnerGroup.OrderBy(candidate => candidate.Rule.Id, StringComparer.Ordinal).First();
        return new CadObjectClassification
        {
            Subject = subject,
            Classification = winner.Rule.Classification,
            MatchedRuleId = winner.Rule.Id,
            MatchRank = winner.Rank,
            Priority = winner.Rule.Priority,
            GeometryDefaults = winner.Rule.GeometryDefaults,
            CandidateRuleIds = candidateIds,
            Diagnostics = winnerGroup.Length > 1
                ? [Diagnostic("RULE_DUPLICATE_MATCH", DiagnosticSeverity.Information, "Equivalent rules selected a stable rule identifier.")]
                : Array.Empty<SceneDiagnostic>()
        };
    }

    private static int GetMatchRank(CadClassificationRule rule, CadClassificationSubject subject)
    {
        var match = rule.Match;
        if (!MatchesEntityType(match.EntityTypes, subject.SourceEntityType) ||
            !MatchesOptionalPattern(match.Layer, subject.SourceLayer) ||
            !MatchesBlock(match.Block, subject))
        {
            return 0;
        }

        var hasLayer = match.Layer is not null;
        var hasBlock = match.Block is not null;
        var layerExact = hasLayer && IsExact(match.Layer!);
        var blockExact = hasBlock && IsExact(match.Block!);
        return (hasLayer, hasBlock, layerExact, blockExact) switch
        {
            (true, true, true, true) => 600,
            (true, true, false, true) => 500,
            (false, true, _, true) => 490,
            (true, true, true, false) => 400,
            (true, false, true, _) => 390,
            (true, true, false, false) => 300,
            (false, true, _, false) => 290,
            (true, false, false, _) => 200,
            (false, false, _, _) when match.EntityTypes.Count > 0 => 100,
            _ => 0
        };
    }

    private static bool MatchesEntityType(IReadOnlyList<string> entityTypes, string entityType) =>
        entityTypes.Count == 0 || entityTypes.Any(type => string.Equals(type, entityType, StringComparison.Ordinal));

    private static bool MatchesOptionalPattern(string? pattern, string value) =>
        pattern is null || CadRuleWildcardMatcher.IsMatch(pattern, value);

    private static bool MatchesBlock(string? pattern, CadClassificationSubject subject) =>
        pattern is null || (subject.Kind is CadClassificationSubjectKind.Insert && subject.BlockName is not null && CadRuleWildcardMatcher.IsMatch(pattern, subject.BlockName));

    private static bool IsExact(string pattern) => pattern.IndexOfAny(['*', '?']) < 0;

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity, string message) =>
        new() { Code = code, Severity = severity, Message = message };

    private sealed record Candidate(CadClassificationRule Rule, int Rank);
}

public static class CadRuleWildcardMatcher
{
    public static bool IsMatch(string pattern, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(value);
        var patternIndex = 0;
        var valueIndex = 0;
        var starIndex = -1;
        var retryValueIndex = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] is '?' || EqualsOrdinalIgnoreCase(pattern[patternIndex], value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] is '*')
            {
                starIndex = patternIndex++;
                retryValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] is '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool EqualsOrdinalIgnoreCase(char left, char right) =>
        string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
}
