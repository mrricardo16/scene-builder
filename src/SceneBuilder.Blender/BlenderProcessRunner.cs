using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SceneBuilder.Blender;

public sealed class BlenderProcessRunner : IBlenderProcessRunner
{
    public async Task<BlenderProcessResult> RunAsync(BlenderProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExecutablePath) || string.IsNullOrWhiteSpace(request.WorkingDirectory) || request.Timeout <= TimeSpan.Zero || request.MaximumOutputCharacters <= 0)
        {
            return new BlenderProcessResult { Status = BlenderProcessStatus.Failed };
        }

        using var nativeProcess = WindowsSuspendedProcess.TryCreate(request);
        if (nativeProcess is null)
        {
            return new BlenderProcessResult { Status = BlenderProcessStatus.Failed };
        }

        using var process = Process.GetProcessById(nativeProcess.ProcessId);
        using var processJob = WindowsProcessJob.TryAssign(nativeProcess.ProcessHandle);
        if (processJob is null)
        {
            nativeProcess.Terminate();
            return new BlenderProcessResult { Status = BlenderProcessStatus.Failed };
        }
        nativeProcess.Resume();

        var standardOutput = new BoundedTextCollector(request.MaximumOutputCharacters);
        var standardError = new BoundedTextCollector(request.MaximumOutputCharacters);
        var outputTask = DrainAsync(nativeProcess.StandardOutput, standardOutput);
        var errorTask = DrainAsync(nativeProcess.StandardError, standardError);
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(completion.Token);
            await Task.WhenAll(outputTask, errorTask).WaitAsync(completion.Token);
            return new BlenderProcessResult
            {
                Status = process.ExitCode == 0 ? BlenderProcessStatus.Succeeded : BlenderProcessStatus.Failed,
                ExitCode = process.ExitCode,
                StandardOutput = standardOutput.Value,
                StandardError = standardError.Value,
                OutputTruncated = standardOutput.IsTruncated || standardError.IsTruncated
            };
        }
        catch (OperationCanceledException)
        {
            processJob.Terminate();
            TryTerminateProcessTree(process);
            return new BlenderProcessResult
            {
                Status = cancellationToken.IsCancellationRequested ? BlenderProcessStatus.Cancelled : BlenderProcessStatus.TimedOut,
                ExitCode = process.HasExited ? process.ExitCode : null,
                StandardOutput = standardOutput.Value,
                StandardError = standardError.Value,
                OutputTruncated = standardOutput.IsTruncated || standardError.IsTruncated
            };
        }
    }

    private static async Task DrainAsync(StreamReader reader, BoundedTextCollector collector)
    {
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            collector.Append(buffer.AsSpan(0, read));
        }
    }

    private static void TryTerminateProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }


    private sealed class BoundedTextCollector
    {
        private readonly StringBuilder _builder = new();
        private readonly int _maximumCharacters;

        public BoundedTextCollector(int maximumCharacters) => _maximumCharacters = maximumCharacters;

        public bool IsTruncated { get; private set; }

        public string Value => _builder.ToString();

        public void Append(ReadOnlySpan<char> text)
        {
            var remaining = _maximumCharacters - _builder.Length;
            if (remaining <= 0)
            {
                IsTruncated = true;
                return;
            }

            _builder.Append(text[..Math.Min(remaining, text.Length)]);
            IsTruncated |= text.Length > remaining;
        }
    }

    private sealed class WindowsProcessJob : IDisposable
    {
        private IntPtr _handle;

        private WindowsProcessJob(IntPtr handle) => _handle = handle;

        public static WindowsProcessJob? TryAssign(IntPtr processHandle)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var job = new WindowsProcessJob(handle);
            try
            {
                if (!AssignProcessToJobObject(handle, processHandle))
                {
                    job.Dispose();
                    return null;
                }
            }
            catch (InvalidOperationException)
            {
                job.Dispose();
                return null;
            }

            return job;
        }

        public void Terminate()
        {
            if (_handle != IntPtr.Zero)
            {
                TerminateJobObject(_handle, 1);
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }

}
