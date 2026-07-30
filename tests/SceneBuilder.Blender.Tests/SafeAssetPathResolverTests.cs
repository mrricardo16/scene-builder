using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class SafeAssetPathResolverTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "scene-builder-safe-assets-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_returns_existing_glb_within_asset_root()
    {
        var path = CreateAsset("equipment/pump.glb");
        var result = new SafeAssetPathResolver().Resolve(_rootDirectory, "equipment/pump.glb");
        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(path), result.FullPath);
    }

    [Theory]
    [InlineData("../outside.glb")]
    [InlineData("C:/outside.glb")]
    [InlineData("//server/share/asset.glb")]
    [InlineData("https://example.test/asset.glb")]
    [InlineData("equipment/pump.gltf")]
    [InlineData("equipment//pump.glb")]
    public void Resolve_rejects_unsafe_or_non_glb_paths(string relativePath)
    {
        CreateAsset("equipment/pump.glb");
        var result = new SafeAssetPathResolver().Resolve(_rootDirectory, relativePath);
        Assert.False(result.IsSuccess);
        Assert.Null(result.FullPath);
        Assert.Equal("ASSET_PATH_INVALID", result.DiagnosticCode);
    }

    [Fact]
    public void Resolve_rejects_any_reparse_point_between_root_and_asset()
    {
        CreateAsset("equipment/pump.glb");
        var reparsePath = Path.Combine(_rootDirectory, "equipment");
        var result = new SafeAssetPathResolver(new FlaggedReparsePointInspector(reparsePath)).Resolve(_rootDirectory, "equipment/pump.glb");
        Assert.False(result.IsSuccess);
        Assert.Equal("ASSET_PATH_REPARSE_POINT", result.DiagnosticCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory)) Directory.Delete(_rootDirectory, recursive: true);
    }

    private string CreateAsset(string relativePath)
    {
        var path = Path.Combine(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        return path;
    }

    private sealed class FlaggedReparsePointInspector(string flaggedPath) : IReparsePointInspector
    {
        private readonly string _flaggedPath = Path.GetFullPath(flaggedPath);
        public bool IsReparsePoint(string path) => string.Equals(Path.GetFullPath(path), _flaggedPath, StringComparison.OrdinalIgnoreCase);
    }
}
