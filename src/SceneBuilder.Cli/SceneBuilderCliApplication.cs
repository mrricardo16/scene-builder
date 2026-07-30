using SceneBuilder.Composition;
using SceneBuilder.Application;

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
                CliCommandKind.Analyze => await RunAnalyzeAsync(command.Analyze!, command.OutputFormat, cancellationToken),
                CliCommandKind.Plan => await RunPlanAsync(command.Plan!, command.OutputFormat, cancellationToken),
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

    private async Task<int> RunAnalyzeAsync(AnalyzeCommand command, CliOutputFormat outputFormat, CancellationToken cancellationToken)
    {
        var handler = _host.CadImportAnalysisHandler ?? throw new InvalidOperationException("CAD analysis is not configured.");
        var result = await handler.ExecuteAsync(command.Request, progress: null, cancellationToken);
        var output = outputFormat is CliOutputFormat.Json
            ? CliOutputWriter.SerializeAnalyzeJson(result)
            : CliOutputWriter.FormatAnalyzeText(result);
        await _standardOutput.WriteLineAsync(output);
        return result.Status switch
        {
            SceneOperationStatus.Succeeded or SceneOperationStatus.PartiallySucceeded => (int)CliExitCode.Success,
            SceneOperationStatus.Cancelled => (int)CliExitCode.Cancelled,
            SceneOperationStatus.Unsupported => (int)CliExitCode.CapabilityUnavailable,
            _ => (int)CliExitCode.Failed
        };
    }

    private async Task<int> RunPlanAsync(PlanCommand command, CliOutputFormat outputFormat, CancellationToken cancellationToken)
    {
        var service = _host.ConversionPlanService ?? throw new InvalidOperationException("Conversion plan is not configured.");
        return command.Operation switch
        {
            PlanCommandOperation.Create => await WriteDraftAsync("planCreate", await service.CreateDraftAsync(new CreateConversionPlanDraftRequest { AnalysisPath = command.InputPath, OutputRootDirectory = command.OutputRootDirectory }, cancellationToken), outputFormat),
            PlanCommandOperation.Validate => await WriteValidationAsync(await service.ValidateAsync(new ValidateConversionPlanRequest { PlanPath = command.InputPath, OutputRootDirectory = command.OutputRootDirectory }, cancellationToken), outputFormat),
            PlanCommandOperation.Freeze => await WriteFrozenAsync(await service.FreezeAsync(new FreezeConversionPlanRequest { PlanPath = command.InputPath, OutputRootDirectory = command.OutputRootDirectory }, cancellationToken), outputFormat),
            _ => throw new InvalidOperationException("The plan operation is not supported.")
        };
    }

    private async Task<int> WriteDraftAsync(string operation, ConversionPlanDraftResult result, CliOutputFormat format)
    {
        var output = format is CliOutputFormat.Json ? CliOutputWriter.SerializePlanJson(new { contractVersion = "1.0", operation, status = result.Status, planId = result.Draft?.PlanId, revision = result.Draft?.Revision, artifacts = result.Artifacts, diagnostics = result.Diagnostics }) : CliOutputWriter.FormatPlanText(operation, result.Draft?.PlanId ?? "Unknown", result.Draft?.Revision ?? 0, result.Status.ToString(), result.Artifacts, result.Diagnostics);
        await _standardOutput.WriteLineAsync(output);
        return Exit(result.Status);
    }

    private async Task<int> WriteValidationAsync(ConversionPlanValidationResult result, CliOutputFormat format)
    {
        var output = format is CliOutputFormat.Json ? CliOutputWriter.SerializePlanJson(new { contractVersion = "1.0", operation = "planValidate", status = result.Status, planId = result.PlanId, revision = result.Revision, validationStatus = result.ValidationStatus, artifacts = result.Artifacts, diagnostics = result.Diagnostics }) : CliOutputWriter.FormatPlanText("planValidate", result.PlanId, result.Revision, result.ValidationStatus.ToString(), result.Artifacts, result.Diagnostics);
        await _standardOutput.WriteLineAsync(output);
        return Exit(result.Status);
    }

    private async Task<int> WriteFrozenAsync(FrozenConversionPlanResult result, CliOutputFormat format)
    {
        var output = format is CliOutputFormat.Json ? CliOutputWriter.SerializePlanJson(new { contractVersion = "1.0", operation = "planFreeze", status = result.Status, planId = result.FrozenPlan?.Draft.PlanId, revision = result.FrozenPlan?.Draft.Revision, artifacts = result.Artifacts, diagnostics = result.Diagnostics }) : CliOutputWriter.FormatPlanText("planFreeze", result.FrozenPlan?.Draft.PlanId ?? "Unknown", result.FrozenPlan?.Draft.Revision ?? 0, result.Status.ToString(), result.Artifacts, result.Diagnostics);
        await _standardOutput.WriteLineAsync(output);
        return Exit(result.Status);
    }

    private static int Exit(SceneOperationStatus status) => status switch
    {
        SceneOperationStatus.Succeeded or SceneOperationStatus.PartiallySucceeded => (int)CliExitCode.Success,
        SceneOperationStatus.Cancelled => (int)CliExitCode.Cancelled,
        SceneOperationStatus.Unsupported => (int)CliExitCode.CapabilityUnavailable,
        _ => (int)CliExitCode.Failed
    };
}
