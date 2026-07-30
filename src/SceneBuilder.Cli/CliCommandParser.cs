using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Cli;

public enum CliCommandKind
{
    Help = 0,
    Doctor = 1,
    Capabilities = 2,
    Invalid = 3
}

public enum CliOutputFormat
{
    Text = 0,
    Json = 1
}

public enum CliExitCode
{
    Success = 0,
    InvalidArguments = 2,
    Cancelled = 3,
    CapabilityUnavailable = 4,
    Failed = 5
}

public sealed record CliCommand
{
    public CliCommandKind Kind { get; init; }

    public DoctorCommand? Doctor { get; init; }

    public CliOutputFormat OutputFormat { get; init; } = CliOutputFormat.Text;

    public string? Error { get; init; }
}

public static class CliCommandParser
{
    public static CliCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            return Invalid("A command is required.");
        }

        if (string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDoctor(args[1..]);
        }

        return args[0] switch
        {
            "help" or "--help" when args.Length == 1 => new CliCommand { Kind = CliCommandKind.Help },
            "capabilities" => ParseCapabilities(args[1..]),
            _ => Invalid($"Unknown command: {args[0]}")
        };
    }

    private static CliCommand ParseDoctor(string[] args)
    {
        if (!DoctorCommandLineParser.TryParse(args, out var command, out var error))
        {
            return Invalid(error ?? "Doctor command arguments are invalid.");
        }

        return new CliCommand { Kind = CliCommandKind.Doctor, Doctor = command };
    }

    private static CliCommand ParseCapabilities(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliCommand { Kind = CliCommandKind.Capabilities };
        }

        if (args.Length != 2 || args[0] != "--format" || string.IsNullOrWhiteSpace(args[1]))
        {
            return Invalid("Usage: scene-builder capabilities [--format text|json]");
        }

        return args[1] switch
        {
            "text" => new CliCommand { Kind = CliCommandKind.Capabilities, OutputFormat = CliOutputFormat.Text },
            "json" => new CliCommand { Kind = CliCommandKind.Capabilities, OutputFormat = CliOutputFormat.Json },
            _ => Invalid($"Unsupported format: {args[1]}")
        };
    }

    private static CliCommand Invalid(string error) => new() { Kind = CliCommandKind.Invalid, Error = error };
}
