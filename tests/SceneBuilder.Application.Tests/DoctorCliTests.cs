using System.Text.Json;
using SceneBuilder.Application.Doctor;
using SceneBuilder.Cli;

namespace SceneBuilder.Application.Tests;

public sealed class DoctorCliTests
{
    [Fact]
    public void TryParse_accepts_doctor_optional_paths_and_output_directory()
    {
        var parsed = DoctorCommandLineParser.TryParse(
            ["--output", @"C:\\jobs\\doctor", "--blender-path", @"C:\\tools\\blender.exe", "--tiles-path", @"C:\\tools\\tiles.exe"],
            out var command,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(command);
        Assert.Equal(@"C:\\jobs\\doctor", command.OutputDirectory);
        Assert.Equal(@"C:\\tools\\blender.exe", command.DoctorOptions.BlenderPath);
        Assert.Equal(@"C:\\tools\\tiles.exe", command.DoctorOptions.TilesPath);
    }

    [Fact]
    public void TryParse_rejects_an_unknown_option()
    {
        var parsed = DoctorCommandLineParser.TryParse(["--unknown"], out _, out var error);

        Assert.False(parsed);
        Assert.Contains("Unknown option", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_writes_camel_case_json_directly_to_the_provided_output_directory()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "scene-builder-tests", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(testRoot, "doctor-output");

        try
        {
            var reportPath = await new DoctorReportWriter().WriteAsync(
                new DoctorReport
                {
                    GeneratedAt = DateTimeOffset.UnixEpoch,
                    Tools =
                    [
                        new DoctorToolReport
                        {
                            Name = "dotnet",
                            Status = DoctorToolStatus.Available,
                            Detail = ".NET runtime is available."
                        }
                    ]
                },
                outputDirectory,
                CancellationToken.None);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.Equal(Path.Combine(outputDirectory, "doctor-report.json"), reportPath);
            Assert.True(document.RootElement.TryGetProperty("generatedAt", out _));
            Assert.True(document.RootElement.TryGetProperty("tools", out _));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
