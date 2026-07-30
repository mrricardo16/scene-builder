namespace SceneBuilder.Application;

public enum SceneCapabilityState
{
    Available = 0,
    NotConfigured = 1,
    Unsupported = 2,
    Planned = 3
}

public sealed record SceneCapability
{
    public string Code { get; init; } = string.Empty;

    public SceneCapabilityState State { get; init; }

    public string? DiagnosticCode { get; init; }
}

public interface ISceneCapabilityRegistry
{
    IReadOnlyList<SceneCapability> GetCapabilities();
}

public sealed class SceneCapabilityRegistry : ISceneCapabilityRegistry
{
    private readonly SceneCapability[] _capabilities;

    public SceneCapabilityRegistry(IEnumerable<SceneCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        _capabilities = capabilities.ToArray();
        if (_capabilities.Any(capability => capability is null || !SceneOperationContractValidator.IsUppercaseAsciiCode(capability.Code)))
        {
            throw new ArgumentException("Capability codes must use uppercase ASCII letters, digits, or underscores.", nameof(capabilities));
        }

        if (_capabilities.Select(capability => capability.Code).Distinct(StringComparer.Ordinal).Count() != _capabilities.Length)
        {
            throw new ArgumentException("Capability codes must be unique.", nameof(capabilities));
        }
    }

    public IReadOnlyList<SceneCapability> GetCapabilities() => _capabilities.ToArray();
}
