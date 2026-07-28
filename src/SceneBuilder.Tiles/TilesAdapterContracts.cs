using SceneBuilder.Domain;

namespace SceneBuilder.Tiles;

public enum TilesProcessStatus
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2
}

public sealed record TilesProcessRequest
{
    public string ExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string WorkingDirectory { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;
}

public sealed record TilesProcessResult
{
    public TilesProcessStatus Status { get; init; } = TilesProcessStatus.Failed;

    public int? ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;
}

public interface ITilesProcessRunner
{
    Task<TilesProcessResult> RunAsync(
        TilesProcessRequest request,
        CancellationToken cancellationToken);
}

public sealed class NotConfiguredTilesConverter : ITilesConverter
{
    public Task<TilesConversionResult> ConvertAsync(
        TilesConversionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new TilesConversionResult
        {
            Status = TilesConversionStatus.NotConfigured,
            OutputPath = null,
            Diagnostics =
            [
                new SceneDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "TILES_NOT_CONFIGURED",
                    Message = "3D Tiles conversion is not configured. No output was created."
                }
            ]
        });
    }
}
