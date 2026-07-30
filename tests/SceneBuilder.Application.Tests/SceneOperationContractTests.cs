using SceneBuilder.Domain;

namespace SceneBuilder.Application.Tests;

public sealed class SceneOperationContractTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0d, true)]
    [InlineData(100d, true)]
    [InlineData(-0.01d, false)]
    [InlineData(100.01d, false)]
    public void ValidateProgress_accepts_only_known_percentages_between_zero_and_one_hundred(double? percent, bool expected)
    {
        var validation = SceneOperationContractValidator.ValidateProgress(new SceneOperationProgress
        {
            Phase = SceneWorkflowPhase.Analyze,
            StageCode = "READ_INPUT",
            Percent = percent
        });

        Assert.Equal(expected, validation.IsValid);
    }

    [Fact]
    public void ValidateProgress_rejects_invalid_numbers_counts_and_stage_codes()
    {
        var invalidProgress = new[]
        {
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = string.Empty },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "read-input" },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "READ", Percent = double.NaN },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "READ", Percent = double.PositiveInfinity },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "READ", Current = 1 },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "READ", Total = 1 },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "READ", Current = 2, Total = 1 },
            new SceneOperationProgress { Phase = SceneWorkflowPhase.Analyze, StageCode = "READ", Current = -1, Total = 1 }
        };

        Assert.All(invalidProgress, progress => Assert.False(SceneOperationContractValidator.ValidateProgress(progress).IsValid));
    }

    [Theory]
    [InlineData("analysis/result.json", true)]
    [InlineData("artifacts/model.glb", true)]
    [InlineData("artifacts/package/scene-package.json", true)]
    [InlineData("artifacts/tiles/tileset.json", true)]
    [InlineData("../escape", false)]
    [InlineData(@"C:\\outside", false)]
    [InlineData(@"\\\\server\\share", false)]
    [InlineData("file://artifact", false)]
    [InlineData("http://artifact", false)]
    [InlineData("mailto:artifact", false)]
    [InlineData(@"artifacts\\model.glb", false)]
    [InlineData("", false)]
    public void ValidateArtifact_accepts_only_controlled_forward_slash_relative_paths(string relativePath, bool expected)
    {
        var validation = SceneOperationContractValidator.ValidateArtifact(new SceneArtifactDescriptor
        {
            Kind = SceneArtifactKind.Glb,
            RelativePath = relativePath,
            IsValidated = true
        });

        Assert.Equal(expected, validation.IsValid);
    }

    [Theory]
    [InlineData(SceneOperationStatus.Succeeded, true, true)]
    [InlineData(SceneOperationStatus.Succeeded, false, false)]
    [InlineData(SceneOperationStatus.PartiallySucceeded, true, true)]
    [InlineData(SceneOperationStatus.PartiallySucceeded, false, false)]
    [InlineData(SceneOperationStatus.Failed, true, false)]
    [InlineData(SceneOperationStatus.Cancelled, true, false)]
    [InlineData(SceneOperationStatus.NotConfigured, true, false)]
    [InlineData(SceneOperationStatus.Unsupported, true, false)]
    public void ValidateResult_allows_artifacts_only_for_successful_states(
        SceneOperationStatus status,
        bool artifactIsValidated,
        bool expected)
    {
        var validation = SceneOperationContractValidator.ValidateResult(new SceneOperationResult
        {
            Status = status,
            Artifacts =
            [
                new SceneArtifactDescriptor
                {
                    Kind = SceneArtifactKind.Report,
                    RelativePath = "reports/result.json",
                    IsValidated = artifactIsValidated
                }
            ]
        });

        Assert.Equal(expected, validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_forwards_request_progress_and_cancellation_to_a_strongly_typed_handler()
    {
        using var cancellationSource = new CancellationTokenSource();
        var expectedProgress = new SceneOperationProgress
        {
            Phase = SceneWorkflowPhase.Analyze,
            StageCode = "READ_INPUT"
        };
        var progress = new Progress<SceneOperationProgress>();
        var handler = new FakeHandler(expectedProgress);
        var executor = new SceneOperationExecutor<string>(handler);

        var result = await executor.ExecuteAsync("request-42", progress, cancellationSource.Token);

        Assert.Equal(SceneOperationStatus.Succeeded, result.Status);
        Assert.Equal("request-42", handler.Request);
        Assert.Same(progress, handler.Progress);
        Assert.Equal(cancellationSource.Token, handler.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_converts_handler_exceptions_to_failed_results()
    {
        var executor = new SceneOperationExecutor<string>(new ThrowingHandler());

        var result = await executor.ExecuteAsync("request", progress: null, CancellationToken.None);

        Assert.Equal(SceneOperationStatus.Failed, result.Status);
        Assert.Empty(result.Artifacts);
        Assert.Contains(result.Diagnostics, item => item.Code == "SCENE_OPERATION_FAILED");
    }

    [Fact]
    public async Task ExecuteAsync_converts_an_invalid_handler_result_to_a_failed_result_without_artifacts()
    {
        var executor = new SceneOperationExecutor<string>(new InvalidResultHandler());

        var result = await executor.ExecuteAsync("request", progress: null, CancellationToken.None);

        Assert.Equal(SceneOperationStatus.Failed, result.Status);
        Assert.Empty(result.Artifacts);
        Assert.Contains(result.Diagnostics, item => item.Code == "SCENE_OPERATION_RESULT_INVALID");
    }

    private sealed class FakeHandler(SceneOperationProgress expectedProgress) : ISceneOperationHandler<string, SceneOperationResult>
    {
        public string? Request { get; private set; }

        public IProgress<SceneOperationProgress>? Progress { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<SceneOperationResult> ExecuteAsync(
            string request,
            IProgress<SceneOperationProgress>? progress,
            CancellationToken cancellationToken)
        {
            Request = request;
            Progress = progress;
            CancellationToken = cancellationToken;
            progress?.Report(expectedProgress);
            return Task.FromResult(new SceneOperationResult { Status = SceneOperationStatus.Succeeded });
        }
    }

    private sealed class ThrowingHandler : ISceneOperationHandler<string, SceneOperationResult>
    {
        public Task<SceneOperationResult> ExecuteAsync(
            string request,
            IProgress<SceneOperationProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Expected test failure.");
    }

    private sealed class InvalidResultHandler : ISceneOperationHandler<string, SceneOperationResult>
    {
        public Task<SceneOperationResult> ExecuteAsync(
            string request,
            IProgress<SceneOperationProgress>? progress,
            CancellationToken cancellationToken) => Task.FromResult(new SceneOperationResult
        {
            Status = SceneOperationStatus.Failed,
            Artifacts =
            [
                new SceneArtifactDescriptor
                {
                    Kind = SceneArtifactKind.Report,
                    RelativePath = "reports/invalid.json",
                    IsValidated = true
                }
            ]
        });
    }
}
