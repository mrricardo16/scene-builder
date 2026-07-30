namespace SceneBuilder.Application.Tests;

public sealed class SceneCapabilityRegistryTests
{
    [Fact]
    public void GetCapabilities_returns_a_stable_defensive_snapshot()
    {
        var registry = new SceneCapabilityRegistry(
        [
            new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Available },
            new SceneCapability { Code = "ANALYZE", State = SceneCapabilityState.Planned }
        ]);

        var first = registry.GetCapabilities();
        var second = registry.GetCapabilities();

        Assert.Equal(["DOCTOR", "ANALYZE"], first.Select(item => item.Code));
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("DOCTOR-CHECK")]
    [InlineData("DOCTOR CHECK")]
    [InlineData("")]
    public void Constructor_rejects_non_uppercase_ascii_capability_codes(string code)
    {
        Assert.Throws<ArgumentException>(() => new SceneCapabilityRegistry(
        [
            new SceneCapability { Code = code, State = SceneCapabilityState.Available }
        ]));
    }

    [Fact]
    public void Constructor_rejects_duplicate_codes()
    {
        Assert.Throws<ArgumentException>(() => new SceneCapabilityRegistry(
        [
            new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Available },
            new SceneCapability { Code = "DOCTOR", State = SceneCapabilityState.Planned }
        ]));
    }
}
