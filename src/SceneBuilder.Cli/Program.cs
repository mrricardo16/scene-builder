using System.Text;
using SceneBuilder.Application.Doctor;
using SceneBuilder.Infrastructure.Doctor;

namespace SceneBuilder.Cli;

public static class Program
{
    private static readonly TimeSpan ExecutableVersionTimeout = TimeSpan.FromSeconds(10);

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || !string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync("Usage: scene-builder doctor [--output <directory>] [--blender-path <file>] [--tiles-path <file>]");
            return 2;
        }

        if (!DoctorCommandLineParser.TryParse(args[1..], out var command, out var error))
        {
            await Console.Error.WriteLineAsync($"Error: {error}");
            await Console.Error.WriteLineAsync("Usage: scene-builder doctor [--output <directory>] [--blender-path <file>] [--tiles-path <file>]");
            return 2;
        }

        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        try
        {
            var report = await CreateDoctorService().InspectAsync(command!.DoctorOptions, cancellationSource.Token);
            PrintHumanReadableSummary(report);

            if (command.OutputDirectory is not null)
            {
                var reportPath = await new DoctorReportWriter().WriteAsync(
                    report,
                    command.OutputDirectory,
                    cancellationSource.Token);
                await Console.Out.WriteLineAsync($"Report / 报告: {reportPath}");
            }

            return 0;
        }
        catch (ArgumentException exception)
        {
            await Console.Error.WriteLineAsync($"Error: {exception.Message}");
            return 2;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Doctor inspection was cancelled / 诊断已取消。");
            return 3;
        }
    }

    private static DoctorService CreateDoctorService()
    {
        var fileSystem = new SystemFileSystem();
        var versionReader = new ProcessExecutableVersionReader(ExecutableVersionTimeout);
        return new DoctorService(
        [
            new DotNetRuntimeProbe(),
            new ConfiguredExecutableProbe(DoctorTool.Blender, fileSystem, versionReader),
            new ConfiguredExecutableProbe(DoctorTool.TilesConverter, fileSystem, versionReader)
        ]);
    }

    private static void PrintHumanReadableSummary(DoctorReport report)
    {
        Console.WriteLine("Scene Builder doctor / 环境诊断");
        foreach (var tool in report.Tools)
        {
            var status = tool.Status == DoctorToolStatus.Available
                ? "Available / 可用"
                : "Unavailable / 不可用";
            Console.WriteLine($"- {GetDisplayName(tool.Name)}: {status}");

            if (!string.IsNullOrWhiteSpace(tool.ConfiguredPath))
            {
                Console.WriteLine($"  Path / 路径: {tool.ConfiguredPath}");
            }

            if (!string.IsNullOrWhiteSpace(tool.Version))
            {
                Console.WriteLine($"  Version / 版本: {tool.Version}");
            }

            Console.WriteLine($"  Detail / 说明: {tool.Detail}");
        }
    }

    private static string GetDisplayName(string toolName) => toolName switch
    {
        "dotnet" => ".NET runtime",
        "blender" => "Blender",
        "tiles" => "3D Tiles converter",
        _ => toolName
    };
}
