using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public enum CadBuildInputSnapshotStatus { Unavailable = 0, Available = 1, Invalid = 2 }

public sealed record CadBuildInputSnapshotDescriptor
{
    public CadBuildInputSnapshotStatus Status { get; init; } = CadBuildInputSnapshotStatus.Unavailable;
    public string? ContractVersion { get; init; }
    public string? SnapshotId { get; init; }
    public string? ContentHash { get; init; }
    public string? RelativePath { get; init; }
}

public static class CadBuildInputSnapshotDescriptorValidator
{
    public static void Validate(CadBuildInputSnapshotDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Status is CadBuildInputSnapshotStatus.Available)
        {
            if (descriptor.ContractVersion != "1.0" || string.IsNullOrWhiteSpace(descriptor.SnapshotId) || string.IsNullOrWhiteSpace(descriptor.ContentHash) || descriptor.RelativePath != "analysis/build-input-snapshot.json")
            {
                throw new InvalidDataException("Snapshot descriptor is invalid.");
            }

            return;
        }

        if (descriptor.SnapshotId is not null || descriptor.ContentHash is not null || descriptor.RelativePath is not null)
        {
            throw new InvalidDataException("Unavailable snapshot descriptors cannot contain artifact identity.");
        }
    }
}

public sealed record CadBuildInputSnapshot
{
    public string ContractVersion { get; init; } = "1.0";
    public string SnapshotId { get; init; } = string.Empty;
    public string AnalysisId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public CadCoordinateContext? CoordinateSystem { get; init; }
    public CadBounds SourceBounds { get; init; } = CadBounds.NotEvaluated;
    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;
    public IReadOnlyList<CadBuildGeometryObject> GeometryObjects { get; init; } = Array.Empty<CadBuildGeometryObject>();
    public IReadOnlyList<CadBuildContour> Contours { get; init; } = Array.Empty<CadBuildContour>();
    public IReadOnlyList<CadBuildRepairCandidate> RepairCandidates { get; init; } = Array.Empty<CadBuildRepairCandidate>();
    public IReadOnlyList<CadBuildClassificationSubject> ClassificationSubjects { get; init; } = Array.Empty<CadBuildClassificationSubject>();
    public IReadOnlyList<CadBuildAnalyzeTimeClassification> AnalyzeTimeClassifications { get; init; } = Array.Empty<CadBuildAnalyzeTimeClassification>();
    public IReadOnlyList<CadBuildAssetCandidate> AssetCandidates { get; init; } = Array.Empty<CadBuildAssetCandidate>();
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
    public string ContentHash { get; init; } = string.Empty;
}

public sealed record CadBuildGeometryObject(string GeometryObjectId, CadGeometryEntity Geometry);
public sealed record CadBuildContour(string ContourId, CadContour Contour, IReadOnlyList<string> GeometryObjectIds);
public sealed record CadBuildRepairCandidate(string RepairActionId, CadGeometryRepairAction Action, IReadOnlyList<string> GeometryObjectIds, IReadOnlyList<string> ContourIds);
public sealed record CadBuildClassificationSubject(string ClassificationSubjectId, CadClassificationSubject Subject, IReadOnlyList<string> GeometryObjectIds, IReadOnlyList<string> ContourIds);
public sealed record CadBuildAnalyzeTimeClassification(string ClassificationSubjectId, CadSemanticClassification Classification, string? MatchedRuleId, int? Priority, IReadOnlyList<string> CandidateRuleIds);
public sealed record CadBuildAssetCandidate(string AssetCandidateId, string ClassificationSubjectId, CadSemanticClassification CandidateType, CadPoint3 Position, double RotationDegrees, CadScale3 Scale, string? BlockName);

public sealed class CadBuildInputSnapshotFactory
{
    private readonly CadClassificationSubjectBuilder _subjectBuilder = new();

