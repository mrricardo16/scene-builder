using SceneBuilder.Application;
using SceneBuilder.Domain;

namespace SceneBuilder.Blender;

public sealed class BlenderSceneGenerator : IBlenderSceneGenerator
{
    private const string StatusLine = "SCENEBUILDER_STATUS:SUCCEEDED";
    private readonly IBlenderProcessRunner _processRunner;
    private readonly BlenderManifestMapper _mapper;
    private readonly BinaryGlbValidator _validator;

    public BlenderSceneGenerator(IBlenderProcessRunner? processRunner = null)
        : this(processRunner, null, null)
    {
    }

    internal BlenderSceneGenerator(IBlenderProcessRunner? processRunner, BlenderManifestMapper? mapper, BinaryGlbValidator? validator)
    {
        _processRunner = processRunner ?? new BlenderProcessRunner();
        _mapper = mapper ?? new BlenderManifestMapper();
        _validator = validator ?? new BinaryGlbValidator();
    }

    public async Task<BlenderGenerationResult> GenerateAsync(BlenderGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryValidateRequest(request, out var outputPath))
        {
            return Failed("BLENDER_REQUEST_INVALID");
        }

        var mapping = _mapper.Map(request.Draft);
        if (!mapping.IsValid)
        {
            return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, mapping.Diagnostics);
        }

        if (!File.Exists(request.Tool.ExecutablePath))
        {
            return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_EXECUTABLE_NOT_FOUND", DiagnosticSeverity.Error)]);
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "generate_scene.py");
        if (!File.Exists(scriptPath))
        {
            return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_SCRIPT_UNAVAILABLE", DiagnosticSeverity.Error)]);
        }

        var workDirectory = string.Empty;
        try
        {
            Directory.CreateDirectory(request.OutputDirectory);
            if (!request.AllowOverwrite && File.Exists(outputPath))
            {
                return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_OUTPUT_EXISTS", DiagnosticSeverity.Error)]);
            }

            workDirectory = Path.Combine(request.OutputDirectory, ".scene-builder-staging", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            var manifestPath = Path.Combine(workDirectory, "manifest.json");
            var stagingPath = Path.Combine(workDirectory, "scene.glb");
            await File.WriteAllTextAsync(manifestPath, BlenderManifestMapper.Serialize(mapping.Manifest!), cancellationToken);
            var process = await _processRunner.RunAsync(
                BlenderCommandBuilder.Create(request.Tool.ExecutablePath, scriptPath, manifestPath, stagingPath, workDirectory, request.Tool.Timeout, request.Tool.MaximumProcessOutputCharacters),
                cancellationToken);
            if (process.Status is BlenderProcessStatus.Cancelled)
            {
                return CreateResult(BlenderGenerationStatus.Cancelled, null, 0, mapping, [Diagnostic("BLENDER_PROCESS_CANCELLED", DiagnosticSeverity.Warning)], process.ExitCode);
            }

            if (process.Status is BlenderProcessStatus.TimedOut)
            {
                return CreateResult(BlenderGenerationStatus.TimedOut, null, 0, mapping, [Diagnostic("BLENDER_PROCESS_TIMED_OUT", DiagnosticSeverity.Error)], process.ExitCode);
            }

            if (process.Status is not BlenderProcessStatus.Succeeded || process.ExitCode != 0)
            {
                return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_PROCESS_EXITED_NONZERO", DiagnosticSeverity.Error)], process.ExitCode);
            }

            if (!process.StandardOutput.Split('\n').Any(line => string.Equals(line.Trim(), StatusLine, StringComparison.Ordinal)))
            {
                return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_PROCESS_STATUS_INVALID", DiagnosticSeverity.Error)], process.ExitCode);
            }

            var validation = _validator.Validate(stagingPath);
            if (!validation.IsValid)
            {
                return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic(validation.DiagnosticCode!, DiagnosticSeverity.Error)], process.ExitCode);
            }

            File.Move(stagingPath, outputPath, request.AllowOverwrite);
            var status = mapping.SkippedSemanticObjectIds.Count == 0 ? BlenderGenerationStatus.Succeeded : BlenderGenerationStatus.PartiallySucceeded;
            return CreateResult(status, outputPath, mapping.Manifest!.Objects.Count, mapping, mapping.Diagnostics, process.ExitCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(BlenderGenerationStatus.Cancelled, null, 0, mapping, [Diagnostic("BLENDER_PROCESS_CANCELLED", DiagnosticSeverity.Warning)]);
        }
        catch (IOException)
        {
            return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_OUTPUT_PUBLISH_FAILED", DiagnosticSeverity.Error)]);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_OUTPUT_UNAVAILABLE", DiagnosticSeverity.Error)]);
        }
        catch (ArgumentException)
        {
            return CreateResult(BlenderGenerationStatus.Failed, null, 0, mapping, [Diagnostic("BLENDER_OUTPUT_UNAVAILABLE", DiagnosticSeverity.Error)]);
        }
        finally
        {
            TryDeleteWorkDirectory(workDirectory);
        }
    }

    private static bool TryValidateRequest(BlenderGenerationRequest request, out string outputPath)
    {
        outputPath = string.Empty;
        if (request.Draft is null || request.Tool is null || string.IsNullOrWhiteSpace(request.OutputDirectory) || string.IsNullOrWhiteSpace(request.Tool.ExecutablePath) || request.Tool.Timeout <= TimeSpan.Zero || request.Tool.MaximumProcessOutputCharacters <= 0 || !IsSafeOutputFileName(request.OutputFileName))
        {
            return false;
        }

        try
        {
            outputPath = Path.GetFullPath(Path.Combine(request.OutputDirectory, request.OutputFileName));
            var root = Path.GetFullPath(request.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static bool IsSafeOutputFileName(string value) =>
        !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains("..", StringComparison.Ordinal) && value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 && string.Equals(Path.GetExtension(value), ".glb", StringComparison.OrdinalIgnoreCase);

    private static BlenderGenerationResult Failed(string code) =>
        new() { Status = BlenderGenerationStatus.Failed, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Error)] };

    private static BlenderGenerationResult CreateResult(BlenderGenerationStatus status, string? artifactPath, int generatedCount, BlenderManifestMappingResult mapping, IReadOnlyList<SceneDiagnostic> diagnostics, int? exitCode = null) =>
        new()
        {
            Status = status,
            ArtifactPath = artifactPath,
            GeneratedObjectCount = generatedCount,
            SkippedObjectCount = mapping.SkippedSemanticObjectIds.Count,
            SkippedSemanticObjectIds = mapping.SkippedSemanticObjectIds,
            Diagnostics = mapping.Diagnostics.Concat(diagnostics).Distinct().OrderBy(item => item.Code, StringComparer.Ordinal).ToArray(),
            ProcessExitCode = exitCode
        };

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "Blender generation did not complete normally." };

    private static void TryDeleteWorkDirectory(string workDirectory)
    {
        try { if (Directory.Exists(workDirectory)) { Directory.Delete(workDirectory, recursive: true); } }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
