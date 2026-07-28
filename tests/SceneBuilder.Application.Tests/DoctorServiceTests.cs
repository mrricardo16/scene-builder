using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Application.Tests;

public sealed class DoctorServiceTests
{
    [Fact]
    public async Task InspectAsync_reports_the_current_dotnet_runtime()
    {
        var service = new DoctorService([new DotNetRuntimeProbe()]);

        var report = await service.InspectAsync(new DoctorOptions(), CancellationToken.None);

        var runtime = Assert.Single(report.Tools);
        Assert.Equal("dotnet", runtime.Name);
        Assert.Equal(DoctorToolStatus.Available, runtime.Status);
        Assert.Equal(Environment.Version.ToString(), runtime.Version);
    }

    [Fact]
    public async Task ProbeAsync_reads_version_for_an_existing_configured_executable()
    {
        var fileSystem = new FakeFileSystem(@"C:\\tools\\blender.exe");
        var versionReader = new FakeVersionReader(ExecutableVersionResult.Success("Blender 4.5"));
        var probe = new ConfiguredExecutableProbe(DoctorTool.Blender, fileSystem, versionReader);

        var result = await probe.ProbeAsync(
            new DoctorOptions { BlenderPath = @"C:\\tools\\blender.exe" },
            CancellationToken.None);

        Assert.Equal(DoctorToolStatus.Available, result.Status);
        Assert.Equal("Blender 4.5", result.Version);
        Assert.Equal(1, versionReader.CallCount);
        Assert.Equal(@"C:\\tools\\blender.exe", versionReader.ExecutablePath);
    }

    [Fact]
    public async Task ProbeAsync_reports_an_unconfigured_optional_tool_without_running_a_process()
    {
        var versionReader = new FakeVersionReader(ExecutableVersionResult.Success("unused"));
        var probe = new ConfiguredExecutableProbe(
            DoctorTool.TilesConverter,
            new FakeFileSystem(),
            versionReader);

        var result = await probe.ProbeAsync(new DoctorOptions(), CancellationToken.None);

        Assert.Equal(DoctorToolStatus.Unavailable, result.Status);
        Assert.Equal(0, versionReader.CallCount);
        Assert.Contains("not configured", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeFileSystem(params string[] existingPaths) : IFileSystem
    {
        private readonly HashSet<string> _existingPaths = new(existingPaths, StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => _existingPaths.Contains(path);
    }

    private sealed class FakeVersionReader(ExecutableVersionResult result) : IExecutableVersionReader
    {
        public int CallCount { get; private set; }

        public string? ExecutablePath { get; private set; }

        public Task<ExecutableVersionResult> ReadVersionAsync(
            string executablePath,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ExecutablePath = executablePath;
            return Task.FromResult(result);
        }
    }
}