    public CadBuildInputSnapshot Create(string analysisId, string sourceFingerprint, CadAdapterAnalysisResult adapter, CadClassificationResult? classification, IEnumerable<SceneDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var geometry = adapter.Geometry!.Entities.OrderBy(entity => entity.SourceOrder).Select(entity =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CadBuildGeometryObject(GeometryId(entity), entity);
        }).ToArray();
        var geometryBySourceOrder = geometry.ToDictionary(item => item.Geometry.SourceOrder, item => item.GeometryObjectId);
        var contours = adapter.Contours!.Contours.OrderBy(contour => contour.Id, StringComparer.Ordinal).Select(contour =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CadBuildContour(contour.Id, contour, ContourGeometryIds(contour, geometryBySourceOrder));
        }).ToArray();
        var subjects = _subjectBuilder.Build(new CadClassificationInput { Summary = adapter.SourceDocument!, Geometry = adapter.Geometry!, Contours = adapter.Contours! })
            .Select(subject => new CadBuildClassificationSubject(subject.Id, subject, SubjectGeometryIds(subject, geometryBySourceOrder), SubjectContourIds(subject, contours))).ToArray();
        var classifications = (classification?.Objects ?? Array.Empty<CadObjectClassification>()).OrderBy(item => item.Subject.Id, StringComparer.Ordinal)
            .Select(item => new CadBuildAnalyzeTimeClassification(item.Subject.Id, item.Classification, item.MatchedRuleId, item.Priority, item.CandidateRuleIds.OrderBy(id => id, StringComparer.Ordinal).ToArray())).ToArray();
        var repairs = adapter.RepairPlan!.Actions.OrderBy(action => action.Id, StringComparer.Ordinal)
            .Select(action => new CadBuildRepairCandidate(action.Id, action, action.SourceSegmentIds.Select(id => GeometryIdFromSegment(id, geometryBySourceOrder)).Where(id => id is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray(), Array.Empty<string>())).ToArray();
        var assets = classifications.Where(item => item.Classification is CadSemanticClassification.StaticFacility or CadSemanticClassification.DynamicEquipment)
            .Join(subjects, classificationItem => classificationItem.ClassificationSubjectId, subject => subject.ClassificationSubjectId, (classificationItem, subject) => CreateAssetCandidate(classificationItem, subject, geometry)).OrderBy(item => item.AssetCandidateId, StringComparer.Ordinal).ToArray();
        var payload = new CadBuildInputSnapshot { AnalysisId = analysisId, SourceFingerprint = sourceFingerprint, CoordinateSystem = adapter.Geometry.CoordinateContext, SourceBounds = adapter.SourceDocument!.Bounds, Bounds = adapter.Geometry.Bounds, GeometryObjects = geometry, Contours = contours, RepairCandidates = repairs, ClassificationSubjects = subjects, AnalyzeTimeClassifications = classifications, AssetCandidates = assets, Diagnostics = diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal).ToArray() };
        var hash = CadBuildInputSnapshotCanonicalHasher.Compute(payload);
        return payload with { ContentHash = hash, SnapshotId = "snapshot-" + hash };
    }

    private static string GeometryId(CadGeometryEntity entity) => $"geometry-{entity.SourceOrder:D6}";
    private static string? GeometryIdFromSegment(string segmentId, IReadOnlyDictionary<int, string> geometryBySourceOrder) => segmentId.Split(':') is [_, var order, ..] && int.TryParse(order, out var sourceOrder) && geometryBySourceOrder.TryGetValue(sourceOrder, out var id) ? id : null;
    private static IReadOnlyList<string> ContourGeometryIds(CadContour contour, IReadOnlyDictionary<int, string> geometryBySourceOrder) => contour is CadSegmentContour segment ? segment.Segments.Select(item => geometryBySourceOrder.TryGetValue(item.SourceOrder, out var id) ? id : null).Where(id => id is not null).Cast<string>().Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    private static IReadOnlyList<string> SubjectGeometryIds(CadClassificationSubject subject, IReadOnlyDictionary<int, string> geometryBySourceOrder) => subject.Kind is CadClassificationSubjectKind.Insert && subject.Id.Split(':') is [_, var order] && int.TryParse(order, out var sourceOrder) && geometryBySourceOrder.TryGetValue(sourceOrder, out var id) ? [id] : Array.Empty<string>();
    private static IReadOnlyList<string> SubjectContourIds(CadClassificationSubject subject, IReadOnlyList<CadBuildContour> contours) => contours.Any(contour => contour.ContourId == subject.Id) ? [subject.Id] : Array.Empty<string>();
    private static CadBuildAssetCandidate CreateAssetCandidate(CadBuildAnalyzeTimeClassification classification, CadBuildClassificationSubject subject, IReadOnlyList<CadBuildGeometryObject> geometry) { var insert = subject.GeometryObjectIds.Select(id => geometry.Single(item => item.GeometryObjectId == id).Geometry).OfType<CadInsertGeometry>().SingleOrDefault(); return new CadBuildAssetCandidate("asset-" + classification.ClassificationSubjectId, classification.ClassificationSubjectId, classification.Classification, insert?.Position ?? new CadPoint3(0, 0, 0), insert?.RotationDegrees ?? 0, insert?.Scale ?? CadScale3.Identity, subject.Subject.BlockName); }
}

