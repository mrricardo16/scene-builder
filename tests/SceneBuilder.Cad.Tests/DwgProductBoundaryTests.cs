namespace SceneBuilder.Cad.Tests;

public sealed class DwgProductBoundaryTests
{
    [Fact]
    public void CadAssembly_DoesNotPublishInProcessDwgReader()
    {
        var readerType = typeof(UnsupportedDwgProbe).Assembly.GetType(
            "SceneBuilder.Cad.ACadSharpDwgInspector");

        Assert.Null(readerType);
    }
}
