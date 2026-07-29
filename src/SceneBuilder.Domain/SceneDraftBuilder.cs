namespace SceneBuilder.Domain;

public sealed class SceneDraftBuilder
{
    private readonly CadClassificationSubjectBuilder _subjectBuilder;

    public SceneDraftBuilder(CadClassificationSubjectBuilder? subjectBuilder = null)
    {
        _subjectBuilder = subjectBuilder ?? new CadClassificationSubjectBuilder();
    }

    public SceneDraftBuildResult Build(SceneDraftBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DraftId) || request.Classification.Status is CadClassificationStatus.Failed)
        {
            return Failed("SCENE_DRAFT_INPUT_INVALID", "The SceneDraft core input is invalid.");
        }

        if (!SceneSourceIndex.TryCreate(request, _subjectBuilder, out var sourceIndex, out var inputDiagnostics))
        {
            return new SceneDraftBuildResult
            {
                Status = SceneDraftBuildStatus.Failed,
                Diagnostics = SortDiagnostics(inputDiagnostics)
            };
        }

        var duplicateSubjects = request.Classification.Objects
            .GroupBy(item => item.Subject.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var semanticObjects = new List<CadSemanticObject>();
        var nodes = new List<SceneNode>();
        var diagnostics = new List<SceneDiagnostic>();
        var skippedSubjectIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var duplicateSubjectId in duplicateSubjects)
        {
            skippedSubjectIds.Add(duplicateSubjectId);
            diagnostics.Add(Diagnostic(
                "SCENE_DUPLICATE_SUBJECT_RESULT",
                DiagnosticSeverity.Error,
                "Classification contains duplicate subject results."));
        }

        foreach (var classification in request.Classification.Objects
                     .Where(item => item.Classification is not CadSemanticClassification.Unclassified)
                     .OrderBy(item => item.Subject.Id, StringComparer.Ordinal))
        {
            if (duplicateSubjects.Contains(classification.Subject.Id))
            {
                continue;
            }

            if (!sourceIndex.TryGet(classification.Subject.Id, out var source))
            {
                Skip(classification.Subject.Id, "SCENE_SOURCE_SUBJECT_NOT_FOUND", "A classification subject does not resolve to source geometry.", skippedSubjectIds, diagnostics);
                continue;
            }

            if (classification.Subject != source.Subject || !classification.Subject.IsEligibleForClassification ||
                !HasMatchingEvidence(classification) || !HasValidGeometryDefaults(classification.GeometryDefaults))
            {
                Skip(classification.Subject.Id, "SCENE_CLASSIFICATION_SOURCE_MISMATCH", "Classification evidence does not match trusted source geometry.", skippedSubjectIds, diagnostics);
                continue;
            }

            if (source.Subject.Bounds.State is not CadBoundsState.Computed)
            {
                Skip(classification.Subject.Id, "SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE", "A semantic source has no computed bounds.", skippedSubjectIds, diagnostics);
                continue;
            }

            if (!IsCompatible(classification.Classification, source.Subject.Kind))
            {
                Skip(classification.Subject.Id, "SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE", "A semantic classification is incompatible with its source kind.", skippedSubjectIds, diagnostics);
                continue;
            }

            var semanticResult = CreateSemanticObject(classification, source);
            if (semanticResult.SemanticObject is null)
            {
                Skip(classification.Subject.Id, semanticResult.DiagnosticCode!, semanticResult.DiagnosticMessage!, skippedSubjectIds, diagnostics);
                continue;
            }

            semanticObjects.Add(semanticResult.SemanticObject);
            diagnostics.AddRange(semanticResult.Diagnostics);
            if (semanticResult.Diagnostics.Count > 0)
            {
                skippedSubjectIds.Remove(classification.Subject.Id);
            }

            nodes.Add(CreateNode(semanticResult.SemanticObject));
        }

        if (HasDuplicates(semanticObjects.Select(item => item.Id)) || HasDuplicates(nodes.Select(item => item.Id)))
        {
            return Failed("SCENE_DRAFT_INPUT_INVALID", "The SceneDraft output contains duplicate stable identifiers.");
        }

        var orderedSemanticObjects = semanticObjects.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var orderedNodes = nodes.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var orderedSkippedSubjectIds = skippedSubjectIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var orderedDiagnostics = SortDiagnostics(diagnostics);
        var status = orderedSkippedSubjectIds.Length > 0 || orderedDiagnostics.Any(diagnostic => diagnostic.Severity is not DiagnosticSeverity.Information)
            ? SceneDraftBuildStatus.PartiallySucceeded
            : SceneDraftBuildStatus.Succeeded;

        return new SceneDraftBuildResult
        {
            Status = status,
            Draft = new SceneDraft
            {
                Id = request.DraftId,
                SourceDocument = request.SourceDocument,
                SemanticObjects = orderedSemanticObjects,
                Nodes = orderedNodes,
                Diagnostics = orderedDiagnostics
            },
            SkippedSubjectIds = orderedSkippedSubjectIds,
            Diagnostics = orderedDiagnostics
        };
    }

    private static bool HasMatchingEvidence(CadObjectClassification classification) =>
        !string.IsNullOrWhiteSpace(classification.MatchedRuleId) &&
        classification.MatchRank > 0 &&
        classification.Priority is not null;

    private static bool HasValidGeometryDefaults(CadRuleGeometryDefaults? geometryDefaults) =>
        geometryDefaults?.HeightMeters is not double height || double.IsFinite(height);

    private static bool IsCompatible(CadSemanticClassification classification, CadClassificationSubjectKind subjectKind) =>
        (classification, subjectKind) switch
        {
            (CadSemanticClassification.Wall, CadClassificationSubjectKind.Contour or CadClassificationSubjectKind.OpenSegment) => true,
            (CadSemanticClassification.Floor or CadSemanticClassification.Column, CadClassificationSubjectKind.Contour) => true,
            (CadSemanticClassification.Road, CadClassificationSubjectKind.Contour or CadClassificationSubjectKind.OpenSegment) => true,
            (CadSemanticClassification.StaticFacility or CadSemanticClassification.DynamicEquipment, CadClassificationSubjectKind.Insert) => true,
            _ => false
        };

    private static SemanticCreationResult CreateSemanticObject(CadObjectClassification classification, SceneSourceReference source)
    {
        var id = $"semantic:{ToIdPart(classification.Classification)}:{classification.Subject.Id}";
        var heightResult = GetHeight(classification, classification.Classification);
        try
        {
            return classification.Classification switch
            {
                CadSemanticClassification.Wall when source.Contour is not null => new SemanticCreationResult(
                    new CadWallObject(id, classification.Subject.Id, classification.Subject.Kind, source.Contour.Bounds, classification.GeometryDefaults, source.Contour, null, heightResult.HeightMeters),
                    heightResult.Diagnostics),
                CadSemanticClassification.Wall when source.OpenSegment is not null => new SemanticCreationResult(
                    new CadWallObject(id, classification.Subject.Id, classification.Subject.Kind, source.OpenSegment.Bounds, classification.GeometryDefaults, null, source.OpenSegment, heightResult.HeightMeters),
                    heightResult.Diagnostics),
                CadSemanticClassification.Floor when source.Contour is not null => new SemanticCreationResult(
                    new CadFloorObject(id, classification.Subject.Id, classification.Subject.Kind, source.Contour.Bounds, classification.GeometryDefaults, source.Contour),
                    Array.Empty<SceneDiagnostic>()),
                CadSemanticClassification.Column when source.Contour is not null => new SemanticCreationResult(
                    new CadColumnObject(id, classification.Subject.Id, classification.Subject.Kind, source.Contour.Bounds, classification.GeometryDefaults, source.Contour, heightResult.HeightMeters),
                    heightResult.Diagnostics),
                CadSemanticClassification.Road when source.Contour is not null => new SemanticCreationResult(
                    new CadRoadObject(id, classification.Subject.Id, classification.Subject.Kind, source.Contour.Bounds, classification.GeometryDefaults, source.Contour, null),
                    Array.Empty<SceneDiagnostic>()),
                CadSemanticClassification.Road when source.OpenSegment is not null => new SemanticCreationResult(
                    new CadRoadObject(id, classification.Subject.Id, classification.Subject.Kind, source.OpenSegment.Bounds, classification.GeometryDefaults, null, source.OpenSegment),
                    Array.Empty<SceneDiagnostic>()),
                CadSemanticClassification.StaticFacility when source.Insert is not null => new SemanticCreationResult(
                    new CadStaticFacilityObject(id, classification.Subject.Id, source.Insert.Bounds, classification.GeometryDefaults, source.Insert.BlockName, source.Insert.Position, source.Insert.RotationDegrees, source.Insert.Scale),
                    Array.Empty<SceneDiagnostic>()),
                CadSemanticClassification.DynamicEquipment when source.Insert is not null => new SemanticCreationResult(
                    new CadDynamicEquipmentObject(id, classification.Subject.Id, source.Insert.Bounds, classification.GeometryDefaults, source.Insert.BlockName, source.Insert.Position, source.Insert.RotationDegrees, source.Insert.Scale),
                    Array.Empty<SceneDiagnostic>()),
                _ => new SemanticCreationResult("SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE", "A semantic classification is incompatible with its source geometry.")
            };
        }
        catch (ArgumentException)
        {
            return new SemanticCreationResult("SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE", "A semantic source geometry does not meet the required invariant.");
        }
    }

    private static HeightResult GetHeight(CadObjectClassification classification, CadSemanticClassification semanticClassification)
    {
        if (semanticClassification is not (CadSemanticClassification.Wall or CadSemanticClassification.Column))
        {
            return new HeightResult(null, Array.Empty<SceneDiagnostic>());
        }

        var heightMeters = classification.GeometryDefaults?.HeightMeters;
        if (heightMeters is double height && double.IsFinite(height) && height > 0)
        {
            return new HeightResult(height, Array.Empty<SceneDiagnostic>());
        }

        return new HeightResult(null,
        [
            Diagnostic(
                "SCENE_GEOMETRY_DEFAULT_MISSING",
                DiagnosticSeverity.Warning,
                "A semantic object has no usable configured height.")
        ]);
    }

    private static SceneNode CreateNode(CadSemanticObject semanticObject)
    {
        var (contentKind, transform) = semanticObject switch
        {
            CadStaticFacilityObject facility => (
                SceneNodeContentKind.StaticAssetReference,
                new SceneNodeTransform(facility.Position, facility.RotationDegrees, facility.Scale)),
            CadDynamicEquipmentObject equipment => (
                SceneNodeContentKind.DynamicAssetReference,
                new SceneNodeTransform(equipment.Position, equipment.RotationDegrees, equipment.Scale)),
            _ => (SceneNodeContentKind.ProceduralStaticGeometry, null)
        };

        return new SceneNode
        {
            Id = $"node:{semanticObject.Id}",
            Name = $"{DisplayName(semanticObject.Classification)} {semanticObject.SourceSubjectId}",
            Bounds = semanticObject.Bounds,
            SourceLayers = Array.Empty<string>(),
            SemanticObjectId = semanticObject.Id,
            Classification = semanticObject.Classification,
            ContentKind = contentKind,
            SourceSubjectId = semanticObject.SourceSubjectId,
            SourceSubjectKind = semanticObject.SourceSubjectKind,
            GeometryDefaults = semanticObject.GeometryDefaults,
            Transform = transform
        };
    }

    private static void Skip(
        string subjectId,
        string code,
        string message,
        ISet<string> skippedSubjectIds,
        ICollection<SceneDiagnostic> diagnostics)
    {
        skippedSubjectIds.Add(subjectId);
        diagnostics.Add(Diagnostic(code, DiagnosticSeverity.Error, message));
    }

    private static bool HasDuplicates(IEnumerable<string> identifiers) =>
        identifiers.GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static SceneDraftBuildResult Failed(string code, string message) =>
        new()
        {
            Status = SceneDraftBuildStatus.Failed,
            Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error, message)]
        };

    private static IReadOnlyList<SceneDiagnostic> SortDiagnostics(IEnumerable<SceneDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity, string message) =>
        new() { Code = code, Severity = severity, Message = message };

    private static string ToIdPart(CadSemanticClassification classification) => classification switch
    {
        CadSemanticClassification.StaticFacility => "static-facility",
        CadSemanticClassification.DynamicEquipment => "dynamic-equipment",
        _ => classification.ToString().ToLowerInvariant()
    };

    private static string DisplayName(CadSemanticClassification classification) => classification switch
    {
        CadSemanticClassification.StaticFacility => "Static Facility",
        CadSemanticClassification.DynamicEquipment => "Dynamic Equipment",
        _ => classification.ToString()
    };

    private sealed record HeightResult(double? HeightMeters, IReadOnlyList<SceneDiagnostic> Diagnostics);

    private sealed record SemanticCreationResult
    {
        public SemanticCreationResult(CadSemanticObject semanticObject, IReadOnlyList<SceneDiagnostic> diagnostics)
        {
            SemanticObject = semanticObject;
            Diagnostics = diagnostics;
        }

        public SemanticCreationResult(string diagnosticCode, string diagnosticMessage)
        {
            DiagnosticCode = diagnosticCode;
            DiagnosticMessage = diagnosticMessage;
        }

        public CadSemanticObject? SemanticObject { get; }

        public IReadOnlyList<SceneDiagnostic> Diagnostics { get; } = Array.Empty<SceneDiagnostic>();

        public string? DiagnosticCode { get; }

        public string? DiagnosticMessage { get; }
    }
}