public static class CadBuildInputSnapshotCanonicalHasher
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new CanonicalDoubleConverter() }
    };

    public static string Compute(CadBuildInputSnapshot snapshot)
    {
        var payload = snapshot with { SnapshotId = string.Empty, ContentHash = string.Empty };
        CadBuildInputSnapshotValidator.Validate(payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal static JsonSerializerOptions SerializerOptions => Options;

    private sealed class CanonicalDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (!double.IsFinite(value))
            {
                throw new JsonException("Snapshot numeric values must be finite.");
            }

            writer.WriteNumberValue(value == 0d ? 0d : value);
        }
    }
}

public static class CadBuildInputSnapshotValidator
{
    public static void Validate(CadBuildInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ContractVersion != "1.0" || string.IsNullOrWhiteSpace(snapshot.AnalysisId) || string.IsNullOrWhiteSpace(snapshot.SourceFingerprint) || snapshot.GeometryObjects is null || snapshot.Contours is null || snapshot.RepairCandidates is null || snapshot.ClassificationSubjects is null || snapshot.AnalyzeTimeClassifications is null || snapshot.AssetCandidates is null || snapshot.Diagnostics is null)
        {
            throw new InvalidDataException("Snapshot contract is invalid.");
        }

        ValidateBounds(snapshot.SourceBounds, nameof(snapshot.SourceBounds));
        ValidateBounds(snapshot.Bounds, nameof(snapshot.Bounds));
        if (snapshot.CoordinateSystem is { } coordinateSystem)
        {
            if (!double.IsFinite(coordinateSystem.UnitScaleToMeters) || coordinateSystem.UnitScaleToMeters <= 0 || coordinateSystem.SourceOrigin is null)
            {
                throw new InvalidDataException("Snapshot coordinate context is invalid.");
            }
        }

        if (snapshot.GeometryObjects.Any(item => item is null || item.Geometry is null) ||
            snapshot.Contours.Any(item => item is null || item.Contour is null || item.GeometryObjectIds is null) ||
            snapshot.RepairCandidates.Any(item => item is null || item.Action is null || item.GeometryObjectIds is null || item.ContourIds is null) ||
            snapshot.ClassificationSubjects.Any(item => item is null || item.Subject is null || item.GeometryObjectIds is null || item.ContourIds is null) ||
            snapshot.AnalyzeTimeClassifications.Any(item => item is null || item.CandidateRuleIds is null) ||
            snapshot.AssetCandidates.Any(item => item is null))
        {
            throw new InvalidDataException("Snapshot records are invalid.");
        }

        foreach (var item in snapshot.GeometryObjects)
        {
            ValidateBounds(item.Geometry.Bounds, $"GeometryObjects[{item.GeometryObjectId}].Bounds");
        }

        foreach (var item in snapshot.Contours)
        {
            ValidateBounds(item.Contour.Bounds, $"Contours[{item.ContourId}].Bounds");
        }

