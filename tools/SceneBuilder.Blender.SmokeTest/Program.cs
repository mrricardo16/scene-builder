using System.Diagnostics;
using SceneBuilder.Application;
using SceneBuilder.Blender;
using SceneBuilder.Domain;
using SceneBuilder.Pipeline;
using SceneBuilder.Tiles;

var blenderPath = ArgumentValue(args, "--blender") ?? @"D:\tool\Blender\blender.exe";
var mode = ArgumentValue(args, "--mode") ?? "scene";
if (mode is not ("scene" or "package" or "tileset"))
{
    throw new ArgumentException("The --mode value must be scene, package, or tileset.");
}
var temporaryDirectory = Path.Combine(Path.GetTempPath(), "scene-builder-smoke-" + Guid.NewGuid().ToString("N"));
var cleaned = false;

try
{
    if (!File.Exists(blenderPath))
    {
        throw new InvalidOperationException("Blender executable is unavailable.");
    }

    Directory.CreateDirectory(temporaryDirectory);
    var version = await RunBlenderAsync(blenderPath, ["--version"]);
    if (version.ExitCode != 0)
    {
        throw new InvalidOperationException("Blender version check failed.");
    }

    var versionLine = version.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "unknown";
    Console.WriteLine($"Blender: {versionLine}");
    var sourceRoot = Path.Combine(temporaryDirectory, "sources");
    Directory.CreateDirectory(sourceRoot);
    var scriptPath = Path.Combine(temporaryDirectory, "create_smoke_asset.py");
    await File.WriteAllTextAsync(scriptPath, "import bpy\nimport sys\nbpy.ops.mesh.primitive_cube_add(size=1.0)\nbpy.ops.export_scene.gltf(filepath=sys.argv[-1], export_format='GLB')\n");
    var staticSource = Path.Combine(sourceRoot, "static.glb");
    var dynamicSource = Path.Combine(sourceRoot, "dynamic.glb");
    await CreateSourceGlbAsync(blenderPath, scriptPath, staticSource);
    await CreateSourceGlbAsync(blenderPath, scriptPath, dynamicSource);

    var validator = new BinaryGlbValidator();
    if (!validator.Validate(staticSource).IsValid || !validator.Validate(dynamicSource).IsValid)
    {
        throw new InvalidOperationException("Synthetic source GLB validation failed.");
    }

    var staticFacility = Facility("smoke-static", "insert-static", "STATIC_SMOKE", CadSemanticClassification.StaticFacility);
    var dynamicEquipment = Facility(
        "smoke-dynamic",
        "insert-dynamic",
        "DYNAMIC_SMOKE",
        CadSemanticClassification.DynamicEquipment,
        CadBounds.Computed(101, 1, 0, 102, 2, 1),
        new CadPoint3(101, 1, 0));
    var globalFacility = Facility(
        "smoke-global",
        "insert-global",
        "GLOBAL_SMOKE",
        CadSemanticClassification.StaticFacility,
        CadBounds.Computed(0, 0, 0, 500, 500, 1),
        new CadPoint3(0, 0, 0));
    var draft = new SceneDraft
    {
        Id = "smoke-draft",
        SemanticObjects = [staticFacility, dynamicEquipment],
        Nodes =
        [
            Node(staticFacility, SceneNodeContentKind.StaticAssetReference),
            Node(dynamicEquipment, SceneNodeContentKind.DynamicAssetReference)
        ]
    };
    var configuration = new CadAssetConfiguration
    {
        Catalog = new CadAssetCatalog
        {
            ContractVersion = CadAssetConfigurationLoader.ContractVersion,
            Assets =
            [
                new CadAssetDefinition { AssetId = "smoke-static", Kind = CadAssetKind.StaticFacility, RelativeGlbPath = "static.glb" },
                new CadAssetDefinition { AssetId = "smoke-dynamic", Kind = CadAssetKind.DynamicEquipment, RelativeGlbPath = "dynamic.glb" }
            ]
        },
        Bindings = new CadAssetBindingSet
        {
            ContractVersion = CadAssetConfigurationLoader.ContractVersion,
            Bindings =
            [
                new CadAssetBinding { Id = "static", Enabled = true, Priority = 0, Kind = CadAssetKind.StaticFacility, Selector = new CadAssetBindingSelector { SemanticObjectId = staticFacility.Id }, AssetId = "smoke-static" },
                new CadAssetBinding { Id = "dynamic", Enabled = true, Priority = 0, Kind = CadAssetKind.DynamicEquipment, Selector = new CadAssetBindingSelector { SemanticObjectId = dynamicEquipment.Id }, AssetId = "smoke-dynamic" },
                new CadAssetBinding { Id = "global", Enabled = true, Priority = 0, Kind = CadAssetKind.StaticFacility, Selector = new CadAssetBindingSelector { SemanticObjectId = globalFacility.Id }, AssetId = "smoke-static" }
            ]
        }
    };
    var toolOptions = new BlenderToolOptions { ExecutablePath = blenderPath, Timeout = TimeSpan.FromMinutes(10), MaximumProcessOutputCharacters = 16_384 };
    var assetGeneration = new BlenderAssetGenerationContext { AssetRootDirectory = sourceRoot, Configuration = configuration };
    if (mode is "package" or "tileset")
    {
        var packageDraft = new SceneDraft
        {
            Id = "smoke-package-draft",
            SemanticObjects = [staticFacility, dynamicEquipment, globalFacility],
            Nodes = [Node(staticFacility, SceneNodeContentKind.StaticAssetReference), Node(dynamicEquipment, SceneNodeContentKind.DynamicAssetReference), Node(globalFacility, SceneNodeContentKind.StaticAssetReference)]
        };
        var packageResult = await new ScenePackageGenerator(new BlenderSceneGenerator()).GenerateAsync(new ScenePackageGenerationRequest
        {
            Draft = packageDraft,
            PartitionPolicy = new ScenePartitionPolicy { MaximumIntersectedCellsPerObject = 2 },
            OutputRootDirectory = Path.Combine(temporaryDirectory, "packages"),
            PackageName = "smoke-package",
            BlenderTool = toolOptions,
            AssetGeneration = assetGeneration
        }, CancellationToken.None);
        if (packageResult.Status is not ScenePackageGenerationStatus.Succeeded || packageResult.PackagePath is null || packageResult.Index is null || packageResult.Index.Partitions.Count != 3 || packageResult.Index.Partitions.Any(partition => partition.ArtifactPath is null))
        {
            throw new InvalidOperationException($"Scene package generation or validation failed: {packageResult.Status}; {string.Join(',', packageResult.Diagnostics.Select(diagnostic => diagnostic.Code))}");
        }

        Console.WriteLine("Scene package generation: passed (2 regular + 1 global)");
        Console.WriteLine("Package GLB validation: passed (3)");
        if (mode is "package")
        {
            return;
        }

        var tilesetResult = await new TilesetGenerator().GenerateAsync(new TilesetGenerationRequest
        {
            ScenePackageDirectory = packageResult.PackagePath,
            Policy = new TilesetGenerationPolicy { RootGeometricErrorMeters = 100d, MinimumBoundingHalfExtentMeters = 0.001d }
        }, CancellationToken.None);
        if (tilesetResult.Status is not TilesetGenerationStatus.Succeeded || tilesetResult.TilesetPath is null || tilesetResult.IncludedPartitionCount != 3 || !(await new TilesetValidator().ValidateAsync(packageResult.PackagePath, tilesetResult.TilesetPath, CancellationToken.None)).IsValid)
        {
            throw new InvalidOperationException($"Tileset generation or validation failed: {tilesetResult.Status}; {string.Join(',', tilesetResult.Diagnostics.Select(diagnostic => diagnostic.Code))}");
        }

        Console.WriteLine("SCENEBUILDER_TILESET_STATUS:SUCCEEDED");
        Console.WriteLine("SCENEBUILDER_TILESET_VERSION:1.1");
        Console.WriteLine("SCENEBUILDER_TILESET_LEAVES:3");
        Console.WriteLine("SCENEBUILDER_TILESET_GLB_VALID:3");
        Console.WriteLine("SCENEBUILDER_TILESET_VALID:true");
        return;
    }

    var result = await new BlenderSceneGenerator().GenerateAsync(new BlenderGenerationRequest
    {
        Draft = draft,
        OutputDirectory = Path.Combine(temporaryDirectory, "output"),
        OutputFileName = "scene.glb",
        AllowOverwrite = false,
        Tool = toolOptions,
        AssetGeneration = assetGeneration
    }, CancellationToken.None);
    if (result.Status is not BlenderGenerationStatus.Succeeded || result.ArtifactPath is null || !validator.Validate(result.ArtifactPath).IsValid)
    {
        throw new InvalidOperationException($"Final GLB generation or validation failed: {result.Status}; {string.Join(',', result.Diagnostics.Select(diagnostic => diagnostic.Code))}");
    }

    Console.WriteLine("StaticFacility: imported");
    Console.WriteLine("DynamicEquipment: imported");
    Console.WriteLine("Source GLB validation: passed (2)");
    Console.WriteLine("Final GLB validation: passed");
}
finally
{
    if (Directory.Exists(temporaryDirectory))
    {
        Directory.Delete(temporaryDirectory, recursive: true);
        cleaned = true;
    }

    Console.WriteLine($"Temporary cleanup: {(cleaned ? "passed" : "not-needed")}");
}

