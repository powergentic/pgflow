namespace Powergentic.Flow.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
        => CliApplication.RunAsync(args);
}
