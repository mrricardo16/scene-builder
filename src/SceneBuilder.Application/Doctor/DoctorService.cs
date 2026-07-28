namespace SceneBuilder.Application.Doctor;

public sealed class DoctorService(IEnumerable<IDoctorProbe> probes)
{
    private readonly IReadOnlyList<IDoctorProbe> _probes = probes.ToArray();

    public async Task<DoctorReport> InspectAsync(
        DoctorOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var probeTasks = _probes
            .Select(probe => probe.ProbeAsync(options, cancellationToken))
            .ToArray();

        var tools = await Task.WhenAll(probeTasks);
        return new DoctorReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Tools = tools
        };
    }
}

public sealed class DotNetRuntimeProbe : IDoctorProbe
{
    public Task<DoctorToolReport> ProbeAsync(
        DoctorOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new DoctorToolReport
        {
            Name = "dotnet",
            Status = DoctorToolStatus.Available,
            Version = Environment.Version.ToString(),
            Detail = ".NET runtime is available."
        });
    }
}

public sealed class ConfiguredExecutableProbe(
    DoctorTool tool,
    IFileSystem fileSystem,
    IExecutableVersionReader versionReader) : IDoctorProbe
{
    public async Task<DoctorToolReport> ProbeAsync(
        DoctorOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredPath = GetConfiguredPath(options);
        var name = GetName();
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Unavailable(name, null, "Optional tool path is not configured.");
        }

        if (!fileSystem.FileExists(configuredPath))
        {
            return Unavailable(name, configuredPath, "Configured executable file does not exist.");
        }

        try
        {
            var version = await versionReader.ReadVersionAsync(configuredPath, cancellationToken);
            return version.IsAvailable
                ? new DoctorToolReport
                {
                    Name = name,
                    Status = DoctorToolStatus.Available,
                    ConfiguredPath = configuredPath,
                    Version = version.Version,
                    Detail = "Configured executable is available."
                }
                : Unavailable(
                    name,
                    configuredPath,
                    version.Detail ?? "Configured executable could not be inspected.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Unavailable(name, configuredPath, $"Executable inspection failed: {exception.Message}");
        }
    }

    private string? GetConfiguredPath(DoctorOptions options) => tool switch
    {
        DoctorTool.Blender => options.BlenderPath,
        DoctorTool.TilesConverter => options.TilesPath,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only optional executable tools are supported.")
    };

    private string GetName() => tool switch
    {
        DoctorTool.Blender => "blender",
        DoctorTool.TilesConverter => "tiles",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only optional executable tools are supported.")
    };

    private static DoctorToolReport Unavailable(string name, string? configuredPath, string detail) => new()
    {
        Name = name,
        Status = DoctorToolStatus.Unavailable,
        ConfiguredPath = configuredPath,
        Detail = detail
    };
}
