using System.Reflection;
using System.Text.Json;

namespace SceneBuilder.Domain.Tests;

public sealed class JobReportSerializationContractTests
{
    [Fact]
    public void Serialize_uses_the_contract_camel_case_field_names()
    {
        var reportType = GetDomainType("JobReport");
        var report = Activator.CreateInstance(reportType);

        Assert.NotNull(report);

        var json = JsonSerializer.Serialize(report, reportType);

        Assert.Contains("\"jobId\"", json);
        Assert.Contains("\"createdAt\"", json);
        Assert.Contains("\"diagnostics\"", json);
        Assert.Contains("\"artifacts\"", json);
        Assert.DoesNotContain("\"JobId\"", json);
        Assert.DoesNotContain("\"CreatedAt\"", json);
    }

    [Fact]
    public void Serialize_camel_cases_nested_diagnostic_fields()
    {
        var reportType = GetDomainType("JobReport");
        var diagnosticType = GetDomainType("SceneDiagnostic");
        var report = Activator.CreateInstance(reportType);
        var diagnostic = Activator.CreateInstance(diagnosticType);

        Assert.NotNull(report);
        Assert.NotNull(diagnostic);

        var severityType = diagnosticType.GetProperty("Severity")!.PropertyType;
        diagnosticType.GetProperty("Severity")!.SetValue(diagnostic, Enum.ToObject(severityType, 2));
        diagnosticType.GetProperty("Code")!.SetValue(diagnostic, "DXF001");
        diagnosticType.GetProperty("Message")!.SetValue(diagnostic, "Example diagnostic");
        diagnosticType.GetProperty("SourcePath")!.SetValue(diagnostic, "input/model.dxf");

        var diagnostics = Array.CreateInstance(diagnosticType, 1);
        diagnostics.SetValue(diagnostic, 0);
        reportType.GetProperty("Diagnostics")!.SetValue(report, diagnostics);

        var json = JsonSerializer.Serialize(report, reportType);

        Assert.Contains("\"severity\":2", json);
        Assert.Contains("\"code\":\"DXF001\"", json);
        Assert.Contains("\"message\":\"Example diagnostic\"", json);
        Assert.Contains("\"sourcePath\":\"input/model.dxf\"", json);
        Assert.DoesNotContain("\"Severity\"", json);
        Assert.DoesNotContain("\"Code\"", json);
        Assert.DoesNotContain("\"Message\"", json);
        Assert.DoesNotContain("\"SourcePath\"", json);
    }

    [Fact]
    public void Serialize_camel_cases_nested_artifact_fields()
    {
        var reportType = GetDomainType("JobReport");
        var artifactType = GetDomainType("JobArtifact");
        var report = Activator.CreateInstance(reportType);
        var artifact = Activator.CreateInstance(artifactType);

        Assert.NotNull(report);
        Assert.NotNull(artifact);

        artifactType.GetProperty("Name")!.SetValue(artifact, "scene.glb");
        artifactType.GetProperty("Path")!.SetValue(artifact, "output/scene.glb");

        var artifacts = Array.CreateInstance(artifactType, 1);
        artifacts.SetValue(artifact, 0);
        reportType.GetProperty("Artifacts")!.SetValue(report, artifacts);

        var json = JsonSerializer.Serialize(report, reportType);

        Assert.Contains("\"name\":\"scene.glb\"", json);
        Assert.Contains("\"path\":\"output/scene.glb\"", json);
        Assert.DoesNotContain("\"Name\"", json);
        Assert.DoesNotContain("\"Path\"", json);
    }

    private static Type GetDomainType(string typeName) =>
        Assembly.Load("SceneBuilder.Domain")
            .GetType($"SceneBuilder.Domain.{typeName}")
        ?? throw new Xunit.Sdk.XunitException($"Expected domain contract '{typeName}' was not found.");
}
