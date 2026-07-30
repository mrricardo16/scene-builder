using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class SecureAssetFileOpenerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scene-builder-secure-open-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void OpenRead_returns_a_live_file_handle_that_can_be_validated_after_its_path_changes()
    {
        var assetPath = CreateMinimalGlb("assets/source.glb");
        using var opened = Assert.IsType<SecureAssetFile>(new WindowsSecureAssetFileOpener().OpenRead(_root, "assets/source.glb").File);
        var replacementPath = Path.Combine(_root, "assets", "replacement.glb");
        Assert.Throws<IOException>(() => File.Move(assetPath, replacementPath));

        using var stream = opened.OpenReadStream();
        var validation = new BinaryGlbValidator().Validate(stream, leaveOpen: true);

        Assert.True(validation.IsValid);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void OpenRead_rejects_a_reparse_point_reported_for_any_path_segment()
    {
        CreateMinimalGlb("assets/source.glb");
        var result = new WindowsSecureAssetFileOpener(new ReparseRootNativeApi()).OpenRead(_root, "assets/source.glb");

        Assert.Null(result.File);
        Assert.Equal("ASSET_REPARSE_POINT_REJECTED", result.DiagnosticCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateMinimalGlb(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"nodes\":[{}]}");
        var paddedLength = (json.Length + 3) & ~3;
        var bytes = new byte[20 + paddedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)paddedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4E4F534A);
        json.CopyTo(bytes, 20);
        Array.Fill(bytes, (byte)0x20, 20 + json.Length, paddedLength - json.Length);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class ReparseRootNativeApi : IWindowsSecureAssetNativeApi
    {
        public SecureAssetOpenNativeResult OpenRootDirectory(string path) => SecureAssetOpenNativeResult.ReparsePoint();

        public SecureAssetOpenNativeResult OpenRelativeDirectory(Microsoft.Win32.SafeHandles.SafeFileHandle parentDirectory, string name) => SecureAssetOpenNativeResult.Failed();

        public SecureAssetOpenNativeResult OpenRelativeFile(Microsoft.Win32.SafeHandles.SafeFileHandle parentDirectory, string name) => SecureAssetOpenNativeResult.Failed();
    }
}
