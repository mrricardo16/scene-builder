using SceneBuilder.Application.Doctor;

namespace SceneBuilder.Cli;

public sealed record DoctorCommand(DoctorOptions DoctorOptions, string? OutputDirectory);

public static class DoctorCommandLineParser
{
    public static bool TryParse(string[] args, out DoctorCommand? command, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? outputDirectory = null;
        string? blenderPath = null;
        string? tilesPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--output" or "--blender-path" or "--tiles-path"))
            {
                command = null;
                error = $"Unknown option: {option}";
                return false;
            }

            if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                command = null;
                error = $"Option {option} requires a non-empty value.";
                return false;
            }

            var value = args[++index];
            if (option == "--output")
            {
                if (outputDirectory is not null)
                {
                    command = null;
                    error = "Option --output may only be specified once.";
                    return false;
                }

                outputDirectory = value;
            }
            else if (option == "--blender-path")
            {
                if (blenderPath is not null)
                {
                    command = null;
                    error = "Option --blender-path may only be specified once.";
                    return false;
                }

                blenderPath = value;
            }
            else
            {
                if (tilesPath is not null)
                {
                    command = null;
                    error = "Option --tiles-path may only be specified once.";
                    return false;
                }

                tilesPath = value;
            }
        }

        command = new DoctorCommand(
            new DoctorOptions { BlenderPath = blenderPath, TilesPath = tilesPath },
            outputDirectory);
        error = null;
        return true;
    }
}
