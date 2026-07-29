using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Blender;

internal sealed class BlenderManifestMapper
{
    private const double ArcMaximumStepDegrees = 15d;

    public BlenderManifestMappingResult Map(SceneDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Id) ||
            HasDuplicates(draft.SemanticObjects.Select(item => item.Id)) ||
            HasDuplicates(draft.Nodes.Select(item => item.SemanticObjectId)))
        {
            return BlenderManifestMappingResult.Failed("BLENDER_MANIFEST_INVALID", "The SceneDraft cannot be mapped to a Blender manifest.");
        }

        var nodes = draft.Nodes.ToDictionary(node => node.SemanticObjectId, StringComparer.Ordinal);
        var objects = new List<BlenderManifestObject>();
        var skippedObjectIds = new List<string>();
        var diagnostics = new List<SceneDiagnostic>();

        foreach (var semanticObject in draft.SemanticObjects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!nodes.TryGetValue(semanticObject.Id, out var node) ||
                node.ContentKind is not SceneNodeContentKind.ProceduralStaticGeometry ||
                node.Classification != semanticObject.Classification ||
                semanticObject.Bounds.State is not CadBoundsState.Computed)
            {
                Skip(semanticObject.Id, "BLENDER_MANIFEST_INVALID", "A SceneDraft object is not suitable for procedural generation.", skippedObjectIds, diagnostics);
                continue;
            }

            switch (semanticObject)
            {
                case CadWallObject { GeometryKind: CadWallGeometryKind.ClosedProfile, Profile: not null, HeightMeters: > 0 } wall:
                    objects.Add(CreateProfileObject(wall.Id, "wall", wall.Profile, wall.HeightMeters.Value));
                    break;
                case CadFloorObject floor:
                    objects.Add(CreateProfileObject(floor.Id, "floor", floor.Profile, null));
                    break;
                case CadColumnObject { HeightMeters: > 0 } column:
                    objects.Add(CreateProfileObject(column.Id, "column", column.Profile, column.HeightMeters.Value));
                    break;
                case CadRoadObject { GeometryKind: CadRoadGeometryKind.Area, Area: not null } road:
                    objects.Add(CreateProfileObject(road.Id, "road", road.Area, null));
                    break;
                case CadWallObject { GeometryKind: CadWallGeometryKind.Baseline }:
                case CadRoadObject { GeometryKind: CadRoadGeometryKind.Centerline }:
                case CadStaticFacilityObject:
                case CadDynamicEquipmentObject:
                    Skip(semanticObject.Id, "BLENDER_OBJECT_UNSUPPORTED", "A SceneDraft object is outside the SB-10 procedural geometry scope.", skippedObjectIds, diagnostics);
                    break;
                case CadWallObject:
                case CadColumnObject:
                    Skip(semanticObject.Id, "BLENDER_GEOMETRY_PARAMETER_MISSING", "A procedural object has no usable configured height.", skippedObjectIds, diagnostics);
                    break;
                default:
                    Skip(semanticObject.Id, "BLENDER_OBJECT_UNSUPPORTED", "A SceneDraft object is outside the SB-10 procedural geometry scope.", skippedObjectIds, diagnostics);
                    break;
            }
        }

        if (objects.Count == 0)
        {
            diagnostics.Add(Diagnostic("BLENDER_MANIFEST_INVALID", DiagnosticSeverity.Error, "The SceneDraft has no supported procedural objects."));
            return new BlenderManifestMappingResult(null, skippedObjectIds, SortDiagnostics(diagnostics));
        }

        var manifest = new BlenderManifest
        {
            ContractVersion = "1.0",
            Unit = "meters",
            DraftId = draft.Id,
            Objects = objects.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
        };
        return new BlenderManifestMappingResult(manifest, skippedObjectIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(), SortDiagnostics(diagnostics));
    }

    public static string Serialize(BlenderManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, BlenderManifestJson.Options);
    }

    private static BlenderManifestObject CreateProfileObject(string id, string kind, CadContour profile, double? heightMeters) =>
        new()
        {
            Id = id,
            Kind = kind,
            Profile = BlenderProfileTessellator.Tessellate(profile, ArcMaximumStepDegrees),
            HeightMeters = heightMeters
        };

    private static bool HasDuplicates(IEnumerable<string> identifiers) =>
        identifiers.GroupBy(identifier => identifier, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static void Skip(string id, string code, string message, ICollection<string> skippedObjectIds, ICollection<SceneDiagnostic> diagnostics)
    {
        skippedObjectIds.Add(id);
        diagnostics.Add(Diagnostic(code, DiagnosticSeverity.Warning, message));
    }

    private static IReadOnlyList<SceneDiagnostic> SortDiagnostics(IEnumerable<SceneDiagnostic> diagnostics) =>
        diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Message, StringComparer.Ordinal).ToArray();

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity, string message) =>
        new() { Code = code, Severity = severity, Message = message };
}

