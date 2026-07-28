using System.Text.Json.Serialization;

namespace SceneBuilder.Application.Doctor;

public enum DoctorTool
{
    DotNet = 0,
    Blender = 1,
    TilesConverter = 2
}

public enum DoctorToolStatus
{
    Available = 0,
    Unavailable = 1
}

public sealed record DoctorOptions
{
    public string? BlenderPath { get; init; }

    public string? TilesPath { get; init; }
}

public sealed record DoctorToolReport
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public DoctorToolStatus Status { get; init; }

    [JsonPropertyName("configuredPath")]
    public string? ConfiguredPath { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}

public sealed record DoctorReport
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<DoctorToolReport> Tools { get; init; } = Array.Empty<DoctorToolReport>();
}

public sealed record ExecutableVersionResult(bool IsAvailable, string? Version, string? Detail)
{
    public static ExecutableVersionResult Success(string version) => new(true, version, null);

    public static ExecutableVersionResult Unavailable(string detail) => new(false, null, detail);
}

public interface IDoctorProbe
{
    Task<DoctorToolReport> ProbeAsync(DoctorOptions options, CancellationToken cancellationToken);
}

public interface IFileSystem
{
    bool FileExists(string path);
}

public interface IExecutableVersionReader
{
    Task<ExecutableVersionResult> ReadVersionAsync(
        string executablePath,
        CancellationToken cancellationToken);
}
