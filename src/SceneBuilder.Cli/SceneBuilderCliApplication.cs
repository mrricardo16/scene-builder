using SceneBuilder.Composition;

namespace SceneBuilder.Cli;

public sealed class SceneBuilderCliApplication(
    SceneBuilderHost host,
    TextWriter standardOutput,
    TextWriter standardError,
    DoctorReportWriter doctorReportWriter)
{
    private readonly SceneBuilderHost _host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly TextWriter _standardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
    private readonly TextWriter _standardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
    private readonly DoctorReportWriter _doctorReportWriter = doctorReportWriter ?? throw new ArgumentNullException(nameof(doctorReportWriter));

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = CliCommandParser.Parse(args);
        if (command.Kind is CliCommandKind.Invalid)
        {
            await _standardError.WriteLineAsync($"Error: {command.Error}");
            await _standardError.WriteLineAsync(CliOutputWriter.GetHelp());
            return (int)CliExitCode.InvalidArguments;
        }

        try
        {
            return command.Kind switch
            {
                CliCommandKind.Help => await WriteHelpAsync(),
                CliCommandKind.Capabilities => await WriteCapabilitiesAsync(command.OutputFormat),
                CliCommandKind.Doctor => await RunDoctorAsync(command.Doctor!, cancellationToken),
                _ => throw new InvalidOperationException("The CLI command kind is not supported.")
            };
        }
        catch (ArgumentException exception)
        {
            await _standardError.WriteLineAsync($"Error: {exception.Message}");
            return (int)CliExitCode.InvalidArguments;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _standardError.WriteLineAsync("Doctor inspection was cancelled / 诊断已取消。");
            return (int)CliExitCode.Cancelled;
        }
        catch (Exception)
        {
            await _standardError.WriteLineAsync("Scene Builder command failed.");
            return (int)CliExitCode.Failed;
        }
    }

    private async Task<int> WriteHelpAsync()
    {
        await _standardOutput.WriteLineAsync(CliOutputWriter.GetHelp());
        return (int)CliExitCode.Success;
    }

    private async Task<int> WriteCapabilitiesAsync(CliOutputFormat outputFormat)
    {
        var capabilities = _host.CapabilityRegistry.GetCapabilities();
        var output = outputFormat is CliOutputFormat.Json
            ? CliOutputWriter.SerializeCapabilitiesJson(capabilities)
            : CliOutputWriter.FormatCapabilitiesText(capabilities);
        await _standardOutput.WriteLineAsync(output);
        return (int)CliExitCode.Success;
    }

    private async Task<int> RunDoctorAsync(DoctorCommand command, CancellationToken cancellationToken)
    {
        var report = await _host.DoctorService.InspectAsync(command.DoctorOptions, cancellationToken);
        CliOutputWriter.WriteDoctorSummary(_standardOutput, report);

        if (command.OutputDirectory is not null)
        {
            var reportPath = await _doctorReportWriter.WriteAsync(report, command.OutputDirectory, cancellationToken);
            await _standardOutput.WriteLineAsync($"Report / 报告: {reportPath}");
        }

        return (int)CliExitCode.Success;
    }
}
