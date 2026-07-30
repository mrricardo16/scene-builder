namespace SceneBuilder.Blender;

internal interface IReparsePointInspector
{
    bool IsReparsePoint(string path);
}

internal sealed class FileSystemReparsePointInspector : IReparsePointInspector
{
    public bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

internal sealed record AssetPathResolution(string? FullPath, string? DiagnosticCode)
{
    public bool IsSuccess => FullPath is not null && DiagnosticCode is null;

    public static AssetPathResolution Succeeded(string fullPath) => new(fullPath, null);

    public static AssetPathResolution Failed(string code) => new(null, code);
}

internal sealed class SafeAssetPathResolver
{
    private readonly IReparsePointInspector _reparsePointInspector;

    public SafeAssetPathResolver(IReparsePointInspector? reparsePointInspector = null)
    {
        _reparsePointInspector = reparsePointInspector ?? new FileSystemReparsePointInspector();
    }

    public AssetPathResolution Resolve(string assetRootDirectory, string relativeGlbPath)
    {
        if (string.IsNullOrWhiteSpace(assetRootDirectory) || !IsSafeRelativeGlbPath(relativeGlbPath))
        {
            return AssetPathResolution.Failed("ASSET_PATH_INVALID");
        }

        try
        {
            var root = Path.GetFullPath(assetRootDirectory);
            if (!Directory.Exists(root))
            {
                return AssetPathResolution.Failed("ASSET_PATH_INVALID");
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, relativeGlbPath));
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return AssetPathResolution.Failed("ASSET_PATH_INVALID");
            }

            return ContainsReparsePoint(root, relativeGlbPath)
                ? AssetPathResolution.Failed("ASSET_PATH_REPARSE_POINT")
                : AssetPathResolution.Succeeded(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return AssetPathResolution.Failed("ASSET_PATH_INVALID");
        }
    }

    private static bool IsSafeRelativeGlbPath(string value) =>
        !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && string.Equals(Path.GetExtension(value), ".glb", StringComparison.OrdinalIgnoreCase) && !Uri.TryCreate(value, UriKind.Absolute, out _) &&
        value.Split(['\\', '/'], StringSplitOptions.None).All(segment => !string.IsNullOrWhiteSpace(segment) && segment is not "." and not "..");

    private bool ContainsReparsePoint(string root, string relativeGlbPath)
    {
        if (_reparsePointInspector.IsReparsePoint(root)) return true;
        var current = root;
        foreach (var segment in relativeGlbPath.Split(['\\', '/']))
        {
            current = Path.Combine(current, segment);
            if (_reparsePointInspector.IsReparsePoint(current)) return true;
        }
        return false;
    }
}
