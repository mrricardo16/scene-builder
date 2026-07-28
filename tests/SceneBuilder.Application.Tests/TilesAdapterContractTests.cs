using SceneBuilder.Domain;
using SceneBuilder.Tiles;

namespace SceneBuilder.Application.Tests;

public sealed class TilesAdapterContractTests
{
    [Fact]
    public async Task ConvertAsync_returns_not_configured_without_an_output_artifact()
    {
        ITilesConverter converter = new NotConfiguredTilesConverter();

        var result = await converter.ConvertAsync(
            new TilesConversionRequest
            {
                SceneDraft = new SceneDraft { Id = "draft-001" },
                OutputDirectory = @"C:\jobs\job-001\output"
            },
            CancellationToken.None);

        Assert.Equal(TilesConversionStatus.NotConfigured, result.Status);
        Assert.Null(result.OutputPath);
        Assert.NotEqual(TilesConversionStatus.Succeeded, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("TILES_NOT_CONFIGURED", diagnostic.Code);
    }
}