internal sealed class SceneSourceIndex
{
    private readonly IReadOnlyDictionary<string, SceneSourceReference> _sources;

    private SceneSourceIndex(IReadOnlyDictionary<string, SceneSourceReference> sources)
    {
        _sources = sources;
    }

    public bool TryGet(string subjectId, out SceneSourceReference source) => _sources.TryGetValue(subjectId, out source!);

    public static bool TryCreate(
        SceneDraftBuildRequest request,
        CadClassificationSubjectBuilder subjectBuilder,
        out SceneSourceIndex sourceIndex,
        out IReadOnlyList<SceneDiagnostic> diagnostics)
    {
        var errors = new List<SceneDiagnostic>();
        var contourItems = request.Contours.Contours.ToArray();
        var openSegmentItems = request.Contours.OpenSegments.ToArray();
        var insertItems = request.Geometry.Entities.OfType<CadInsertGeometry>().ToArray();
        if (HasDuplicateIdentifiers(contourItems.Select(contour => contour.Id)) ||
            HasDuplicateIdentifiers(openSegmentItems.Select(segment => segment.Id)) ||
            HasDuplicateIdentifiers(insertItems.Select(insert => CadClassificationSubjectIdentity.ForInsert(insert.SourceOrder))))
        {
            errors.Add(new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "SCENE_DRAFT_INPUT_INVALID",
                Message = "Trusted source geometry contains duplicate stable identifiers."
            });
        }

