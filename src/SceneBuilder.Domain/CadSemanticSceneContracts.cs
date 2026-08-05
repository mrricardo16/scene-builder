using System.Text.Json.Serialization;

namespace SceneBuilder.Domain;

public enum CadWallGeometryKind
{
    ClosedProfile = 0,
    Baseline = 1
}

public enum CadRoadGeometryKind
{
    Area = 0,
    Centerline = 1
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CadWallObject), "wall")]
[JsonDerivedType(typeof(CadFloorObject), "floor")]
[JsonDerivedType(typeof(CadColumnObject), "column")]
[JsonDerivedType(typeof(CadRoadObject), "road")]
[JsonDerivedType(typeof(CadStaticFacilityObject), "static-facility")]
[JsonDerivedType(typeof(CadDynamicEquipmentObject), "dynamic-equipment")]
public abstract record CadSemanticObject
{
    protected CadSemanticObject(
        string id,
        string sourceSubjectId,
        CadClassificationSubjectKind sourceSubjectKind,
        CadSemanticClassification classification,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSubjectId);
        ArgumentNullException.ThrowIfNull(bounds);
        if (!Enum.IsDefined(sourceSubjectKind) || !Enum.IsDefined(classification) || classification is CadSemanticClassification.Unclassified)
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        Id = id;
        SourceSubjectId = sourceSubjectId;
        SourceSubjectKind = sourceSubjectKind;
        Classification = classification;
        Bounds = bounds;
        GeometryDefaults = geometryDefaults;
    }

    public string Id { get; }

    public string SourceSubjectId { get; }

    public CadClassificationSubjectKind SourceSubjectKind { get; }

    public CadSemanticClassification Classification { get; }

    public CadBounds Bounds { get; }

    public CadRuleGeometryDefaults? GeometryDefaults { get; }

    protected static void ValidateValidContour(CadContour contour, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (contour.ValidationState is not CadContourValidationState.Valid || !contour.IsClosed)
        {
            throw new ArgumentException("A semantic profile must be a valid closed contour.", parameterName);
        }
    }

    protected static void ValidateUsableSegment(CadCurveSegment2 segment, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!double.IsFinite(segment.LengthMeters) || segment.LengthMeters <= 0 || segment.Bounds.State is not CadBoundsState.Computed)
        {
            throw new ArgumentException("A semantic path must be a finite segment with computed bounds.", parameterName);
        }
    }

    protected static void ValidateHeight(double? heightMeters, string parameterName)
    {
        if (heightMeters is double height && (!double.IsFinite(height) || height <= 0))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Semantic heights must be finite and greater than zero.");
        }
    }
}

public sealed record CadWallObject : CadSemanticObject
{
    public CadWallObject(
        string id,
        string sourceSubjectId,
        CadClassificationSubjectKind sourceSubjectKind,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults,
        CadContour? profile,
        CadCurveSegment2? baseline,
        double? heightMeters)
        : base(id, sourceSubjectId, sourceSubjectKind, CadSemanticClassification.Wall, bounds, geometryDefaults)
    {
        if ((profile is null) == (baseline is null))
        {
            throw new ArgumentException("A wall must have exactly one source geometry.");
        }

        if (profile is not null)
        {
            if (sourceSubjectKind is not CadClassificationSubjectKind.Contour)
            {
                throw new ArgumentException("A wall profile must originate from a contour.", nameof(sourceSubjectKind));
            }

            ValidateValidContour(profile, nameof(profile));
            GeometryKind = CadWallGeometryKind.ClosedProfile;
            Profile = profile;
        }
        else
        {
            if (sourceSubjectKind is not CadClassificationSubjectKind.OpenSegment)
            {
                throw new ArgumentException("A wall baseline must originate from an open segment.", nameof(sourceSubjectKind));
            }

            ValidateUsableSegment(baseline!, nameof(baseline));
            GeometryKind = CadWallGeometryKind.Baseline;
            Baseline = baseline;
        }

        ValidateHeight(heightMeters, nameof(heightMeters));
        HeightMeters = heightMeters;
    }

    public CadWallGeometryKind GeometryKind { get; }

    public CadContour? Profile { get; }

    public CadCurveSegment2? Baseline { get; }

    public double? HeightMeters { get; }
}

public sealed record CadFloorObject : CadSemanticObject
{
    public CadFloorObject(
        string id,
        string sourceSubjectId,
        CadClassificationSubjectKind sourceSubjectKind,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults,
        CadContour profile)
        : base(id, sourceSubjectId, sourceSubjectKind, CadSemanticClassification.Floor, bounds, geometryDefaults)
    {
        if (sourceSubjectKind is not CadClassificationSubjectKind.Contour)
        {
            throw new ArgumentException("A floor must originate from a contour.", nameof(sourceSubjectKind));
        }

        ValidateValidContour(profile, nameof(profile));
        Profile = profile;
    }

    public CadContour Profile { get; }
}

