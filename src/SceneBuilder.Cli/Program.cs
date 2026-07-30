using System.Text;
using SceneBuilder.Composition;

namespace SceneBuilder.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        var host = SceneBuilderComposition.CreateDefault();
        var application = new SceneBuilderCliApplication(host, Console.Out, Console.Error, new DoctorReportWriter());
        return await application.RunAsync(args, cancellationSource.Token);
    }
}
