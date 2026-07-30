using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SceneBuilder.Tiles;
using Xunit;

namespace SceneBuilder.Tiles.Tests;

public sealed class TilesetGeneratorTests : IDisposable
{
    private readonly string _packageDirectory = Path.Combine(Path.GetTempPath(), "scene-builder-tileset-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_publishes_a_stable_root_and_three_direct_glb_leaves()
    {
        await CreatePackageAsync();

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest
        {
            ScenePackageDirectory = _packageDirectory,
            Policy = new TilesetGenerationPolicy { RootGeometricErrorMeters = 100d, MinimumBoundingHalfExtentMeters = 0.001d }
        }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.Succeeded, result.Status);
        Assert.Equal(4, result.TileCount);
        Assert.Equal(3, result.IncludedPartitionCount);
        Assert.True(File.Exists(Path.Combine(_packageDirectory, "tileset.json")));
    }

    [Fact]
    public async Task GenerateAsync_writes_a_content_free_root_and_leaf_tiles_without_children()
    {
        await CreatePackageAsync();
        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Assert.IsType<string>(result.TilesetPath)));
        var root = document.RootElement.GetProperty("root");

        Assert.False(root.TryGetProperty("content", out _));
        Assert.All(root.GetProperty("children").EnumerateArray(), leaf =>
        {
            Assert.False(leaf.TryGetProperty("children", out _));
            Assert.False(leaf.TryGetProperty("refine", out _));
        });
    }

    [Fact]
    public async Task GenerateAsync_excludes_failed_partitions_and_reports_partial_success_when_allowed()
    {
        await CreatePackageAsync(includeFailedPartition: true);

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest
        {
            ScenePackageDirectory = _packageDirectory,
            Policy = new TilesetGenerationPolicy { RootGeometricErrorMeters = 100d, MinimumBoundingHalfExtentMeters = 0.001d, AllowPartialScenePackage = true }
        }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.PartiallySucceeded, result.Status);
        Assert.Equal(4, result.TileCount);
        Assert.Equal(3, result.IncludedPartitionCount);
        Assert.Equal(1, result.ExcludedPartitionCount);
    }

    [Fact]
    public async Task GenerateAsync_rejects_an_existing_tileset_without_overwriting_it()
    {
        await CreatePackageAsync();
        var tilesetPath = Path.Combine(_packageDirectory, "tileset.json");
        await File.WriteAllTextAsync(tilesetPath, "preserve-existing-tileset");

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.Failed, result.Status);
        Assert.Equal("preserve-existing-tileset", await File.ReadAllTextAsync(tilesetPath));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TILESET_ALREADY_EXISTS");
    }

    [Fact]
    public async Task GenerateAsync_does_not_publish_when_a_scene_package_uri_escapes_its_root()
    {
        await CreatePackageAsync();
        var indexPath = Path.Combine(_packageDirectory, "scene-package.json");
        var index = await File.ReadAllTextAsync(indexPath);
        await File.WriteAllTextAsync(indexPath, index.Replace("partitions/partition-global.glb", "../outside.glb", StringComparison.Ordinal));

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.Failed, result.Status);
        Assert.False(File.Exists(Path.Combine(_packageDirectory, "tileset.json")));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TILESET_INPUT_PACKAGE_INVALID");
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_tileset_missing_the_required_asset_object()
    {
        await CreatePackageAsync();
        var generated = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);
        var validJson = await File.ReadAllTextAsync(Assert.IsType<string>(generated.TilesetPath));
        var invalidPath = Path.Combine(_packageDirectory, "missing-asset.json");
        await File.WriteAllTextAsync(invalidPath, validJson.Replace("\"asset\":{\"version\":\"1.1\"},", string.Empty, StringComparison.Ordinal));

        var validation = await new TilesetValidator().ValidateAsync(_packageDirectory, invalidPath, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "TILESET_JSON_INVALID");
    }

    [Fact]
    public async Task GenerateAsync_rejects_an_artifact_uri_with_an_empty_path_segment()
    {
        await CreatePackageAsync();
        var indexPath = Path.Combine(_packageDirectory, "scene-package.json");
        var index = await File.ReadAllTextAsync(indexPath);
        await File.WriteAllTextAsync(indexPath, index.Replace("partitions/partition-global.glb", "partitions//partition-global.glb", StringComparison.Ordinal));

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.Failed, result.Status);
        Assert.False(File.Exists(Path.Combine(_packageDirectory, "tileset.json")));
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_leaf_box_that_does_not_cover_its_partition_content_bounds()
    {
        await CreatePackageAsync();
        var generated = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);
        var validJson = await File.ReadAllTextAsync(Assert.IsType<string>(generated.TilesetPath));
        var invalidPath = Path.Combine(_packageDirectory, "undersized-leaf.json");
        await File.WriteAllTextAsync(invalidPath, validJson.Replace("\"box\":[5,5,5,5,0,0,0,5,0,0,0,5]", "\"box\":[5,5,5,0.1,0,0,0,0.1,0,0,0,0.1]", StringComparison.Ordinal));

        var validation = await new TilesetValidator().ValidateAsync(_packageDirectory, invalidPath, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "TILESET_VALIDATION_FAILED");
    }

    [Fact]
    public async Task GenerateAsync_excludes_a_successful_partition_without_computed_content_bounds_when_partial_is_allowed()
    {
        await CreatePackageAsync();
        var indexPath = Path.Combine(_packageDirectory, "scene-package.json");
        var index = await File.ReadAllTextAsync(indexPath);
        const string firstBounds = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":10,\"MaxY\":10,\"MaxZ\":10,\"State\":2}";
        const string notEvaluated = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":0,\"MaxY\":0,\"MaxZ\":0,\"State\":0}";
        await File.WriteAllTextAsync(indexPath, index.Replace($"\"contentBounds\":{firstBounds},\"artifactPath\":\"partitions/partition-x-p000000-y-p000000.glb\"", $"\"contentBounds\":{notEvaluated},\"artifactPath\":\"partitions/partition-x-p000000-y-p000000.glb\"", StringComparison.Ordinal));

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory, Policy = new TilesetGenerationPolicy { AllowPartialScenePackage = true } }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.PartiallySucceeded, result.Status);
        Assert.Equal(3, result.TileCount);
        Assert.Equal(2, result.IncludedPartitionCount);
        Assert.Equal(1, result.ExcludedPartitionCount);
    }

    [Fact]
    public async Task GenerateAsync_does_not_modify_the_scene_package_or_referenced_glbs()
    {
        await CreatePackageAsync();
        var inputPaths = Directory.GetFiles(_packageDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("tileset.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var inputHashes = inputPaths.ToDictionary(path => path, ComputeFileHash, StringComparer.Ordinal);

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.Succeeded, result.Status);
        Assert.All(inputHashes, input => Assert.Equal(input.Value, ComputeFileHash(input.Key)));
    }

    [Fact]
    public async Task GenerateAsync_handles_a_thousand_valid_partitions()
    {
        await CreateLargePackageAsync(1_000);

        var result = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest { ScenePackageDirectory = _packageDirectory }, CancellationToken.None);

        Assert.Equal(TilesetGenerationStatus.Succeeded, result.Status);
        Assert.Equal(1_001, result.TileCount);
        Assert.Equal(1_000, result.IncludedPartitionCount);
        Assert.True((await new TilesetValidator().ValidateAsync(_packageDirectory, Assert.IsType<string>(result.TilesetPath), CancellationToken.None)).IsValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    private async Task CreatePackageAsync(bool includeFailedPartition = false)
    {
        var partitions = Path.Combine(_packageDirectory, "partitions");
        Directory.CreateDirectory(partitions);
        foreach (var name in new[] { "partition-x-p000001-y-p000000.glb", "partition-global.glb", "partition-x-p000000-y-p000000.glb" })
        {
            WriteMinimalGlb(Path.Combine(partitions, name));
        }

        const string firstBounds = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":10,\"MaxY\":10,\"MaxZ\":10,\"State\":2}";
        const string secondBounds = "{\"MinX\":100,\"MinY\":0,\"MinZ\":0,\"MaxX\":110,\"MaxY\":10,\"MaxZ\":10,\"State\":2}";
        const string globalBounds = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":200,\"MaxY\":200,\"MaxZ\":20,\"State\":2}";
        var failedPartition = includeFailedPartition
            ? $",{{\"id\":\"partition-x-p000002-y-p000000\",\"status\":1,\"xIndex\":2,\"yIndex\":0,\"cellBounds\":{secondBounds},\"contentBounds\":{secondBounds},\"artifactPath\":null,\"proceduralCount\":0,\"staticAssetCount\":0,\"dynamicAssetCount\":0}}"
            : string.Empty;
        await File.WriteAllTextAsync(Path.Combine(_packageDirectory, "scene-package.json"),
            $"{{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"sceneBounds\":{globalBounds},\"partitions\":[{{\"id\":\"partition-x-p000001-y-p000000\",\"status\":0,\"xIndex\":1,\"yIndex\":0,\"cellBounds\":{secondBounds},\"contentBounds\":{secondBounds},\"artifactPath\":\"partitions/partition-x-p000001-y-p000000.glb\",\"proceduralCount\":1,\"staticAssetCount\":0,\"dynamicAssetCount\":0}},{{\"id\":\"partition-global\",\"status\":0,\"xIndex\":null,\"yIndex\":null,\"cellBounds\":{{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":0,\"MaxY\":0,\"MaxZ\":0,\"State\":0}},\"contentBounds\":{globalBounds},\"artifactPath\":\"partitions/partition-global.glb\",\"proceduralCount\":1,\"staticAssetCount\":0,\"dynamicAssetCount\":0}},{{\"id\":\"partition-x-p000000-y-p000000\",\"status\":0,\"xIndex\":0,\"yIndex\":0,\"cellBounds\":{firstBounds},\"contentBounds\":{firstBounds},\"artifactPath\":\"partitions/partition-x-p000000-y-p000000.glb\",\"proceduralCount\":1,\"staticAssetCount\":0,\"dynamicAssetCount\":0}}{failedPartition}],\"dynamicNodes\":[]}}");
    }

    private async Task CreateLargePackageAsync(int partitionCount)
    {
        var partitionsDirectory = Path.Combine(_packageDirectory, "partitions");
        Directory.CreateDirectory(partitionsDirectory);
        const string bounds = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":1,\"MaxY\":1,\"MaxZ\":1,\"State\":2}";
        var index = new StringBuilder($"{{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"sceneBounds\":{bounds},\"partitions\":[");

        for (var partitionIndex = 0; partitionIndex < partitionCount; partitionIndex++)
        {
            var partitionName = $"partition-x-p{partitionIndex:D6}-y-p000000";
            if (partitionIndex > 0)
            {
                index.Append(',');
            }

            index.Append($"{{\"id\":\"{partitionName}\",\"status\":0,\"xIndex\":{partitionIndex},\"yIndex\":0,\"cellBounds\":{bounds},\"contentBounds\":{bounds},\"artifactPath\":\"partitions/{partitionName}.glb\",\"proceduralCount\":1,\"staticAssetCount\":0,\"dynamicAssetCount\":0}}");
            WriteMinimalGlb(Path.Combine(partitionsDirectory, partitionName + ".glb"));
        }

        index.Append("],\"dynamicNodes\":[]}");
        await File.WriteAllTextAsync(Path.Combine(_packageDirectory, "scene-package.json"), index.ToString());
    }

    private static string ComputeFileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteMinimalGlb(string path)
    {
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
    }
}