internal sealed record BlenderManifestMappingResult(
    BlenderManifest? Manifest,
    IReadOnlyList<string> SkippedSemanticObjectIds,
    IReadOnlyList<SceneDiagnostic> Diagnostics)
{
    public bool IsValid => Manifest is not null;

    public static BlenderManifestMappingResult Failed(string code, string message) =>
        new(null, Array.Empty<string>(), [new SceneDiagnostic { Code = code, Severity = DiagnosticSeverity.Error, Message = message }]);
}

internal sealed record BlenderManifest
{
    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; init; } = "1.0";

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = "meters";

    [JsonPropertyName("draftId")]
    public string DraftId { get; init; } = string.Empty;

    [JsonPropertyName("objects")]
    public IReadOnlyList<BlenderManifestObject> Objects { get; init; } = Array.Empty<BlenderManifestObject>();
}

internal sealed record BlenderManifestObject
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public IReadOnlyList<BlenderManifestPoint> Profile { get; init; } = Array.Empty<BlenderManifestPoint>();

    [JsonPropertyName("heightMeters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? HeightMeters { get; init; }
}

internal sealed record BlenderManifestPoint
{
    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }

    [JsonPropertyName("z")]
    public double Z { get; init; }
}

internal static class BlenderManifestJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

internal static class BlenderProfileTessellator
{
    public static IReadOnlyList<BlenderManifestPoint> Tessellate(CadContour contour, double maximumArcStepDegrees)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (!double.IsFinite(maximumArcStepDegrees) || maximumArcStepDegrees <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumArcStepDegrees));
        }

        var points = contour switch
        {
            CadSegmentContour segmentContour => TessellateSegments(segmentContour.Segments, maximumArcStepDegrees),
            CadCircleContour circleContour => TessellateCircle(circleContour, maximumArcStepDegrees),
            _ => throw new ArgumentException("The contour type is not supported by the Blender manifest.", nameof(contour))
        };

        RemoveAdjacentDuplicates(points);
        if (points.Count > 1 && SamePoint(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)))
        {
            throw new ArgumentException("A Blender profile must have at least three finite points.", nameof(contour));
        }

        return points.ToArray();
    }

    private static List<BlenderManifestPoint> TessellateSegments(IReadOnlyList<CadCurveSegment2> segments, double maximumArcStepDegrees)
    {
        var points = new List<BlenderManifestPoint>();
        foreach (var segment in segments)
        {
            if (points.Count == 0)
            {
                points.Add(ToPoint(segment.Start));
            }

            if (segment is CadArcSegment2 arc)
            {
                var stepCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.SignedSweepRadians) * 180d / Math.PI / maximumArcStepDegrees));
                for (var step = 1; step <= stepCount; step++)
                {
                    points.Add(ToPoint(arc.PointAtFraction((double)step / stepCount)));
                }
            }
            else
            {
                points.Add(ToPoint(segment.End));
            }
        }

        return points;
    }

    private static List<BlenderManifestPoint> TessellateCircle(CadCircleContour circle, double maximumArcStepDegrees)
    {
        var segmentCount = Math.Max(3, (int)Math.Ceiling(360d / maximumArcStepDegrees));
        var points = new List<BlenderManifestPoint>(segmentCount);
        for (var index = 0; index < segmentCount; index++)
        {
            var radians = index * 2d * Math.PI / segmentCount;
            points.Add(new BlenderManifestPoint
            {
                X = circle.Center.X + (circle.Radius * Math.Cos(radians)),
                Y = circle.Center.Y + (circle.Radius * Math.Sin(radians)),
                Z = circle.Center.Z
            });
        }

        return points;
    }

    private static BlenderManifestPoint ToPoint(CadPoint3 point) => new() { X = point.X, Y = point.Y, Z = point.Z };

    private static void RemoveAdjacentDuplicates(IList<BlenderManifestPoint> points)
    {
        for (var index = points.Count - 1; index > 0; index--)
        {
            if (SamePoint(points[index], points[index - 1]))
            {
                points.RemoveAt(index);
            }
        }
    }

    private static bool SamePoint(BlenderManifestPoint first, BlenderManifestPoint second) =>
        Math.Abs(first.X - second.X) <= 0.000000001d &&
        Math.Abs(first.Y - second.Y) <= 0.000000001d &&
        Math.Abs(first.Z - second.Z) <= 0.000000001d;
}
