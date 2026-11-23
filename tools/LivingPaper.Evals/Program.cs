using LivingPaper.Evals;
using LivingPaper.Infrastructure;

// The evaluation tool of the characters (decision 0010). See docs/evals.md. Commands: run, compare, latency, charts.
// Exit codes: 0 done, 1 nothing could be graded, 2 something to fix (message shown), 130 stopped with Ctrl+C.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    if (args.Length > 0 && args[0] == "compare")
    {
        var compare = CompareOptions.Parse(args);
        if (compare.ShowHelp)
        {
            Console.WriteLine(CompareOptions.Usage);
            return 0;
        }

        return await CompareCommand.RunAsync(compare, Console.Out, cancellation.Token);
    }

    if (args.Length > 0 && args[0] == "charts")
    {
        var charts = ChartsOptions.Parse(args);
        if (charts.ShowHelp)
        {
            Console.WriteLine(ChartsOptions.Usage);
            return 0;
        }

        return await ChartsCommand.RunAsync(charts, Console.Out, cancellation.Token);
    }

    if (args.Length > 0 && args[0] == "latency")
    {
        // The Langfuse variables, from the .env file of the repository like for `run`; those already set win.
        if (DotEnvFile.Find(Directory.GetCurrentDirectory()) is { } latencyEnv)
        {
            DotEnvFile.Load(latencyEnv);
        }

        var latency = LatencyOptions.Parse(args);
        if (latency.ShowHelp)
        {
            Console.WriteLine(LatencyOptions.Usage);
            return 0;
        }

        return await LatencyCommand.RunAsync(latency, Console.Out, cancellation.Token);
    }

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
