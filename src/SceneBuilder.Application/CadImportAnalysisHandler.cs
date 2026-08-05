using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SceneBuilder.Domain;

namespace SceneBuilder.Application;

public sealed class CadImportAnalysisHandler : ISceneOperationHandler<CadImportAnalysisRequest, CadImportAnalysisResult>
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IOutputRootPolicy _outputRootPolicy;
    private readonly CadRuleSetJsonLoader _ruleSetLoader;
    private readonly CadRuleEngine _ruleEngine;
    private readonly CadBuildInputSnapshotFactory _snapshotFactory;
    private readonly CadBuildInputSnapshotSerializer _snapshotSerializer;
    private readonly ICadInputAdapter[] _adapters;

    public CadImportAnalysisHandler(
        IEnumerable<ICadInputAdapter> adapters,
        IOutputRootPolicy outputRootPolicy,
        CadRuleSetJsonLoader? ruleSetLoader = null,
        CadRuleEngine? ruleEngine = null,
        CadBuildInputSnapshotFactory? snapshotFactory = null,
        CadBuildInputSnapshotSerializer? snapshotSerializer = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.OrderBy(adapter => adapter.AdapterId, StringComparer.Ordinal).ToArray();
        _outputRootPolicy = outputRootPolicy ?? throw new ArgumentNullException(nameof(outputRootPolicy));
        _ruleSetLoader = ruleSetLoader ?? new CadRuleSetJsonLoader();
        _ruleEngine = ruleEngine ?? new CadRuleEngine();
        _snapshotFactory = snapshotFactory ?? new CadBuildInputSnapshotFactory();
        _snapshotSerializer = snapshotSerializer ?? new CadBuildInputSnapshotSerializer();
    }

    public async Task<CadImportAnalysisResult> ExecuteAsync(
        CadImportAnalysisRequest request,
        IProgress<SceneOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            Report(progress, "ANALYZE_VALIDATE_REQUEST");
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryValidateRequest(request, _outputRootPolicy, out var inputPath, out var outputRoot, out var error))
            {
                return Failed(error!);
            }

            var descriptor = CreateInputDescriptor(inputPath);
            var matches = _adapters.Where(adapter => adapter.CanHandle(descriptor)).ToArray();
            if (matches.Length != 1)
            {
                return Unsupported("CAD_INPUT_UNSUPPORTED");
            }

            Report(progress, "ANALYZE_STAGE_INPUT");
            cancellationToken.ThrowIfCancellationRequested();
            var controlledInput = CopyControlledInput(inputPath, outputRoot, descriptor.Extension, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprint = ComputeSha256(File.ReadAllBytes(controlledInput));
            var rules = await LoadRulesAsync(request.RuleSetPath, cancellationToken);
            if (rules.Diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error))
            {
                return new CadImportAnalysisResult
                {
                    Status = SceneOperationStatus.Failed,
                    SourceFingerprint = fingerprint,
                    Diagnostics = SanitizeDiagnostics(rules.Diagnostics)
                };
            }

            var adapterResult = await matches[0].AnalyzeAsync(new CadAdapterAnalysisRequest
            {
                ControlledInputPath = controlledInput,
                Input = descriptor,
                UnitOverride = request.UnitOverride
            }, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (adapterResult.Status is not (SceneOperationStatus.Succeeded or SceneOperationStatus.PartiallySucceeded) ||
                adapterResult.SourceDocument is null || adapterResult.Geometry is null || adapterResult.Contours is null || adapterResult.RepairPlan is null)
            {
                return new CadImportAnalysisResult
                {
                    Status = adapterResult.Status,
                    AnalysisId = "analysis-" + ComputeSha256(Encoding.UTF8.GetBytes(fingerprint + "|unsupported"))[..16],
                    SourceFingerprint = fingerprint,
                    Input = new CadImportInputSummary { InputKind = descriptor.SourceFormat },
                    Diagnostics = SanitizeDiagnostics(adapterResult.Diagnostics)
                };
            }

            Report(progress, "ANALYZE_CLASSIFY");
            var classification = rules.RuleSet is null
                ? null
                : _ruleEngine.Classify(new CadClassificationInput
                {
                    Summary = adapterResult.SourceDocument,
                    Geometry = adapterResult.Geometry,
                    Contours = adapterResult.Contours,
                    RuleSet = rules.RuleSet
                });
            if (classification?.Status is CadClassificationStatus.Failed)
            {
                return new CadImportAnalysisResult
                {
                    Status = SceneOperationStatus.Failed,
                    SourceFingerprint = fingerprint,
                    Diagnostics = SanitizeDiagnostics(adapterResult.Diagnostics.Concat(classification.Diagnostics))
                };
            }

            var provisional = CreateResult(adapterResult, descriptor, fingerprint, classification);
            Report(progress, "ANALYZE_BUILD_SNAPSHOT");
            var snapshot = _snapshotFactory.Create(provisional.AnalysisId, fingerprint, adapterResult, classification, provisional.Diagnostics, cancellationToken);
            Report(progress, "ANALYZE_VALIDATE_BUILD_SNAPSHOT");
            CadBuildInputSnapshotValidator.Validate(snapshot);
            var result = provisional with
            {
                BuildInputSnapshot = new CadBuildInputSnapshotDescriptor { Status = CadBuildInputSnapshotStatus.Available, ContractVersion = snapshot.ContractVersion, SnapshotId = snapshot.SnapshotId, ContentHash = snapshot.ContentHash, RelativePath = "analysis/build-input-snapshot.json" },
                Artifacts = [new SceneArtifactDescriptor { Kind = SceneArtifactKind.Analysis, RelativePath = "analysis/cad-analysis.json", IsValidated = true }, new SceneArtifactDescriptor { Kind = SceneArtifactKind.BuildInputSnapshot, RelativePath = "analysis/build-input-snapshot.json", IsValidated = true }]
            };
            CadBuildInputSnapshotDescriptorValidator.Validate(result.BuildInputSnapshot);
            Report(progress, "ANALYZE_WRITE_BUILD_SNAPSHOT");
            cancellationToken.ThrowIfCancellationRequested();
            var snapshotPath = Path.Combine(outputRoot, "analysis", "build-input-snapshot.json");
            try
            {
                await _snapshotSerializer.WriteValidatedAsync(outputRoot, snapshot, cancellationToken);
                Report(progress, "ANALYZE_WRITE_RESULT");
                WriteArtifact(outputRoot, result, cancellationToken);
            }
            catch
            {
                if (File.Exists(snapshotPath))
                {
                    File.Delete(snapshotPath);
                }

                throw;
            }

            Report(progress, "ANALYZE_VALIDATE_RESULT");
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, "ANALYZE_COMPLETED", 100d);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CadImportAnalysisResult { Status = SceneOperationStatus.Cancelled, Diagnostics = [Diagnostic("CAD_ANALYSIS_CANCELLED", DiagnosticSeverity.Warning)] };
        }
        catch (IOException)
        {
            return Failed(Diagnostic("CAD_ANALYSIS_IO_FAILED", DiagnosticSeverity.Error));
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(Diagnostic("CAD_ANALYSIS_IO_FAILED", DiagnosticSeverity.Error));
        }
        catch (Exception)
        {
            return Failed(Diagnostic("CAD_ANALYSIS_FAILED", DiagnosticSeverity.Error));
        }
    }

    private async Task<CadRuleSetLoadResult> LoadRulesAsync(string? ruleSetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ruleSetPath))
        {
            return new CadRuleSetLoadResult();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(ruleSetPath) || !File.Exists(ruleSetPath) || Directory.Exists(ruleSetPath))
        {
            return new CadRuleSetLoadResult { Diagnostics = [Diagnostic("RULE_SOURCE_NOT_FOUND", DiagnosticSeverity.Error)] };
        }

        return _ruleSetLoader.Load(await File.ReadAllTextAsync(ruleSetPath, Utf8WithoutBom, cancellationToken));
    }

    private static CadImportAnalysisResult CreateResult(CadAdapterAnalysisResult adapter, CadInputDescriptor input, string fingerprint, CadClassificationResult? classification)
    {
        var document = adapter.SourceDocument!;
        var contours = adapter.Contours!;
        var repair = adapter.RepairPlan!;
        var classifications = classification?.Objects ?? Array.Empty<CadObjectClassification>();
        var diagnostics = adapter.Diagnostics.Concat(classification?.Diagnostics ?? Array.Empty<SceneDiagnostic>()).ToArray();
        var analysisId = "analysis-" + ComputeSha256(Encoding.UTF8.GetBytes(fingerprint + "|" + (adapter.WasUnitOverridden ? adapter.SourceDocument!.Unit.ToString() : string.Empty) + "|" + (classification is null ? "no-rules" : "rules")))[..16];
        return new CadImportAnalysisResult
        {
            AnalysisId = analysisId,
            SourceFingerprint = fingerprint,
            Status = adapter.Status,
            Input = new CadImportInputSummary
            {
                InputKind = input.SourceFormat,
                Unit = document.Unit,
                UnitStatus = document.Unit is CadUnit.Unknown or CadUnit.Unitless ? "Unknown" : "Known",
                UnitSource = adapter.WasUnitOverridden ? "Override" : "Source"
            },
            OriginalBounds = document.Bounds,
            NormalizedBounds = adapter.Geometry!.Bounds,
            Structure = new CadImportStructureSummary
            {
                Layers = document.Layers.OrderBy(layer => layer.Name, StringComparer.Ordinal).ToArray(),
                Blocks = document.Blocks.OrderBy(block => block.Name, StringComparer.Ordinal).ToArray(),
                EntityTypes = document.EntityTypes.OrderBy(type => type.Type, StringComparer.Ordinal).ToArray(),
                UnsupportedEntityCount = diagnostics.Count(diagnostic => diagnostic.Code == "DXF_ENTITY_UNSUPPORTED")
            },
            Geometry = new CadImportGeometrySummary
            {
                SupportedGeometryCount = adapter.Geometry.Entities.Count,
                OpenSegmentCount = contours.OpenSegments.Count,
                ClosedCandidateCount = contours.Contours.Count,
                ValidContourCount = contours.Contours.Count(contour => contour.ValidationState is CadContourValidationState.Valid),
                InvalidContourCount = contours.Contours.Count(contour => contour.ValidationState is CadContourValidationState.Invalid)
            },
            Repair = new CadImportRepairSummary
            {
                CandidateCount = repair.Actions.Count,
                ApplicableCount = repair.Status is CadGeometryRepairPlanStatus.Ready ? repair.Actions.Count : 0,
                RejectedCount = repair.Status is CadGeometryRepairPlanStatus.HasConflicts ? repair.Actions.Count : 0,
                UnresolvedIssueCount = repair.Diagnostics.Count(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)
            },
            Classification = CreateClassificationSummary(classification),
            AssetCandidates = classifications.Where(item => item.Classification is CadSemanticClassification.StaticFacility or CadSemanticClassification.DynamicEquipment)
                .Select(item => item.Subject.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Diagnostics = SanitizeDiagnostics(diagnostics)
        };
    }

    private static CadImportClassificationSummary CreateClassificationSummary(CadClassificationResult? classification)
    {
        if (classification is null)
        {
            return new CadImportClassificationSummary { Status = "NotConfigured" };
        }

        return new CadImportClassificationSummary
        {
            Status = classification.Status.ToString(),
            WallCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.Wall),
            ColumnCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.Column),
            FloorCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.Floor),
            RoadCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.Road),
            StaticFacilityCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.StaticFacility),
            DynamicEquipmentCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.DynamicEquipment),
            UnclassifiedCount = classification.Objects.Count(item => item.Classification is CadSemanticClassification.Unclassified),
            RuleConflictCount = classification.Diagnostics.Count(diagnostic => diagnostic.Code == "RULE_CONFLICT")
        };
    }

    private static void WriteArtifact(string outputRoot, CadImportAnalysisResult result, CancellationToken cancellationToken)
    {
        var analysisDirectory = Path.Combine(outputRoot, "analysis");
        var destination = Path.Combine(analysisDirectory, "cad-analysis.json");
        Directory.CreateDirectory(analysisDirectory);
        if (File.Exists(destination))
        {
            throw new IOException("The analysis artifact already exists.");
        }

        var staging = Path.Combine(analysisDirectory, ".cad-analysis.staging");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(staging, JsonSerializer.Serialize(result, SerializerOptions), Utf8WithoutBom);
            cancellationToken.ThrowIfCancellationRequested();
            using var roundTrip = JsonDocument.Parse(File.ReadAllText(staging, Utf8WithoutBom));
            if (!roundTrip.RootElement.TryGetProperty("analysisId", out var analysisId) ||
                !string.Equals(analysisId.GetString(), result.AnalysisId, StringComparison.Ordinal))
            {
                throw new IOException("The analysis artifact could not be validated.");
            }

            File.Move(staging, destination);
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }
    }

    private static string CopyControlledInput(string inputPath, string outputRoot, string extension, CancellationToken cancellationToken)
    {
        if (IsSameOrUnder(outputRoot, Path.GetDirectoryName(inputPath)!))
        {
            throw new IOException("The output root must not be within the input directory.");
        }

        var inputDirectory = Path.Combine(outputRoot, "input");
        var target = Path.Combine(inputDirectory, "source" + extension.ToLowerInvariant());
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException("The controlled input already exists.");
        }

        Directory.CreateDirectory(inputDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        File.Copy(inputPath, target, overwrite: false);
        cancellationToken.ThrowIfCancellationRequested();
        return target;
    }

    private static bool TryValidateRequest(CadImportAnalysisRequest request, IOutputRootPolicy outputRootPolicy, out string inputPath, out string outputRoot, out SceneDiagnostic? error)
    {
        inputPath = string.Empty;
        outputRoot = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(request.InputPath) || !Path.IsPathFullyQualified(request.InputPath))
        {
            error = Diagnostic("CAD_INPUT_PATH_INVALID", DiagnosticSeverity.Error);
            return false;
        }

        inputPath = Path.GetFullPath(request.InputPath);
        if (!File.Exists(inputPath) || Directory.Exists(inputPath))
        {
            error = Diagnostic("CAD_INPUT_NOT_FOUND", DiagnosticSeverity.Error);
            return false;
        }

        if ((File.GetAttributes(inputPath) & FileAttributes.ReparsePoint) != 0)
        {
            error = Diagnostic("CAD_INPUT_REPARSE_POINT_REJECTED", DiagnosticSeverity.Error);
            return false;
        }

        var validation = outputRootPolicy.Validate(request.OutputRootDirectory);
        if (!validation.IsValid || validation.NormalizedPath is null)
        {
            error = Diagnostic("CAD_OUTPUT_ROOT_INVALID", DiagnosticSeverity.Error);
            return false;
        }

        outputRoot = validation.NormalizedPath;
        if (Directory.Exists(outputRoot) && (File.GetAttributes(outputRoot) & FileAttributes.ReparsePoint) != 0)
        {
            error = Diagnostic("CAD_OUTPUT_ROOT_REPARSE_POINT_REJECTED", DiagnosticSeverity.Error);
            return false;
        }

        return true;
    }

    private static CadInputDescriptor CreateInputDescriptor(string path)
    {
        var extension = Path.GetExtension(path);
        return new CadInputDescriptor
        {
            Extension = extension,
            SourceFormat = extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase) ? CadSourceFormat.Dxf :
                extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase) ? CadSourceFormat.Dwg : CadSourceFormat.Unknown
        };
    }

    private static IReadOnlyList<SceneDiagnostic> SanitizeDiagnostics(IEnumerable<SceneDiagnostic> diagnostics) => diagnostics
        .Select(diagnostic => diagnostic with { SourcePath = null })
        .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
        .ToArray();

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsSameOrUnder(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static CadImportAnalysisResult Failed(SceneDiagnostic diagnostic) => new() { Status = SceneOperationStatus.Failed, Diagnostics = [diagnostic] };

    private static CadImportAnalysisResult Unsupported(string code) => new() { Status = SceneOperationStatus.Unsupported, Diagnostics = [Diagnostic(code, DiagnosticSeverity.Warning)] };

    private static SceneDiagnostic Diagnostic(string code, DiagnosticSeverity severity) => new() { Code = code, Severity = severity, Message = "CAD analysis did not complete normally." };

    private static void Report(IProgress<SceneOperationProgress>? progress, string stageCode, double? percent = null) => progress?.Report(new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = stageCode, Percent = percent });
}
