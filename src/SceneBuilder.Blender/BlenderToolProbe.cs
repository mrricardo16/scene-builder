namespace SceneBuilder.Blender;

public sealed record BlenderToolProbeResult
{
    public bool IsAvailable { get; init; }

    public string? Version { get; init; }
}

public sealed class BlenderToolProbe
{
    private readonly IBlenderProcessRunner _processRunner;

    public BlenderToolProbe(IBlenderProcessRunner? processRunner = null) => _processRunner = processRunner ?? new BlenderProcessRunner();

    public async Task<BlenderToolProbeResult> ProbeAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return new BlenderToolProbeResult();
        }

        var result = await _processRunner.RunAsync(new BlenderProcessRequest
        {
            ExecutablePath = executablePath,
            Arguments = ["--version"],
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            Timeout = TimeSpan.FromSeconds(15),
            MaximumOutputCharacters = 1024
        }, cancellationToken);
        var version = result.StandardOutput.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("Blender ", StringComparison.Ordinal));
        return new BlenderToolProbeResult { IsAvailable = result.Status is BlenderProcessStatus.Succeeded && result.ExitCode == 0 && version is not null, Version = version };
    }
}
