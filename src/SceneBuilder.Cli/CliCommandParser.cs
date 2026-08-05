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
    Plan = 4,
    Invalid = 5,
    Build = 6
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

    public PlanCommand? Plan { get; init; }

    public BuildCommand? Build { get; init; }

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

        if (string.Equals(args[0], "plan", StringComparison.OrdinalIgnoreCase))
        {
            return ParsePlan(args[1..]);
        }

        if (string.Equals(args[0], "build", StringComparison.OrdinalIgnoreCase))
        {
            return ParseBuild(args[1..]);
        }

        return args[0] switch
        {
            "help" or "--help" when args.Length == 1 => new CliCommand { Kind = CliCommandKind.Help },
            "capabilities" => ParseCapabilities(args[1..]),
            _ => Invalid($"Unknown command: {args[0]}")
        };
    }

    private static CliCommand ParseBuild(string[] args)
    {
        string? plan = null;
        string? output = null;
        string? blender = null;
        TimeSpan? timeout = null;
        var format = CliOutputFormat.Text;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1])) return Invalid($"Option {option} requires a non-empty value.");
            var value = args[++index];
            switch (option)
            {
                case "--plan" when plan is null: plan = value; break;
                case "--output" when output is null: output = value; break;
                case "--blender-path" when blender is null: blender = value; break;
                case "--timeout-seconds" when timeout is null && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0: timeout = TimeSpan.FromSeconds(seconds); break;
                case "--format" when value == "text": format = CliOutputFormat.Text; break;
                case "--format" when value == "json": format = CliOutputFormat.Json; break;
                default: return Invalid($"Unknown, duplicate, or invalid option: {option}");
            }
        }
        if (plan is null || output is null) return Invalid("Usage: scene-builder build --plan <file> --output <directory> [--blender-path <file>] [--timeout-seconds <seconds>] [--format text|json]");
        return new CliCommand { Kind = CliCommandKind.Build, OutputFormat = format, Build = new BuildCommand(new BuildFrozenPlanRequest { FrozenPlanPath = Path.GetFullPath(plan), OutputRootDirectory = Path.GetFullPath(output), BlenderExecutablePath = blender is null ? null : Path.GetFullPath(blender), BlenderTimeout = timeout }) };
    }

    private static CliCommand ParsePlan(string[] args)
    {
        if (args.Length < 1 || !TryPlanOperation(args[0], out var operation)) return Invalid("Usage: scene-builder plan <create|validate|freeze> --analysis|--plan <file> --output <directory> [--format text|json]");
        string? input = null;
        string? output = null;
        var format = CliOutputFormat.Text;
        var inputOption = operation is PlanCommandOperation.Create ? "--analysis" : "--plan";
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1])) return Invalid($"Option {option} requires a non-empty value.");
            var value = args[++index];
            switch (option)
            {
                case var expected when expected == inputOption && input is null: input = value; break;
                case "--output" when output is null: output = value; break;
                case "--format" when value is "text": format = CliOutputFormat.Text; break;
                case "--format" when value is "json": format = CliOutputFormat.Json; break;
                default: return Invalid($"Unknown or duplicate option: {option}");
            }
        }
        if (input is null || output is null) return Invalid($"Usage: scene-builder plan {args[0]} {inputOption} <file> --output <directory> [--format text|json]");
        return new CliCommand { Kind = CliCommandKind.Plan, OutputFormat = format, Plan = new PlanCommand(operation, Path.GetFullPath(input), Path.GetFullPath(output)) };
    }

    private static bool TryPlanOperation(string value, out PlanCommandOperation operation)
    {
        operation = value.ToLowerInvariant() switch { "create" => PlanCommandOperation.Create, "validate" => PlanCommandOperation.Validate, "freeze" => PlanCommandOperation.Freeze, _ => (PlanCommandOperation)(-1) };
        return Enum.IsDefined(operation);
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
