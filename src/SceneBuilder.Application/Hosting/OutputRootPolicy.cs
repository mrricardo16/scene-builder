namespace SceneBuilder.Application;

public sealed record OutputRootValidationResult
{
    public bool IsValid { get; init; }

    public string? NormalizedPath { get; init; }
}

public interface IOutputRootPolicy
{
    OutputRootValidationResult Validate(string outputRoot);
}

public sealed class OutputRootPolicy : IOutputRootPolicy
{
    private readonly string[] _protectedRoots;

    public OutputRootPolicy(IEnumerable<string>? protectedRoots = null)
    {
        _protectedRoots = (protectedRoots ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public OutputRootValidationResult Validate(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Path.IsPathFullyQualified(outputRoot))
        {
            return new OutputRootValidationResult();
        }

        var normalizedPath = Path.GetFullPath(outputRoot);
        if (_protectedRoots.Any(protectedRoot => IsSameOrUnder(normalizedPath, protectedRoot)))
        {
            return new OutputRootValidationResult();
        }

        return new OutputRootValidationResult { IsValid = true, NormalizedPath = normalizedPath };
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