static CadSemanticObject Facility(string id, string insertId, string block, CadSemanticClassification classification, CadBounds? bounds = null, CadPoint3? position = null) => classification switch
{
    CadSemanticClassification.StaticFacility => new CadStaticFacilityObject(id, insertId, bounds ?? CadBounds.Computed(0, 0, 0, 1, 1, 1), null, block, position ?? new CadPoint3(1, 2, 3), 15, CadScale3.Identity),
    CadSemanticClassification.DynamicEquipment => new CadDynamicEquipmentObject(id, insertId, bounds ?? CadBounds.Computed(0, 0, 0, 1, 1, 1), null, block, position ?? new CadPoint3(4, 5, 6), 30, CadScale3.Identity),
    _ => throw new ArgumentOutOfRangeException(nameof(classification))
};

static SceneNode Node(CadSemanticObject semanticObject, SceneNodeContentKind contentKind) => new()
{
    Id = "node-" + semanticObject.Id,
    SemanticObjectId = semanticObject.Id,
    Classification = semanticObject.Classification,
    ContentKind = contentKind,
    Bounds = semanticObject.Bounds,
    Transform = semanticObject switch
    {
        CadStaticFacilityObject facility => new SceneNodeTransform(facility.Position, facility.RotationDegrees, facility.Scale),
        CadDynamicEquipmentObject equipment => new SceneNodeTransform(equipment.Position, equipment.RotationDegrees, equipment.Scale),
        _ => throw new ArgumentOutOfRangeException(nameof(semanticObject))
    }
};

static async Task CreateSourceGlbAsync(string blenderPath, string scriptPath, string outputPath)
{
    var result = await RunBlenderAsync(blenderPath, ["--background", "--factory-startup", "--python", scriptPath, "--", outputPath]);
    if (result.ExitCode != 0 || !File.Exists(outputPath))
    {
        throw new InvalidOperationException("Synthetic GLB creation failed.");
    }
}

static async Task<ProcessResult> RunBlenderAsync(string blenderPath, IReadOnlyList<string> arguments)
{
    using var process = new Process { StartInfo = new ProcessStartInfo { FileName = blenderPath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
    foreach (var argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    process.Start();
    var standardOutput = await process.StandardOutput.ReadToEndAsync();
    var standardError = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, standardOutput, standardError);
}

static string? ArgumentValue(IReadOnlyList<string> arguments, string name)
{
    var index = Array.FindIndex(arguments.ToArray(), argument => string.Equals(argument, name, StringComparison.Ordinal));
    return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
