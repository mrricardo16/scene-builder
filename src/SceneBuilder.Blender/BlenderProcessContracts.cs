using System.Diagnostics;

namespace SceneBuilder.Blender;

public enum BlenderProcessStatus
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
    TimedOut = 3
}

public sealed record BlenderProcessRequest
{
    public string ExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string WorkingDirectory { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; }

    public int MaximumOutputCharacters { get; init; }
}

public sealed record BlenderProcessResult
{
    public BlenderProcessStatus Status { get; init; } = BlenderProcessStatus.Failed;

    public int? ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool OutputTruncated { get; init; }
}

public interface IBlenderProcessRunner
{
    Task<BlenderProcessResult> RunAsync(BlenderProcessRequest request, CancellationToken cancellationToken);
}

internal static class BlenderCommandBuilder
{
    public static BlenderProcessRequest Create(
        string executablePath,
        string scriptPath,
        string manifestPath,
        string outputPath,
        string workingDirectory,
        TimeSpan timeout,
        int maximumOutputCharacters) =>
        new()
        {
            ExecutablePath = executablePath,
            WorkingDirectory = workingDirectory,
            Timeout = timeout,
            MaximumOutputCharacters = maximumOutputCharacters,
            Arguments = ["--background", "--factory-startup", "--python", scriptPath, "--", "--manifest", manifestPath, "--output", outputPath]
        };

    public static ProcessStartInfo CreateStartInfo(BlenderProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var info = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}
