using System.Buffers.Binary;
using System.Text;
using SceneBuilder.Application;
using SceneBuilder.Domain;
using SceneBuilder.Pipeline;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class ScenePackageGeneratorTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-package-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_publishes_verified_regular_and_global_partition_glbs_with_a_relative_index()
    {
        var generator = new ScenePackageGenerator(new ValidGlbGenerator());
        var result = await generator.GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = Draft(),
            PartitionPolicy = new ScenePartitionPolicy { MaximumIntersectedCellsPerObject = 2 },
            OutputRootDirectory = _outputRoot,
            PackageName = "synthetic-package",
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.Succeeded, result.Status);
        var packagePath = Assert.IsType<string>(result.PackagePath);
        Assert.True(Directory.Exists(packagePath));
        var index = Assert.IsType<ScenePackageIndex>(result.Index);
        Assert.Equal("1.0", index.ContractVersion);
        Assert.Equal(["partition-x-p000000-y-p000000", "partition-x-p000001-y-p000000", "partition-global"], index.Partitions.Select(partition => partition.Id));
        Assert.All(index.Partitions, partition => Assert.False(Path.IsPathRooted(Assert.IsType<string>(partition.ArtifactPath))));
        Assert.Equal("dynamic-1", Assert.Single(index.DynamicNodes).SemanticObjectId);
        Assert.All(index.Partitions, partition => Assert.True(new BinaryGlbValidator().Validate(Path.Combine(packagePath, partition.ArtifactPath!)).IsValid));
        Assert.True(File.Exists(Path.Combine(packagePath, "scene-package.json")));
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_index_that_references_an_invalid_partition_glb()
    {
        var packagePath = Path.Combine(_outputRoot, "invalid-package");
        Directory.CreateDirectory(Path.Combine(packagePath, "partitions"));
        const string bounds = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":0,\"MaxY\":0,\"MaxZ\":0,\"State\":0}";
        await File.WriteAllTextAsync(
            Path.Combine(packagePath, "scene-package.json"),
            $"{{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"sceneBounds\":{bounds},\"partitions\":[{{\"id\":\"partition-x-p000000-y-p000000\",\"status\":0,\"xIndex\":0,\"yIndex\":0,\"cellBounds\":{bounds},\"contentBounds\":{bounds},\"artifactPath\":\"partitions/bad.glb\",\"proceduralCount\":0,\"staticAssetCount\":0,\"dynamicAssetCount\":0}}],\"dynamicNodes\":[]}}");
        await File.WriteAllTextAsync(Path.Combine(packagePath, "partitions", "bad.glb"), "not a glb");

        var result = await new ScenePackageValidator().ValidateAsync(packagePath, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SCENE_PACKAGE_ARTIFACT_INVALID");
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_index_with_an_undefined_partition_status_or_blank_id()
    {
        var packagePath = Path.Combine(_outputRoot, "malformed-package");
        Directory.CreateDirectory(packagePath);
        await File.WriteAllTextAsync(
            Path.Combine(packagePath, "scene-package.json"),
            "{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"sceneBounds\":{\"State\":0},\"partitions\":[{\"id\":\"\",\"status\":999,\"cellBounds\":{\"State\":0},\"contentBounds\":{\"State\":0}}],\"dynamicNodes\":[]}");

        var result = await new ScenePackageValidator().ValidateAsync(packagePath, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SCENE_PACKAGE_INDEX_INVALID");
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_index_missing_required_partition_metadata_even_when_its_glb_is_valid()
    {
        var packagePath = Path.Combine(_outputRoot, "missing-metadata-package");
        var partitionPath = Path.Combine(packagePath, "partitions");
        Directory.CreateDirectory(partitionPath);
        ValidGlbGenerator.WriteMinimalGlb(Path.Combine(partitionPath, "valid.glb"));
        await File.WriteAllTextAsync(
            Path.Combine(packagePath, "scene-package.json"),
            "{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"partitions\":[{\"id\":\"partition-x-p000000-y-p000000\",\"status\":0,\"xIndex\":0,\"yIndex\":0,\"artifactPath\":\"partitions/valid.glb\"}],\"dynamicNodes\":[]}");

        var result = await new ScenePackageValidator().ValidateAsync(packagePath, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SCENE_PACKAGE_INDEX_INVALID");
    }

    [Fact]
    public async Task ValidateAsync_rejects_null_required_bounds_even_when_all_property_names_are_present()
    {
        var packagePath = Path.Combine(_outputRoot, "null-bounds-package");
        var partitionPath = Path.Combine(packagePath, "partitions");
        Directory.CreateDirectory(partitionPath);
        ValidGlbGenerator.WriteMinimalGlb(Path.Combine(partitionPath, "valid.glb"));
        await File.WriteAllTextAsync(
            Path.Combine(packagePath, "scene-package.json"),
            "{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"sceneBounds\":null,\"partitions\":[{\"id\":\"partition-x-p000000-y-p000000\",\"status\":0,\"xIndex\":0,\"yIndex\":0,\"cellBounds\":null,\"contentBounds\":null,\"artifactPath\":\"partitions/valid.glb\",\"proceduralCount\":0,\"staticAssetCount\":0,\"dynamicAssetCount\":0}],\"dynamicNodes\":[]}");

        var result = await new ScenePackageValidator().ValidateAsync(packagePath, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SCENE_PACKAGE_INDEX_INVALID");
    }

    [Fact]
    public async Task ValidateAsync_preserves_not_evaluated_global_cell_bounds_from_the_index_contract()
    {
        var packagePath = Path.Combine(_outputRoot, "global-bounds-package");
        var partitionPath = Path.Combine(packagePath, "partitions");
        Directory.CreateDirectory(partitionPath);
        ValidGlbGenerator.WriteMinimalGlb(Path.Combine(partitionPath, "global.glb"));
        const string bounds = "{\"MinX\":0,\"MinY\":0,\"MinZ\":0,\"MaxX\":0,\"MaxY\":0,\"MaxZ\":0,\"State\":0}";
        await File.WriteAllTextAsync(
            Path.Combine(packagePath, "scene-package.json"),
            $"{{\"contractVersion\":\"1.0\",\"unit\":\"meters\",\"sceneBounds\":{bounds},\"partitions\":[{{\"id\":\"partition-global\",\"status\":0,\"xIndex\":null,\"yIndex\":null,\"cellBounds\":{bounds},\"contentBounds\":{bounds},\"artifactPath\":\"partitions/global.glb\",\"proceduralCount\":0,\"staticAssetCount\":0,\"dynamicAssetCount\":0}}],\"dynamicNodes\":[]}}");

        var result = await new ScenePackageValidator().ValidateAsync(packagePath, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(CadBoundsState.NotEvaluated, Assert.Single(result.Index!.Partitions).CellBounds.State);
    }

    [Fact]
    public async Task GenerateAsync_does_not_publish_or_leave_staging_when_a_partition_fails_and_partial_publish_is_disabled()
    {
        var generator = new ScenePackageGenerator(new FailingGlbGenerator());
        var result = await generator.GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = Draft(),
            OutputRootDirectory = _outputRoot,
            PackageName = "failed-package",
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.Failed, result.Status);
        Assert.False(Directory.Exists(Path.Combine(_outputRoot, "failed-package")));
        Assert.Empty(Directory.EnumerateDirectories(_outputRoot, ".failed-package.staging-*"));
    }

    [Fact]
    public async Task GenerateAsync_publishes_only_successful_partition_artifacts_when_partial_publish_is_enabled()
    {
        var generator = new ScenePackageGenerator(new FailFirstPartitionGlbGenerator());
        var result = await generator.GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = Draft(),
            PartitionPolicy = new ScenePartitionPolicy { MaximumIntersectedCellsPerObject = 2 },
            OutputRootDirectory = _outputRoot,
            PackageName = "partial-package",
            PublicationPolicy = new ScenePackagePublicationPolicy { PublishPartialPackage = true },
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.PartiallySucceeded, result.Status);
        var index = Assert.IsType<ScenePackageIndex>(result.Index);
        Assert.Equal(["partition-x-p000001-y-p000000", "partition-global"], index.Partitions.Select(partition => partition.Id));
        Assert.All(index.Partitions, partition => Assert.Equal(ScenePackagePartitionStatus.Succeeded, partition.Status));
        Assert.DoesNotContain(index.Partitions, partition => partition.Id == "partition-x-p000000-y-p000000");
    }

    [Fact]
    public async Task GenerateAsync_reports_partial_success_when_policy_skips_an_invalid_object()
    {
        var valid = (CadStaticFacilityObject)Asset("valid-static", 1, 1, 1, 1, false);
        var invalid = new CadStaticFacilityObject("invalid-static", "insert-invalid", CadBounds.NotEvaluated, null, "synthetic", new CadPoint3(1, 1, 0), 0, CadScale3.Identity);
        var draft = new SceneDraft
        {
            Id = "draft-skipped",
            SemanticObjects = [valid, invalid],
            Nodes =
            [
                new SceneNode { Id = "node-valid", SemanticObjectId = valid.Id, Classification = valid.Classification, ContentKind = SceneNodeContentKind.StaticAssetReference, Bounds = valid.Bounds, Transform = new SceneNodeTransform(valid.Position, valid.RotationDegrees, valid.Scale) },
                new SceneNode { Id = "node-invalid", SemanticObjectId = invalid.Id, Classification = invalid.Classification, ContentKind = SceneNodeContentKind.StaticAssetReference, Bounds = invalid.Bounds, Transform = new SceneNodeTransform(invalid.Position, invalid.RotationDegrees, invalid.Scale) }
            ]
        };
        var result = await new ScenePackageGenerator(new ValidGlbGenerator()).GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = draft,
            PartitionPolicy = new ScenePartitionPolicy { InvalidBoundsBehavior = InvalidBoundsBehavior.Skip },
            OutputRootDirectory = _outputRoot,
            PackageName = "skipped-package",
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.PartiallySucceeded, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PARTITION_OBJECT_SKIPPED");
    }

    [Fact]
    public async Task GenerateAsync_does_not_publish_a_valid_glb_when_blender_reports_that_its_partition_was_partial()
    {
        var result = await new ScenePackageGenerator(new PartialGlbGenerator()).GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = Draft(),
            OutputRootDirectory = _outputRoot,
            PackageName = "blender-partial-package",
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.Failed, result.Status);
        Assert.False(Directory.Exists(Path.Combine(_outputRoot, "blender-partial-package")));
    }

    [Fact]
    public async Task GenerateAsync_publishes_completed_partitions_when_it_stops_after_a_failure_and_partial_publish_is_enabled()
    {
        var result = await new ScenePackageGenerator(new FailSecondPartitionGlbGenerator()).GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = Draft(),
            PartitionPolicy = new ScenePartitionPolicy { MaximumIntersectedCellsPerObject = 2 },
            OutputRootDirectory = _outputRoot,
            PackageName = "stopped-partial-package",
            PublicationPolicy = new ScenePackagePublicationPolicy { ContinueAfterPartitionFailure = false, PublishPartialPackage = true },
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.PartiallySucceeded, result.Status);
        Assert.Equal(["partition-x-p000000-y-p000000"], Assert.IsType<ScenePackageIndex>(result.Index).Partitions.Select(partition => partition.Id));
    }

    [Fact]
    public async Task GenerateAsync_returns_a_publication_failure_when_a_protected_write_throws_unauthorized_access()
    {
        var result = await new ScenePackageGenerator(new UnauthorizedGlbGenerator()).GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = Draft(),
            OutputRootDirectory = _outputRoot,
            PackageName = "unauthorized-package",
            BlenderTool = new BlenderToolOptions { ExecutablePath = "fake", Timeout = TimeSpan.FromSeconds(5), MaximumProcessOutputCharacters = 128 }
        }, CancellationToken.None);

        Assert.Equal(ScenePackageGenerationStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SCENE_PACKAGE_PUBLICATION_FAILED");
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot))
        {
            Directory.Delete(_outputRoot, recursive: true);
        }
    }

    private static SceneDraft Draft()
    {
        var first = Asset("static-1", 1, 1, 1, 1, false);
        var dynamic = Asset("dynamic-1", 101, 1, 101, 1, true);
        var large = Asset("large-1", 0, 0, 500, 500, false);
        var objects = new[] { first, dynamic, large };
        return new SceneDraft
        {
            Id = "draft-package",
            SemanticObjects = objects,
            Nodes = objects.Select(item => new SceneNode
            {
                Id = "node-" + item.Id,
                SemanticObjectId = item.Id,
                Classification = item.Classification,
                ContentKind = item is CadDynamicEquipmentObject ? SceneNodeContentKind.DynamicAssetReference : SceneNodeContentKind.StaticAssetReference,
                Bounds = item.Bounds,
                Transform = item switch
                {
                    CadStaticFacilityObject facility => new SceneNodeTransform(facility.Position, facility.RotationDegrees, facility.Scale),
                    CadDynamicEquipmentObject equipment => new SceneNodeTransform(equipment.Position, equipment.RotationDegrees, equipment.Scale),
                    _ => throw new InvalidOperationException()
                }
            }).ToArray()
        };
    }

    private static CadSemanticObject Asset(string id, double minX, double minY, double maxX, double maxY, bool dynamic) => dynamic
        ? new CadDynamicEquipmentObject(id, "insert-" + id, CadBounds.Computed(minX, minY, 0, maxX, maxY, 1), null, "synthetic", new CadPoint3(minX, minY, 0), 0, CadScale3.Identity)
        : new CadStaticFacilityObject(id, "insert-" + id, CadBounds.Computed(minX, minY, 0, maxX, maxY, 1), null, "synthetic", new CadPoint3(minX, minY, 0), 0, CadScale3.Identity);

    private sealed class ValidGlbGenerator : IBlenderSceneGenerator
    {
        public Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(request.OutputDirectory);
            var path = Path.Combine(request.OutputDirectory, request.OutputFileName);
            WriteMinimalGlb(path);
            return Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.Succeeded, ArtifactPath = path, GeneratedObjectCount = request.Draft.Nodes.Count });
        }

        internal static void WriteMinimalGlb(string path)
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

    private sealed class FailingGlbGenerator : IBlenderSceneGenerator
    {
        public Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.Failed });
    }

    private sealed class FailFirstPartitionGlbGenerator : IBlenderSceneGenerator
    {
        public Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken)
        {
            if (request.OutputFileName.Contains("partition-x-p000000-y-p000000", StringComparison.Ordinal))
            {
                return Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.Failed });
            }

            Directory.CreateDirectory(request.OutputDirectory);
            var path = Path.Combine(request.OutputDirectory, request.OutputFileName);
            ValidGlbGenerator.WriteMinimalGlb(path);
            return Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.Succeeded, ArtifactPath = path, GeneratedObjectCount = request.Draft.Nodes.Count });
        }
    }

    private sealed class PartialGlbGenerator : IBlenderSceneGenerator
    {
        public Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(request.OutputDirectory);
            var path = Path.Combine(request.OutputDirectory, request.OutputFileName);
            ValidGlbGenerator.WriteMinimalGlb(path);
            return Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.PartiallySucceeded, ArtifactPath = path, SkippedObjectCount = 1, SkippedSemanticObjectIds = ["synthetic-skip"] });
        }
    }

    private sealed class FailSecondPartitionGlbGenerator : IBlenderSceneGenerator
    {
        public Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken)
        {
            if (request.OutputFileName.Contains("partition-x-p000001-y-p000000", StringComparison.Ordinal))
            {
                return Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.Failed });
            }

            Directory.CreateDirectory(request.OutputDirectory);
            var path = Path.Combine(request.OutputDirectory, request.OutputFileName);
            ValidGlbGenerator.WriteMinimalGlb(path);
            return Task.FromResult(new BlenderGenerationResult { Status = BlenderGenerationStatus.Succeeded, ArtifactPath = path });
        }
    }

    private sealed class UnauthorizedGlbGenerator : IBlenderSceneGenerator
    {
        public Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("Synthetic protected output.");
    }
}
