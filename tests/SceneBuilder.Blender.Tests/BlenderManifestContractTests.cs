using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class BlenderManifestContractTests
{
    [Fact]
    public void Internal_manifest_mapper_contract_is_available_for_scene_draft_generation()
    {
        var mapperType = Type.GetType("SceneBuilder.Blender.BlenderManifestMapper, SceneBuilder.Blender");

        Assert.NotNull(mapperType);
    }

    [Fact]
    public void Trusted_script_is_copied_and_declares_a_contract_checked_entry_point()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "generate_scene.py");

        var script = File.ReadAllText(scriptPath);

        Assert.Contains("contractVersion", script, StringComparison.Ordinal);
        Assert.Contains("def main()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("exec(", script, StringComparison.Ordinal);
    }
}
