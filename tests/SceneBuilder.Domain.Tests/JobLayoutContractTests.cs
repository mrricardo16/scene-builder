using System.Reflection;

namespace SceneBuilder.Domain.Tests;

public sealed class JobLayoutContractTests
{
    [Fact]
    public void Create_returns_stable_isolated_directories_for_the_same_job()
    {
        var jobLayoutType = GetDomainType("JobLayout");
        var createMethod = jobLayoutType.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(createMethod);

        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-contract-tests");
        var firstLayout = createMethod!.Invoke(null, [outputRoot, "job-42"]);
        var secondLayout = createMethod.Invoke(null, [outputRoot, "job-42"]);

        Assert.NotNull(firstLayout);
        Assert.NotNull(secondLayout);

        var expectedJobDirectory = Path.GetFullPath(Path.Combine(outputRoot, "job-42"));
        Assert.Equal(expectedJobDirectory, GetStringProperty(firstLayout, "JobDirectory"));
        Assert.Equal(expectedJobDirectory, GetStringProperty(secondLayout, "JobDirectory"));
        Assert.Equal(Path.Combine(expectedJobDirectory, "input"), GetStringProperty(firstLayout, "InputDirectory"));
        Assert.Equal(Path.Combine(expectedJobDirectory, "intermediate"), GetStringProperty(firstLayout, "IntermediateDirectory"));
        Assert.Equal(Path.Combine(expectedJobDirectory, "output"), GetStringProperty(firstLayout, "OutputDirectory"));
        Assert.Equal(Path.Combine(expectedJobDirectory, "reports"), GetStringProperty(firstLayout, "ReportsDirectory"));
    }

    [Theory]
    [InlineData("job-42.")]
    [InlineData("job-42 ")]
    public void Create_rejects_job_ids_that_windows_normalizes(string jobId)
    {
        var jobLayoutType = GetDomainType("JobLayout");
        var createMethod = jobLayoutType.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(createMethod);

        var exception = Assert.Throws<TargetInvocationException>(
            () => createMethod!.Invoke(null, [Path.GetTempPath(), jobId]));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    public static IEnumerable<object[]> WindowsReservedJobIds()
    {
        yield return ["CON"];
        yield return ["con.json"];
        yield return ["PRN"];
        yield return ["prn.log"];
        yield return ["AUX"];
        yield return ["aux.txt"];
        yield return ["NUL"];
        yield return ["nul.data"];

        for (var deviceNumber = 1; deviceNumber <= 9; deviceNumber++)
        {
            yield return [$"COM{deviceNumber}"];
            yield return [$"com{deviceNumber}.json"];
            yield return [$"LPT{deviceNumber}"];
            yield return [$"lpt{deviceNumber}.log"];
        }

        foreach (var superscriptNumber in new[] { '¹', '²', '³' })
        {
            yield return [$"COM{superscriptNumber}"];
            yield return [$"com{superscriptNumber}.json"];
            yield return [$"LPT{superscriptNumber}"];
            yield return [$"lpt{superscriptNumber}.log"];
        }
    }

    [Theory]
    [MemberData(nameof(WindowsReservedJobIds))]
    public void Create_rejects_windows_reserved_device_basenames(string jobId)
    {
        var createMethod = GetCreateMethod();

        var exception = Assert.Throws<TargetInvocationException>(
            () => createMethod.Invoke(null, [Path.GetTempPath(), jobId]));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Create_uses_separate_layouts_for_distinct_valid_job_ids()
    {
        var createMethod = GetCreateMethod();
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-contract-tests");
        var firstLayout = createMethod.Invoke(null, [outputRoot, "drawing-a"]);
        var secondLayout = createMethod.Invoke(null, [outputRoot, "drawing-b"]);

        Assert.NotNull(firstLayout);
        Assert.NotNull(secondLayout);
        Assert.NotEqual(
            GetStringProperty(firstLayout, "JobDirectory"),
            GetStringProperty(secondLayout, "JobDirectory"));
        Assert.NotEqual(
            GetStringProperty(firstLayout, "OutputDirectory"),
            GetStringProperty(secondLayout, "OutputDirectory"));
    }

    [Fact]
    public void Create_keeps_all_derived_paths_beneath_the_supplied_output_root()
    {
        var createMethod = GetCreateMethod();
        var outputRoot = Path.Combine(Path.GetTempPath(), "scene-builder-contract-tests", "root");
        var layout = createMethod.Invoke(null, [outputRoot, "drawing-a"]);

        Assert.NotNull(layout);

        var normalizedRoot = Path.GetFullPath(outputRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var derivedPaths = new[]
        {
            "JobDirectory",
            "InputDirectory",
            "IntermediateDirectory",
            "OutputDirectory",
            "ReportsDirectory",
            "ReportPath"
        };

        foreach (var propertyName in derivedPaths)
        {
            Assert.StartsWith(
                normalizedRoot,
                GetStringProperty(layout, propertyName),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Type GetDomainType(string typeName) =>
        Assembly.Load("SceneBuilder.Domain")
            .GetType($"SceneBuilder.Domain.{typeName}")
        ?? throw new Xunit.Sdk.XunitException($"Expected domain contract '{typeName}' was not found.");

    private static MethodInfo GetCreateMethod()
    {
        var createMethod = GetDomainType("JobLayout").GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static);

        return Assert.IsAssignableFrom<MethodInfo>(createMethod);
    }

    private static string GetStringProperty(object instance, string propertyName) =>
        Assert.IsType<string>(instance.GetType().GetProperty(propertyName)?.GetValue(instance));
}
