using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Cli;

public sealed class DoctorReportWriter
{
    private const string ReportFileName = "doctor-report.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<string> WriteAsync(
        DoctorReport report,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        if (File.Exists(fullOutputDirectory))
        {
            throw new ArgumentException($"The --output path must be a directory: {outputDirectory}", nameof(outputDirectory));
        }

        Directory.CreateDirectory(fullOutputDirectory);
        var reportPath = Path.Combine(fullOutputDirectory, ReportFileName);
        var json = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(reportPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        return reportPath;
    }
}
