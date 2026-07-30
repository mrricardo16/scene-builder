namespace SceneBuilder.Application.Tests;

public sealed class OutputRootPolicyTests
{
    [Fact]
    public void Validate_requires_an_explicit_absolute_path_without_creating_a_directory()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-output-root", Guid.NewGuid().ToString("N"));
        var policy = new OutputRootPolicy();

        var validation = policy.Validate(outputRoot);

        Assert.True(validation.IsValid);
        Assert.Equal(Path.GetFullPath(outputRoot), validation.NormalizedPath);
        Assert.False(Directory.Exists(outputRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-output")]
    public void Validate_rejects_empty_or_relative_paths(string outputRoot)
    {
        var validation = new OutputRootPolicy().Validate(outputRoot);

        Assert.False(validation.IsValid);
        Assert.Null(validation.NormalizedPath);
    }

    [Fact]
    public void Validate_rejects_paths_under_a_protected_root()
    {
        var protectedRoot = Path.Combine(Path.GetTempPath(), "scene-builder-protected", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(protectedRoot, "src", "job-output");
        var policy = new OutputRootPolicy([protectedRoot]);

        var validation = policy.Validate(outputRoot);

        Assert.False(validation.IsValid);
    }
}
