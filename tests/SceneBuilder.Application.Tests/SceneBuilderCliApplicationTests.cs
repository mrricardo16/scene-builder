using System.Text.Json;
using SceneBuilder.Application.Doctor;
using SceneBuilder.Cli;
using SceneBuilder.Composition;

namespace SceneBuilder.Application.Tests;

public sealed class SceneBuilderCliApplicationTests
{
    [Fact]
    public async Task RunAsync_writes_stable_help_for_help_forms()
    {
        var first = await RunAsync(["help"]);
        var second = await RunAsync(["--help"]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(first.StandardOutput, second.StandardOutput);
        Assert.Contains("capabilities", first.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(first.StandardError);
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_commands_without_creating_output()
    {
        var result = await RunAsync(["convert"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown command: convert", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_writes_deterministic_capabilities_text()
    {
        var first = await RunAsync(["capabilities"]);
        var second = await RunAsync(["capabilities", "--format", "text"]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.StandardOutput, second.StandardOutput);
        Assert.Contains("DOCTOR: Available", first.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("DWG_INPUT: Unsupported", first.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_writes_strict_deterministic_capabilities_json_without_machine_details()
    {
        var first = await RunAsync(["capabilities", "--format", "json"]);
        var second = await RunAsync(["capabilities", "--format", "json"]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.StandardOutput, second.StandardOutput);
        Assert.DoesNotContain(Path.GetPathRoot(Environment.CurrentDirectory)!, first.StandardOutput, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(first.StandardOutput);
        Assert.Equal("1.0", document.RootElement.GetProperty("contractVersion").GetString());
        Assert.Equal("DOCTOR", document.RootElement.GetProperty("capabilities")[0].GetProperty("code").GetString());
        Assert.Equal("available", document.RootElement.GetProperty("capabilities")[0].GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("capabilities")[0].GetProperty("diagnosticCode").ValueKind);
    }

    [Fact]
    public async Task RunAsync_rejects_an_unknown_capabilities_format()
    {
        var result = await RunAsync(["capabilities", "--format", "yaml"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unsupported format: yaml", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_preserves_doctor_command_output()
    {
        var result = await RunAsync(["doctor"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Scene Builder doctor / 环境诊断", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_preserves_the_existing_case_insensitive_doctor_command()
    {
        var result = await RunAsync(["DOCTOR"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Scene Builder doctor / 环境诊断", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_returns_cancelled_exit_code_when_doctor_observes_cancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var host = new SceneBuilderHost(
            new DoctorService([new CancelledProbe()]),
            new SceneCapabilityRegistry([new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Available }]));
        var result = await RunAsync(["doctor"], host, cancellationSource.Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("诊断已取消", result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<CliRunResult> RunAsync(
        string[] args,
        SceneBuilderHost? host = null,
        CancellationToken cancellationToken = default)
    {
        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        var application = new SceneBuilderCliApplication(
            host ?? CreateHost(),
            standardOutput,
            standardError,
            new DoctorReportWriter());

        var exitCode = await application.RunAsync(args, cancellationToken);
        return new CliRunResult(exitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static SceneBuilderHost CreateHost() => new(
        new DoctorService([new DotNetRuntimeProbe()]),
        new SceneCapabilityRegistry(
        [
            new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Available },
            new SceneCapability { Code = "DWG_INPUT", State = SceneCapabilityState.Unsupported }
        ]));

    private sealed record CliRunResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class CancelledProbe : IDoctorProbe
    {
        public Task<DoctorToolReport> ProbeAsync(DoctorOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DoctorToolReport());
        }
    }
}
