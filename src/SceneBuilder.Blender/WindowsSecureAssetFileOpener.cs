using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace SceneBuilder.Blender;

internal interface ISecureAssetFileOpener
{
    SecureAssetOpenResult OpenRead(string assetRootDirectory, string relativeGlbPath);
}

internal sealed record SecureAssetOpenResult(SecureAssetFile? File, string? DiagnosticCode)
{
    public static SecureAssetOpenResult Succeeded(SecureAssetFile file) => new(file, null);

    public static SecureAssetOpenResult Failed(string code) => new(null, code);
}

internal sealed class SecureAssetFile : IDisposable
{
    private SafeFileHandle? _handle;

    internal SecureAssetFile(SafeFileHandle handle) => _handle = handle;

    public FileStream OpenReadStream()
    {
        var handle = _handle ?? throw new ObjectDisposedException(nameof(SecureAssetFile));
        return new FileStream(new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false), FileAccess.Read, 4096, isAsync: false);
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }
}

internal interface IWindowsSecureAssetNativeApi
{
    SecureAssetOpenNativeResult OpenRootDirectory(string path);

    SecureAssetOpenNativeResult OpenRelativeDirectory(SafeFileHandle parentDirectory, string name);

    SecureAssetOpenNativeResult OpenRelativeFile(SafeFileHandle parentDirectory, string name);
}

internal sealed record SecureAssetOpenNativeResult(SafeFileHandle? Handle, bool IsDirectory, bool IsReparsePoint)
{
    public static SecureAssetOpenNativeResult Failed() => new(null, false, false);

    public static SecureAssetOpenNativeResult ReparsePoint() => new(null, false, true);
}

internal sealed class WindowsSecureAssetFileOpener : ISecureAssetFileOpener
{
    private readonly IWindowsSecureAssetNativeApi _nativeApi;

    public WindowsSecureAssetFileOpener(IWindowsSecureAssetNativeApi? nativeApi = null)
    {
        _nativeApi = nativeApi ?? new WindowsSecureAssetNativeApi();
    }

    public SecureAssetOpenResult OpenRead(string assetRootDirectory, string relativeGlbPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return SecureAssetOpenResult.Failed("ASSET_SECURE_OPEN_UNSUPPORTED");
        }

        if (string.IsNullOrWhiteSpace(assetRootDirectory) || !TryGetSafeSegments(relativeGlbPath, out var segments))
        {
            return SecureAssetOpenResult.Failed("ASSET_PATH_INVALID");
        }

        var root = _nativeApi.OpenRootDirectory(assetRootDirectory);
        if (root.IsReparsePoint)
        {
            root.Handle?.Dispose();
            return SecureAssetOpenResult.Failed("ASSET_REPARSE_POINT_REJECTED");
        }

        if (root.Handle is null || !root.IsDirectory)
        {
            root.Handle?.Dispose();
            return SecureAssetOpenResult.Failed("ASSET_PATH_INVALID");
        }

        using var currentDirectory = root.Handle;
        SafeFileHandle parent = currentDirectory;
        var heldDirectories = new List<SafeFileHandle>();
        try
        {
            for (var index = 0; index < segments.Length - 1; index++)
            {
                var directory = _nativeApi.OpenRelativeDirectory(parent, segments[index]);
                if (directory.IsReparsePoint)
                {
                    directory.Handle?.Dispose();
                    return SecureAssetOpenResult.Failed("ASSET_REPARSE_POINT_REJECTED");
                }

                if (directory.Handle is null || !directory.IsDirectory)
                {
                    directory.Handle?.Dispose();
                    return SecureAssetOpenResult.Failed("ASSET_PATH_INVALID");
                }

                heldDirectories.Add(directory.Handle);
                parent = directory.Handle;
            }

            var file = _nativeApi.OpenRelativeFile(parent, segments[^1]);
            if (file.IsReparsePoint)
            {
                file.Handle?.Dispose();
                return SecureAssetOpenResult.Failed("ASSET_REPARSE_POINT_REJECTED");
            }

            if (file.Handle is null || file.IsDirectory)
            {
                file.Handle?.Dispose();
                return SecureAssetOpenResult.Failed("ASSET_PATH_INVALID");
            }

            return SecureAssetOpenResult.Succeeded(new SecureAssetFile(file.Handle));
        }
        finally
        {
            foreach (var directory in heldDirectories)
            {
                directory.Dispose();
            }
        }
    }

    private static bool TryGetSafeSegments(string value, out string[] segments)
    {
        segments = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || Uri.TryCreate(value, UriKind.Absolute, out _) ||
            !string.Equals(Path.GetExtension(value), ".glb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        segments = value.Split(['\\', '/'], StringSplitOptions.None);
        return segments.Length > 0 && segments.All(segment => !string.IsNullOrWhiteSpace(segment) && segment is not "." and not "..");
    }
}