        if (errors.Count > 0)
        {
            sourceIndex = null!;
            diagnostics = errors;
            return false;
        }

        var contours = contourItems.ToDictionary(contour => contour.Id, StringComparer.Ordinal);
        var openSegments = openSegmentItems.ToDictionary(segment => segment.Id, StringComparer.Ordinal);
        var inserts = insertItems.ToDictionary(insert => CadClassificationSubjectIdentity.ForInsert(insert.SourceOrder), StringComparer.Ordinal);
        var subjects = subjectBuilder.Build(new CadClassificationInput
        {
            Summary = request.SourceDocument,
            Geometry = request.Geometry,
            Contours = request.Contours
        });
        if (subjects.GroupBy(subject => subject.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            errors.Add(new SceneDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "SCENE_DRAFT_INPUT_INVALID",
                Message = "Trusted source subjects contain duplicate stable identifiers."
            });
        }

        if (errors.Count > 0)
        {
            sourceIndex = null!;
            diagnostics = errors;
            return false;
        }

        var sources = new Dictionary<string, SceneSourceReference>(StringComparer.Ordinal);
        foreach (var subject in subjects)
        {
            sources.Add(subject.Id, subject.Kind switch
            {
                CadClassificationSubjectKind.Contour => new SceneSourceReference(subject, contours[subject.Id], null, null),
                CadClassificationSubjectKind.OpenSegment => new SceneSourceReference(subject, null, openSegments[subject.Id], null),
                CadClassificationSubjectKind.Insert => new SceneSourceReference(subject, null, null, inserts[subject.Id]),
                _ => throw new ArgumentOutOfRangeException(nameof(subject.Kind))
            });
        }

        sourceIndex = new SceneSourceIndex(sources);
        diagnostics = Array.Empty<SceneDiagnostic>();
        return true;
    }

    private static bool HasDuplicateIdentifiers(IEnumerable<string> identifiers) =>
        identifiers.GroupBy(identifier => identifier, StringComparer.Ordinal).Any(group => group.Count() > 1);
}

internal sealed record SceneSourceReference(
    CadClassificationSubject Subject,
    CadContour? Contour,
    CadCurveSegment2? OpenSegment,
    CadInsertGeometry? Insert);
