using System.Collections;
using System.Reflection;

namespace SceneBuilder.Domain.Tests;

public sealed class ModelDefaultsContractTests
{
    [Theory]
    [InlineData("CadDocumentModel", "Layers")]
    [InlineData("CadDocumentModel", "Blocks")]
    [InlineData("CadDocumentModel", "EntityTypes")]
    [InlineData("CadDocumentModel", "Diagnostics")]
    [InlineData("SceneDraft", "Nodes")]
    [InlineData("SceneDraft", "SemanticObjects")]
    [InlineData("SceneDraft", "Diagnostics")]
    [InlineData("SceneNode", "SourceLayers")]
    [InlineData("SceneDraftBuildResult", "SkippedSubjectIds")]
    [InlineData("SceneDraftBuildResult", "Diagnostics")]
    [InlineData("JobReport", "Diagnostics")]
    [InlineData("JobReport", "Artifacts")]
    [InlineData("TilesConversionResult", "Diagnostics")]
    public void Default_collection_properties_are_empty_and_never_null(
        string typeName,
        string propertyName)
    {
        var contractType = GetDomainType(typeName);
        var instance = Activator.CreateInstance(contractType);

        Assert.NotNull(instance);
        var collection = contractType.GetProperty(propertyName)?.GetValue(instance);

        Assert.NotNull(collection);
        Assert.IsAssignableFrom<IEnumerable>(collection);
        Assert.Empty((IEnumerable)collection);
    }

    private static Type GetDomainType(string typeName) =>
        Assembly.Load("SceneBuilder.Domain")
            .GetType($"SceneBuilder.Domain.{typeName}")
        ?? throw new Xunit.Sdk.XunitException($"Expected domain contract '{typeName}' was not found.");
}
