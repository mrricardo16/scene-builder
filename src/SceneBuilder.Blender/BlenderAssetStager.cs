using SceneBuilder.Application;

namespace SceneBuilder.Blender;

internal sealed record StagedBlenderAsset(string AssetId, CadAssetKind Kind, string ManifestRelativePath);

internal sealed record BlenderAssetStagingResult(IReadOnlyList<StagedBlenderAsset> Assets, string? DiagnosticCode)
{
    public bool IsSuccess => DiagnosticCode is null;

    public static BlenderAssetStagingResult Succeeded(IReadOnlyList<StagedBlenderAsset> assets) => new(assets, null);

    public static BlenderAssetStagingResult Failed(string code) => new(Array.Empty<StagedBlenderAsset>(), code);
}

internal sealed class BlenderAssetStager
{
    private readonly ISecureAssetFileOpener _fileOpener;
    private readonly BinaryGlbValidator _validator;

    public BlenderAssetStager(ISecureAssetFileOpener? fileOpener = null, BinaryGlbValidator? validator = null)
    {
        _fileOpener = fileOpener ?? new WindowsSecureAssetFileOpener();
        _validator = validator ?? new BinaryGlbValidator();
    }

    public BlenderAssetStagingResult Stage(
        IEnumerable<CadAssetResolution> resolutions,
        string assetRootDirectory,
        string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            return BlenderAssetStagingResult.Failed("ASSET_STAGING_FAILED");
        }

        var selectedAssets = resolutions
            .Where(resolution => resolution.Status is CadAssetResolutionStatus.Resolved && resolution.Asset is not null)
            .Select(resolution => resolution.Asset!)
            .GroupBy(asset => asset.AssetId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.ToArray())
            .ToArray();

        var assetDirectory = Path.Combine(workDirectory, "assets");
        try
        {
            Directory.CreateDirectory(assetDirectory);
            var stagedAssets = new List<StagedBlenderAsset>(selectedAssets.Length);
            for (var index = 0; index < selectedAssets.Length; index++)
            {
                var candidates = selectedAssets[index];
                var asset = candidates[0];
                if (candidates.Any(candidate => candidate.Kind != asset.Kind || !string.Equals(candidate.RelativeGlbPath, asset.RelativeGlbPath, StringComparison.Ordinal)))
                {
                    DeletePartiallyStagedAssets(assetDirectory);
                    return BlenderAssetStagingResult.Failed("ASSET_STAGING_FAILED");
                }

                var opened = _fileOpener.OpenRead(assetRootDirectory, asset.RelativeGlbPath);
                if (opened.File is null)
                {
                    DeletePartiallyStagedAssets(assetDirectory);
                    return BlenderAssetStagingResult.Failed(opened.DiagnosticCode!);
                }

                var manifestRelativePath = $"assets/asset-{index + 1:D6}.glb";
                var stagedPath = Path.Combine(workDirectory, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var temporaryPath = stagedPath + ".tmp";
                using (opened.File)
                using (var sourceStream = opened.File.OpenReadStream())
                {
                    if (!_validator.Validate(sourceStream, leaveOpen: true).IsValid)
                    {
                        DeleteTemporaryFile(temporaryPath);
                        DeletePartiallyStagedAssets(assetDirectory);
                        return BlenderAssetStagingResult.Failed("ASSET_SOURCE_GLB_INVALID");
                    }

                    sourceStream.Position = 0;
                    using var stagedStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
                    sourceStream.CopyTo(stagedStream);
                    stagedStream.Flush(flushToDisk: true);
                }

                if (!_validator.Validate(temporaryPath).IsValid)
                {
                    DeleteTemporaryFile(temporaryPath);
                    DeletePartiallyStagedAssets(assetDirectory);
                    return BlenderAssetStagingResult.Failed("ASSET_SOURCE_GLB_INVALID");
                }

                File.Move(temporaryPath, stagedPath, overwrite: false);
                stagedAssets.Add(new StagedBlenderAsset(asset.AssetId, asset.Kind, manifestRelativePath));
            }

            return BlenderAssetStagingResult.Succeeded(stagedAssets);
        }
        catch (IOException)
        {
            DeletePartiallyStagedAssets(assetDirectory);
            return BlenderAssetStagingResult.Failed("ASSET_STAGING_FAILED");
        }
        catch (UnauthorizedAccessException)
        {
            DeletePartiallyStagedAssets(assetDirectory);
            return BlenderAssetStagingResult.Failed("ASSET_STAGING_FAILED");
        }
    }

    private static void DeletePartiallyStagedAssets(string assetDirectory)
    {
        try
        {
            if (Directory.Exists(assetDirectory))
            {
                Directory.Delete(assetDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
