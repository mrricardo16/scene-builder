using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Application;
using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Cli;

public static class CliOutputWriter
{
    private const string Help = """
        Usage:
          scene-builder doctor [--output <directory>] [--blender-path <file>] [--tiles-path <file>]
          scene-builder capabilities [--format text|json]
          scene-builder analyze --input <file> --output <directory> [--rules <file>] [--unit <meters|millimeters|centimeters>] [--format text|json]
          scene-builder help
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string GetHelp() => Help;

    public static string FormatCapabilitiesText(IReadOnlyList<SceneCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var lines = new List<string> { "Scene Builder capabilities" };
        lines.AddRange(capabilities.Select(capability => $"- {capability.Code}: {capability.State}"));
        return string.Join(Environment.NewLine, lines);
    }

    public static string SerializeCapabilitiesJson(IReadOnlyList<SceneCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return JsonSerializer.Serialize(
            new CapabilityDocument { Capabilities = capabilities },
            JsonOptions);
    }

    public static void WriteDoctorSummary(TextWriter output, DoctorReport report)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        output.WriteLine("Scene Builder doctor / 环境诊断");
        foreach (var tool in report.Tools)
        {
            var status = tool.Status == DoctorToolStatus.Available
                ? "Available / 可用"
                : "Unavailable / 不可用";
            output.WriteLine($"- {GetDisplayName(tool.Name)}: {status}");

            if (!string.IsNullOrWhiteSpace(tool.ConfiguredPath))
            {
                output.WriteLine($"  Path / 路径: {tool.ConfiguredPath}");
            }

            if (!string.IsNullOrWhiteSpace(tool.Version))
            {
                output.WriteLine($"  Version / 版本: {tool.Version}");
            }

            output.WriteLine($"  Detail / 说明: {tool.Detail}");
        }
    }

    public static string FormatAnalyzeText(CadImportAnalysisResult result) => string.Join(Environment.NewLine,
    [
        "Scene Builder CAD analysis",
        $"Status: {result.Status}",
        $"Input: {result.Input.InputKind}",
        $"Unit: {result.Input.Unit}",
        $"Layers: {result.Structure.Layers.Count}",
        $"Blocks: {result.Structure.Blocks.Count}",
        $"Entities: {result.Structure.EntityTypes.Sum(entityType => entityType.EntityCount)}",
        $"Valid contours: {result.Geometry.ValidContourCount}",
        $"Unclassified: {result.Classification.UnclassifiedCount}",
        $"Artifact: {result.Artifacts.FirstOrDefault()?.RelativePath ?? "None"}"
    ]);

    public static string SerializeAnalyzeJson(CadImportAnalysisResult result) => JsonSerializer.Serialize(result, JsonOptions);

    private static string GetDisplayName(string toolName) => toolName switch
    {
        "dotnet" => ".NET runtime",
        "blender" => "Blender",
        "tiles" => "3D Tiles converter",
        _ => toolName
    };

    private sealed record CapabilityDocument
    {
        public string ContractVersion { get; init; } = "1.0";

        public IReadOnlyList<SceneCapability> Capabilities { get; init; } = Array.Empty<SceneCapability>();
    }
}
