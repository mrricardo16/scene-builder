using System.Buffers.Binary;
using System.Text;
using SceneBuilder.Application;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class BlenderAssetStagerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "scene-builder-asset-stager-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Stage_validates_and_copies_each_used_asset_once_in_ordinal_anonymous_order()
    {
        var sourceA = CreateValidGlb("source/a.glb");
        var sourceB = CreateValidGlb("source/b.glb");
        var originalA = File.ReadAllBytes(sourceA);
        var originalB = File.ReadAllBytes(sourceB);
        var workDirectory = Path.Combine(_temporaryDirectory, "work");
        CadAssetResolution[] resolutions =
        [
            Resolution("z-asset", "source/b.glb"),
            Resolution("a-asset", "source/a.glb"),
            Resolution("a-asset", "source/a.glb")
        ];

        var result = new BlenderAssetStager().Stage(resolutions, _temporaryDirectory, workDirectory);

        Assert.True(result.IsSuccess);
        Assert.Equal(["a-asset", "z-asset"], result.Assets.Select(asset => asset.AssetId));
        Assert.Equal(["assets/asset-000001.glb", "assets/asset-000002.glb"], result.Assets.Select(asset => asset.ManifestRelativePath));
        Assert.Equal(originalA, File.ReadAllBytes(sourceA));
        Assert.Equal(originalB, File.ReadAllBytes(sourceB));
        Assert.All(result.Assets, asset => Assert.True(File.Exists(Path.Combine(workDirectory, asset.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [Fact]
    public void Stage_rejects_invalid_source_glb_without_publishing_staged_assets()
    {
        var invalidPath = Path.Combine(_temporaryDirectory, "source", "invalid.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(invalidPath)!);
        File.WriteAllBytes(invalidPath, [1, 2, 3]);

        var result = new BlenderAssetStager().Stage([Resolution("invalid", "source/invalid.glb")], _temporaryDirectory, Path.Combine(_temporaryDirectory, "work"));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Assets);
        Assert.Equal("ASSET_SOURCE_GLB_INVALID", result.DiagnosticCode);
        Assert.False(Directory.Exists(Path.Combine(_temporaryDirectory, "work", "assets")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static CadAssetResolution Resolution(string assetId, string relativePath) => new()
    {
        SemanticObjectId = "semantic-1",
        Kind = CadAssetKind.StaticFacility,
        Status = CadAssetResolutionStatus.Resolved,
        Asset = new CadAssetDefinition { AssetId = assetId, Kind = CadAssetKind.StaticFacility, RelativeGlbPath = relativePath },
        BindingId = "binding-1"
    };

    private string CreateValidGlb(string relativePath)
    {
        var path = Path.Combine(_temporaryDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
}
