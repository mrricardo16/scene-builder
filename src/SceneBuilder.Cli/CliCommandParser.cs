using SceneBuilder.Application.Doctor;
using SceneBuilder.Application;
using SceneBuilder.Domain;

namespace SceneBuilder.Cli;

public enum CliCommandKind
{
    Help = 0,
    Doctor = 1,
    Capabilities = 2,
    Analyze = 3,
    Invalid = 4
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

    public AnalyzeCommand? Analyze { get; init; }

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

        if (string.Equals(args[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            return ParseAnalyze(args[1..]);
        }

        return args[0] switch
        {
            "help" or "--help" when args.Length == 1 => new CliCommand { Kind = CliCommandKind.Help },
            "capabilities" => ParseCapabilities(args[1..]),
            _ => Invalid($"Unknown command: {args[0]}")
        };
    }

    private static CliCommand ParseAnalyze(string[] args)
    {
        string? input = null;
        string? output = null;
        string? rules = null;
        CadUnit? unit = null;
        var format = CliOutputFormat.Text;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return Invalid($"Option {option} requires a non-empty value.");
            }

            var value = args[++index];
            switch (option)
            {
                case "--input" when input is null: input = value; break;
                case "--output" when output is null: output = value; break;
                case "--rules" when rules is null: rules = value; break;
                case "--format" when value is "text": format = CliOutputFormat.Text; break;
                case "--format" when value is "json": format = CliOutputFormat.Json; break;
                case "--unit" when unit is null && TryParseUnit(value, out var parsedUnit): unit = parsedUnit; break;
                default: return Invalid($"Unknown or duplicate option: {option}");
            }
        }

        if (input is null || output is null)
        {
            return Invalid("Usage: scene-builder analyze --input <file> --output <directory> [--rules <file>] [--unit <meters|millimeters|centimeters>] [--format text|json]");
        }

        return new CliCommand
        {
            Kind = CliCommandKind.Analyze,
            OutputFormat = format,
            Analyze = new AnalyzeCommand(new CadImportAnalysisRequest
            {
                InputPath = Path.GetFullPath(input),
                OutputRootDirectory = Path.GetFullPath(output),
                RuleSetPath = rules is null ? null : Path.GetFullPath(rules),
                UnitOverride = unit
            })
        };
    }

    private static bool TryParseUnit(string value, out CadUnit unit)
    {
        unit = value switch
        {
            "meters" => CadUnit.Meters,
            "millimeters" => CadUnit.Millimeters,
            "centimeters" => CadUnit.Centimeters,
            _ => CadUnit.Unknown
        };
        return unit is not CadUnit.Unknown;
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
