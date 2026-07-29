using System.Text.Json.Serialization;

namespace SceneBuilder.Domain;

public enum CadSourceFormat
{
    Unknown = 0,
    Dxf = 1,
    Dwg = 2
}

public enum CadUnit
{
    Unknown = 0,
    Unitless = 1,
    Millimeters = 2,
    Centimeters = 3,
    Meters = 4,
    Inches = 5,
    Feet = 6
}

public enum CadBoundsState
{
    NotEvaluated = 0,
    Empty = 1,
    Computed = 2
}

public enum DiagnosticSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2
}

public enum JobReportStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

public enum TilesConversionStatus
{
    NotConfigured = 0,
    Succeeded = 1,
    Failed = 2
}

public sealed record CadBounds
{
    public CadBounds(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
        : this(minX, minY, minZ, maxX, maxY, maxZ, CadBoundsState.Computed)
    {
    }

    private CadBounds(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ,
        CadBoundsState state)
    {
        if (state is CadBoundsState.Computed)
        {
            ValidateComputedCoordinates(minX, minY, minZ, maxX, maxY, maxZ);
        }

        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
        State = state;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MinZ { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public double MaxZ { get; }

    public CadBoundsState State { get; }

    public static CadBounds NotEvaluated { get; } = new(0, 0, 0, 0, 0, 0, CadBoundsState.NotEvaluated);

    public static CadBounds Empty { get; } = new(0, 0, 0, 0, 0, 0, CadBoundsState.Empty);

    public static CadBounds Computed(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ) =>
        new(minX, minY, minZ, maxX, maxY, maxZ);

    private static void ValidateComputedCoordinates(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        ValidateFinite(minX, nameof(minX));
        ValidateFinite(minY, nameof(minY));
        ValidateFinite(minZ, nameof(minZ));
        ValidateFinite(maxX, nameof(maxX));
        ValidateFinite(maxY, nameof(maxY));
        ValidateFinite(maxZ, nameof(maxZ));

        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            throw new ArgumentException("Minimum bounds must not exceed maximum bounds.");
        }
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Bounds coordinates must be finite.");
        }
    }
}

public sealed record SceneDiagnostic
{
    [JsonPropertyName("severity")]
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Information;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; init; }
}

public sealed record CadLayerModel
{
    public string Name { get; init; } = string.Empty;

    public int EntityCount { get; init; }

    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;
}

public sealed record CadBlockModel
{
    public CadBlockModel(string name, int entityCount, CadBounds? bounds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);

        Name = name ?? string.Empty;
        EntityCount = entityCount;
        Bounds = bounds ?? CadBounds.NotEvaluated;
    }

    public string Name { get; }

    public int EntityCount { get; }

    public CadBounds Bounds { get; }
}

public sealed record CadEntityTypeSummary
{
    public CadEntityTypeSummary(string type, int entityCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);

        Type = type;
        EntityCount = entityCount;
    }

    public string Type { get; }

    public int EntityCount { get; }
}

public sealed record CadDocumentModel
{
    public string SourcePath { get; init; } = string.Empty;

    public CadSourceFormat SourceFormat { get; init; } = CadSourceFormat.Unknown;

    public CadUnit Unit { get; init; } = CadUnit.Unknown;

    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;

    public IReadOnlyList<CadLayerModel> Layers { get; init; } = Array.Empty<CadLayerModel>();

    public IReadOnlyList<CadBlockModel> Blocks { get; init; } = Array.Empty<CadBlockModel>();

    public IReadOnlyList<CadEntityTypeSummary> EntityTypes { get; init; } = Array.Empty<CadEntityTypeSummary>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record SceneNode
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public CadBounds Bounds { get; init; } = CadBounds.NotEvaluated;

    public IReadOnlyList<string> SourceLayers { get; init; } = Array.Empty<string>();
}

public sealed record SceneDraft
{
    public string Id { get; init; } = string.Empty;

    public CadDocumentModel SourceDocument { get; init; } = new();

    public IReadOnlyList<SceneNode> Nodes { get; init; } = Array.Empty<SceneNode>();

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public sealed record JobLayout
{
    private const string InputDirectoryName = "input";
    private const string IntermediateDirectoryName = "intermediate";
    private const string OutputDirectoryName = "output";
    private const string ReportsDirectoryName = "reports";
    private const string ReportFileName = "job-report.json";

    private JobLayout(string jobDirectory)
    {
        JobDirectory = jobDirectory;
        InputDirectory = Path.Combine(jobDirectory, InputDirectoryName);
        IntermediateDirectory = Path.Combine(jobDirectory, IntermediateDirectoryName);
        OutputDirectory = Path.Combine(jobDirectory, OutputDirectoryName);
        ReportsDirectory = Path.Combine(jobDirectory, ReportsDirectoryName);
        ReportPath = Path.Combine(ReportsDirectory, ReportFileName);
    }

    public string JobDirectory { get; }

    public string InputDirectory { get; }

    public string IntermediateDirectory { get; }

    public string OutputDirectory { get; }

    public string ReportsDirectory { get; }

    public string ReportPath { get; }

    public static JobLayout Create(string outputRoot, string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        if (jobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            jobId.Contains(Path.DirectorySeparatorChar) ||
            jobId.Contains(Path.AltDirectorySeparatorChar) ||
            jobId.EndsWith('.') ||
            jobId.EndsWith(' ') ||
            jobId is "." or ".." ||
            IsWindowsReservedDeviceBasename(jobId))
        {
            throw new ArgumentException("Job identifiers must be single path segments.", nameof(jobId));
        }

        var jobDirectory = Path.GetFullPath(Path.Combine(outputRoot, jobId));
        return new JobLayout(jobDirectory);
    }

    private static bool IsWindowsReservedDeviceBasename(string jobId)
    {
        var extensionSeparatorIndex = jobId.IndexOf('.');
        var deviceBasename = extensionSeparatorIndex >= 0
            ? jobId[..extensionSeparatorIndex]
            : jobId;

        if (deviceBasename.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceBasename.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceBasename.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceBasename.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return deviceBasename.Length == 4 &&
            (deviceBasename.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             deviceBasename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            deviceBasename[3] is (>= '1' and <= '9') or '¹' or '²' or '³';
    }
}

public sealed record JobArtifact
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public sealed record JobReport
{
    [JsonPropertyName("jobId")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UnixEpoch;

    [JsonPropertyName("status")]
    public JobReportStatus Status { get; init; } = JobReportStatus.Pending;

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();

    [JsonPropertyName("artifacts")]
    public IReadOnlyList<JobArtifact> Artifacts { get; init; } = Array.Empty<JobArtifact>();
}

public sealed record TilesConversionRequest
{
    public SceneDraft SceneDraft { get; init; } = new();

    public string OutputDirectory { get; init; } = string.Empty;
}

public sealed record TilesConversionResult
{
    public TilesConversionStatus Status { get; init; } = TilesConversionStatus.NotConfigured;

    public string? OutputPath { get; init; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = Array.Empty<SceneDiagnostic>();
}

public interface ITilesConverter
{
    Task<TilesConversionResult> ConvertAsync(
        TilesConversionRequest request,
        CancellationToken cancellationToken);
}
