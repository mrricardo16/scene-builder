using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace SceneBuilder.Blender;

internal sealed class WindowsSuspendedProcess : IDisposable
{
    private const uint CreateSuspended = 0x00000004;
    private const uint StartfUseStdHandles = 0x00000100;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;

    private WindowsSuspendedProcess(IntPtr processHandle, IntPtr threadHandle, int processId, StreamReader standardOutput, StreamReader standardError)
    {
        _processHandle = processHandle;
        _threadHandle = threadHandle;
        ProcessId = processId;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public IntPtr ProcessHandle => _processHandle;

    public int ProcessId { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public static WindowsSuspendedProcess? TryCreate(BlenderProcessRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        if (!CreatePipe(out var outputRead, out var outputWrite, ref security, 0) || !CreatePipe(out var errorRead, out var errorWrite, ref security, 0))
        {
            return null;
        }

        SetHandleInformation(outputRead, 1, 0);
        SetHandleInformation(errorRead, 1, 0);
        var startup = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Flags = StartfUseStdHandles,
            StandardInput = GetStdHandle(-10),
            StandardOutput = outputWrite,
            StandardError = errorWrite
        };
        if (!CreateProcess(null, BuildCommandLine(request), IntPtr.Zero, IntPtr.Zero, true, CreateSuspended, IntPtr.Zero, request.WorkingDirectory, ref startup, out var information))
        {
            CloseHandle(outputRead); CloseHandle(outputWrite); CloseHandle(errorRead); CloseHandle(errorWrite);
            return null;
        }

        CloseHandle(outputWrite);
        CloseHandle(errorWrite);
        var output = new StreamReader(new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read, 4096, true), Encoding.UTF8, true);
        var error = new StreamReader(new FileStream(new SafeFileHandle(errorRead, true), FileAccess.Read, 4096, true), Encoding.UTF8, true);
        return new WindowsSuspendedProcess(information.Process, information.Thread, checked((int)information.ProcessId), output, error);
    }

    public void Resume() => ResumeThread(_threadHandle);

    public void Terminate() => TerminateProcess(_processHandle, 1);

    public void Dispose()
    {
        StandardOutput.Dispose();
        StandardError.Dispose();
        if (_threadHandle != IntPtr.Zero) { CloseHandle(_threadHandle); _threadHandle = IntPtr.Zero; }
        if (_processHandle != IntPtr.Zero) { CloseHandle(_processHandle); _processHandle = IntPtr.Zero; }
    }

    private static string BuildCommandLine(BlenderProcessRequest request) => string.Join(" ", new[] { request.ExecutablePath }.Concat(request.Arguments).Select(Quote));

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0) return value;
        var builder = new StringBuilder("\"");
        var slashCount = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashCount++; continue; }
            if (character == '"') builder.Append('\\', (slashCount * 2) + 1);
            else builder.Append('\\', slashCount);
            builder.Append(character);
            slashCount = 0;
        }
        builder.Append('\\', slashCount * 2).Append('"');
        return builder.ToString();
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int Size; public string? Reserved; public string? Desktop; public string? Title; public int X; public int Y; public int XSize; public int YSize; public int XChars; public int YChars; public int Fill; public uint Flags; public short Show; public short ReservedCount; public IntPtr ReservedBytes; public IntPtr StandardInput; public IntPtr StandardOutput; public IntPtr StandardError; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process; public IntPtr Thread; public uint ProcessId; public uint ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string? application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
