namespace SceneBuilder.Blender;

public enum BlenderProcessStatus
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2
}

public sealed record BlenderProcessRequest
{
    public string ExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string WorkingDirectory { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;
}

public sealed record BlenderProcessResult
{
    public BlenderProcessStatus Status { get; init; } = BlenderProcessStatus.Failed;

    public int? ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;
}

public interface IBlenderProcessRunner
{
    Task<BlenderProcessResult> RunAsync(
        BlenderProcessRequest request,
        CancellationToken cancellationToken);
}