public sealed record CadColumnObject : CadSemanticObject
{
    public CadColumnObject(
        string id,
        string sourceSubjectId,
        CadClassificationSubjectKind sourceSubjectKind,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults,
        CadContour profile,
        double? heightMeters)
        : base(id, sourceSubjectId, sourceSubjectKind, CadSemanticClassification.Column, bounds, geometryDefaults)
    {
        if (sourceSubjectKind is not CadClassificationSubjectKind.Contour)
        {
            throw new ArgumentException("A column must originate from a contour.", nameof(sourceSubjectKind));
        }

        ValidateValidContour(profile, nameof(profile));
        ValidateHeight(heightMeters, nameof(heightMeters));
        Profile = profile;
        HeightMeters = heightMeters;
    }

    public CadContour Profile { get; }

    public double? HeightMeters { get; }
}

public sealed record CadRoadObject : CadSemanticObject
{
    public CadRoadObject(
        string id,
        string sourceSubjectId,
        CadClassificationSubjectKind sourceSubjectKind,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults,
        CadContour? area,
        CadCurveSegment2? centerline)
        : base(id, sourceSubjectId, sourceSubjectKind, CadSemanticClassification.Road, bounds, geometryDefaults)
    {
        if ((area is null) == (centerline is null))
        {
            throw new ArgumentException("A road must have exactly one source geometry.");
        }

        if (area is not null)
        {
            if (sourceSubjectKind is not CadClassificationSubjectKind.Contour)
            {
                throw new ArgumentException("A road area must originate from a contour.", nameof(sourceSubjectKind));
            }

            ValidateValidContour(area, nameof(area));
            GeometryKind = CadRoadGeometryKind.Area;
            Area = area;
        }
        else
        {
            if (sourceSubjectKind is not CadClassificationSubjectKind.OpenSegment)
            {
                throw new ArgumentException("A road centerline must originate from an open segment.", nameof(sourceSubjectKind));
            }

            ValidateUsableSegment(centerline!, nameof(centerline));
            GeometryKind = CadRoadGeometryKind.Centerline;
            Centerline = centerline;
        }
    }

    public CadRoadGeometryKind GeometryKind { get; }

    public CadContour? Area { get; }

    public CadCurveSegment2? Centerline { get; }
}

public sealed record CadStaticFacilityObject : CadSemanticObject
{
    public CadStaticFacilityObject(
        string id,
        string sourceInsertId,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults,
        string blockName,
        CadPoint3 position,
        double rotationDegrees,
        CadScale3 scale)
        : base(id, sourceInsertId, CadClassificationSubjectKind.Insert, CadSemanticClassification.StaticFacility, bounds, geometryDefaults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(scale);
        CadPoint3.ValidateFinite(rotationDegrees, nameof(rotationDegrees));

        SourceInsertId = sourceInsertId;
        BlockName = blockName;
        Position = position;
        RotationDegrees = rotationDegrees;
        Scale = scale;
    }

    public string SourceInsertId { get; }

    public string BlockName { get; }

    public CadPoint3 Position { get; }

    public double RotationDegrees { get; }

    public CadScale3 Scale { get; }
}

public sealed record CadDynamicEquipmentObject : CadSemanticObject
{
    public CadDynamicEquipmentObject(
        string id,
        string sourceInsertId,
        CadBounds bounds,
        CadRuleGeometryDefaults? geometryDefaults,
        string blockName,
        CadPoint3 position,
        double rotationDegrees,
        CadScale3 scale)
        : base(id, sourceInsertId, CadClassificationSubjectKind.Insert, CadSemanticClassification.DynamicEquipment, bounds, geometryDefaults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(scale);
        CadPoint3.ValidateFinite(rotationDegrees, nameof(rotationDegrees));

        SourceInsertId = sourceInsertId;
        BlockName = blockName;
        Position = position;
        RotationDegrees = rotationDegrees;
        Scale = scale;
    }

    public string SourceInsertId { get; }

    public string BlockName { get; }

    public CadPoint3 Position { get; }

    public double RotationDegrees { get; }

    public CadScale3 Scale { get; }
}

public enum SceneNodeContentKind
{
    ProceduralStaticGeometry = 0,
    StaticAssetReference = 1,
    DynamicAssetReference = 2
}

public sealed record SceneNodeTransform
{
    public SceneNodeTransform(CadPoint3 position, double rotationDegrees, CadScale3 scale)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(scale);
        CadPoint3.ValidateFinite(rotationDegrees, nameof(rotationDegrees));

        Position = position;
        RotationDegrees = rotationDegrees;
        Scale = scale;
    }

    public CadPoint3 Position { get; }

    public double RotationDegrees { get; }

    public CadScale3 Scale { get; }
}

public enum SceneDraftBuildStatus
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2
}

public sealed record SceneDraftBuildRequest
{
    public string DraftId { get; init; } = string.Empty;

    public CadDocumentModel SourceDocument { get; init; } = new();

    public NormalizedCadGeometryDocument Geometry { get; init; } = new();

    public CadContourDocument Contours { get; init; } = new();

    public CadClassificationResult Classification { get; init; } = new();
}

public sealed record SceneDraftBuildResult
{
    public SceneDraftBuildStatus Status { get; init; } = SceneDraftBuildStatus.Failed;

    public SceneDraft? Draft { get; init; }

    public IReadOnlyList<string> SkippedSubjectIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}
