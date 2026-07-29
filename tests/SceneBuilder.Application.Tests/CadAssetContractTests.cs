using Xunit;

namespace SceneBuilder.Application.Tests;

public sealed class CadAssetContractTests
{
    [Fact]
    public void Versioned_asset_catalog_contract_is_available()
    {
        var contract = Type.GetType("SceneBuilder.Application.CadAssetCatalog, SceneBuilder.Application");

        Assert.NotNull(contract);
    }
}
