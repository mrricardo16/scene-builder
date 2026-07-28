using System.Diagnostics;
using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Infrastructure.Doctor;

public sealed class SystemFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
}

public sealed class ProcessExecutableVersionReader(TimeSpan timeout) : IExecutableVersionReader
{
    public async Task<ExecutableVersionResult> ReadVersionAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--version");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return ExecutableVersionResult.Unavailable("Executable process did not start.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return ExecutableVersionResult.Unavailable($"Executable version check timed out after {timeout.TotalSeconds:0} seconds.");
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            if (process.ExitCode != 0)
            {
                var error = FirstNonEmptyLine(standardError) ?? "No error text was returned.";
                return ExecutableVersionResult.Unavailable(
                    $"Executable returned exit code {process.ExitCode}: {error}");
            }

            var version = FirstNonEmptyLine(standardOutput) ?? FirstNonEmptyLine(standardError);
            return string.IsNullOrWhiteSpace(version)
                ? ExecutableVersionResult.Unavailable("Executable returned no version text.")
                : ExecutableVersionResult.Success(version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            return ExecutableVersionResult.Unavailable($"Unable to start executable: {exception.Message}");
        }
    }

    private static string? FirstNonEmptyLine(string text) => text
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    private static void TryKill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