        try
        {
            _ = JsonSerializer.SerializeToUtf8Bytes(snapshot, CadBuildInputSnapshotCanonicalHasher.SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Snapshot numeric values are invalid.", exception);
        }

        var geometry = Unique(snapshot.GeometryObjects.Select(item => item.GeometryObjectId)); var contours = Unique(snapshot.Contours.Select(item => item.ContourId)); var subjects = Unique(snapshot.ClassificationSubjects.Select(item => item.ClassificationSubjectId)); Unique(snapshot.RepairCandidates.Select(item => item.RepairActionId)); Unique(snapshot.AssetCandidates.Select(item => item.AssetCandidateId));
        if (snapshot.Contours.Any(item => item.GeometryObjectIds.Any(id => !geometry.Contains(id))) || snapshot.RepairCandidates.Any(item => item.GeometryObjectIds.Any(id => !geometry.Contains(id)) || item.ContourIds.Any(id => !contours.Contains(id))) || snapshot.ClassificationSubjects.Any(item => item.GeometryObjectIds.Any(id => !geometry.Contains(id)) || item.ContourIds.Any(id => !contours.Contains(id))) || snapshot.AnalyzeTimeClassifications.Any(item => !subjects.Contains(item.ClassificationSubjectId)) || snapshot.AssetCandidates.Any(item => !subjects.Contains(item.ClassificationSubjectId))) throw new InvalidDataException("Snapshot references are invalid.");
        if (!double.IsFinite(snapshot.Bounds.MinX) || !double.IsFinite(snapshot.Bounds.MaxX)) throw new InvalidDataException("Snapshot bounds are invalid.");
        if (!string.IsNullOrEmpty(snapshot.ContentHash) && (!string.Equals(snapshot.ContentHash, CadBuildInputSnapshotCanonicalHasher.Compute(snapshot), StringComparison.Ordinal) || snapshot.SnapshotId != "snapshot-" + snapshot.ContentHash)) throw new InvalidDataException("Snapshot content hash is invalid.");
    }

    private static void ValidateBounds(CadBounds? bounds, string name)
    {
        if (bounds is null || !Enum.IsDefined(bounds.State) ||
            !double.IsFinite(bounds.MinX) || !double.IsFinite(bounds.MinY) || !double.IsFinite(bounds.MinZ) ||
            !double.IsFinite(bounds.MaxX) || !double.IsFinite(bounds.MaxY) || !double.IsFinite(bounds.MaxZ) ||
            (bounds.State is CadBoundsState.Computed && (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY || bounds.MinZ > bounds.MaxZ)))
        {
            throw new InvalidDataException($"Snapshot bounds are invalid: {name}.");
        }
    }

    private static HashSet<string> Unique(IEnumerable<string> ids) { var values = ids.ToArray(); if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw new InvalidDataException("Snapshot identifiers are invalid."); return values.ToHashSet(StringComparer.Ordinal); }
}

public sealed class CadBuildInputSnapshotSerializer
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public async Task WriteValidatedAsync(string outputRoot, CadBuildInputSnapshot snapshot, CancellationToken cancellationToken)
    {
        CadBuildInputSnapshotValidator.Validate(snapshot);
        var path = Path.Combine(outputRoot, "analysis", "build-input-snapshot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new IOException("The build input snapshot artifact already exists.");
        var staging = path + ".staging";
        try
        {
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, CadBuildInputSnapshotCanonicalHasher.SerializerOptions, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var roundTrip = await ReadValidatedAsync(staging, cancellationToken);
            if (roundTrip.SnapshotId != snapshot.SnapshotId || roundTrip.ContentHash != snapshot.ContentHash ||
                CadBuildInputSnapshotCanonicalHasher.Compute(roundTrip) != snapshot.ContentHash)
            {
                throw new InvalidDataException("Snapshot readback is invalid.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, path, false);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    public static async Task<CadBuildInputSnapshot> ReadValidatedAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<CadBuildInputSnapshot>(stream, CadBuildInputSnapshotCanonicalHasher.SerializerOptions, cancellationToken) ?? throw new InvalidDataException("Snapshot JSON is empty.");
        CadBuildInputSnapshotValidator.Validate(snapshot);
        return snapshot;
    }
}
