namespace SceneBuilder.Cli;

public enum PlanCommandOperation { Create = 0, Validate = 1, Freeze = 2 }

public sealed record PlanCommand(PlanCommandOperation Operation, string InputPath, string OutputRootDirectory);