internal sealed class WindowsSecureAssetNativeApi : IWindowsSecureAssetNativeApi
{
    private const uint GenericRead = 0x80000000;
    private const uint FileReadData = 0x00000001;
    private const uint FileReadAttributes = 0x00000080;
    private const uint Synchronize = 0x00100000;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileOpen = 0x00000001;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileSynchronousIoNonalert = 0x00000020;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjCaseInsensitive = 0x00000040;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;

    public SecureAssetOpenNativeResult OpenRootDirectory(string path)
    {
        var handle = CreateFile(path, GenericRead, FileShareRead, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        return Inspect(handle);
    }

    public SecureAssetOpenNativeResult OpenRelativeDirectory(SafeFileHandle parentDirectory, string name) =>
        OpenRelative(parentDirectory, name, FileDirectoryFile | FileSynchronousIoNonalert | FileOpenReparsePoint);

    public SecureAssetOpenNativeResult OpenRelativeFile(SafeFileHandle parentDirectory, string name) =>
        OpenRelative(parentDirectory, name, FileNonDirectoryFile | FileSynchronousIoNonalert | FileOpenReparsePoint);

    private static SecureAssetOpenNativeResult OpenRelative(SafeFileHandle parentDirectory, string name, uint createOptions)
    {
        var namePointer = Marshal.StringToHGlobalUni(name);
        try
        {
            var unicodeName = new UnicodeString { Length = checked((ushort)(name.Length * sizeof(char))), MaximumLength = checked((ushort)((name.Length + 1) * sizeof(char))), Buffer = namePointer };
            var nameStructure = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            try
            {
                Marshal.StructureToPtr(unicodeName, nameStructure, false);
                var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parentDirectory.DangerousGetHandle(), ObjectName = nameStructure, Attributes = ObjCaseInsensitive };
                var status = NtCreateFile(out var handle, FileReadData | FileReadAttributes | Synchronize, ref attributes, out _, IntPtr.Zero, 0, FileShareRead, FileOpen, createOptions, IntPtr.Zero, 0);
                return status < 0 ? SecureAssetOpenNativeResult.Failed() : Inspect(handle);
            }
            finally
            {
                Marshal.FreeHGlobal(nameStructure);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(namePointer);
        }
    }

    private static SecureAssetOpenNativeResult Inspect(SafeFileHandle handle)
    {
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return SecureAssetOpenNativeResult.Failed();
        }

        var info = new FileAttributeTagInfo();
        if (!GetFileInformationByHandleEx(handle, FileInfoByHandleClass.FileAttributeTagInfo, ref info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            handle.Dispose();
            return SecureAssetOpenNativeResult.Failed();
        }

        return new SecureAssetOpenNativeResult(handle, (info.FileAttributes & FileAttributeDirectory) != 0, (info.FileAttributes & FileAttributeReparsePoint) != 0);
    }

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock { public IntPtr Status; public IntPtr Information; }
    [StructLayout(LayoutKind.Sequential)] private struct FileAttributeTagInfo { public uint FileAttributes; public uint ReparseTag; }
    private enum FileInfoByHandleClass { FileAttributeTagInfo = 9 }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, FileInfoByHandleClass fileInformationClass, ref FileAttributeTagInfo fileInformation, uint bufferSize);
    [DllImport("ntdll.dll")] private static extern int NtCreateFile(out SafeFileHandle fileHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, out IoStatusBlock ioStatusBlock, IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);
}
