using LivingPaper.Evals;
using LivingPaper.Infrastructure;

// The evaluation tool of the characters (decision 0010). See docs/evals.md.
// Exit codes: 0 done, 1 nothing could be graded, 2 something to fix (message shown), 130 stopped with Ctrl+C.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    // Like `dotnet run` of the site in development: variables from the .env file of the repository, those already set win.
    if (DotEnvFile.Find(Directory.GetCurrentDirectory()) is { } dotEnv)
    {
        DotEnvFile.Load(dotEnv);
    }

    var options = EvalOptions.Parse(args, Environment.GetEnvironmentVariable);
    if (options.ShowHelp)
    {
        Console.WriteLine(EvalOptions.Usage);
        return 0;
    }

    return await EvalCommand.RunAsync(options, new ConsoleTerminal(), Environment.GetEnvironmentVariable, cancellation.Token);
}
catch (EvalException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Stopped.");
    return 130;
}
