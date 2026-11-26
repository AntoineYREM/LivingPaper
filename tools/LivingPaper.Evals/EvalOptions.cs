using System.Globalization;
using LivingPaper.Application;

namespace LivingPaper.Evals;

// How the passages of the book are searched. Embedding is the name of a model of the Settings page; with EmbeddingUrl it is the model
// of a server that asks for no key (a local one), which the site does not need to have saved.
internal sealed record RetrievalOptions(string Embedding, string? EmbeddingUrl, QueryMode Mode, int Passages);

// The command line: `run`, with a few options. A model can also be named by an environment variable (EVAL_CHAT_PROVIDER,
// EVAL_JUDGE_PROVIDER, from the shell or .env), for a run with no keyboard.
internal sealed record EvalOptions(
    bool ShowHelp,
    int Repeats,
    string? QuestionsPath,
    string? JudgePromptPath,
    string? ChatProvider,
    string? JudgeProvider,
    bool LocalOnly = false,
    string? PromptsPath = null,
    string? Label = null,
    int? MaxOutputTokens = null,
    string? BookDataPath = null,
    RetrievalOptions? Retrieval = null)
{
    public const int DefaultRepeats = 3;
    public const int MaxRepeats = 20;
    public const int MaxLabelLength = 60;
    public const int MaxCeiling = 200_000;

    private static readonly string[] SearchOptions = ["--embedding", "--embedding-url", "--query-mode", "--passages"];

    public const string Usage = """
        Usage: dotnet run --project tools/LivingPaper.Evals -- run [options]

        Puts the questions of evals/bovary/questions.json to the character through the real chat, has a judge grade each reply
        from 1 to 5, and prints the average by category.

        Options:
          --repeats <n>          times each question is asked (1 to 20, default 3)
          --chat <name>          language model of the character, by its name on the Settings page
          --judge <name>         language model of the judge, by its name on the Settings page
          --questions <file>     another questions file (default evals/bovary/questions.json)
          --judge-prompt <file>  another judge prompt (default evals/judge.json)
          --prompts-file <file>  another prompts file for the chat (default: prompts.json of the data folder), to try a change of prompt
          --label <text>         a short name for this run (for example "rule-no-ai"), kept in the run file and shown by compare
          --max-tokens <n>       ceiling on the length of every reply, thinking included (default: none). Useful with an expensive model on
                                 OpenRouter, which sets aside the cost of the longest reply first; not for OpenAI's own newest models
          --local-only           keep the run in evals/runs/ only, even when Langfuse is configured
          --book-data <folder>   a data folder whose library holds the book (books/ and rag.db), instead of examples/
          --embedding <name>     search the passages of the book with this model for vectorization (its name on the Settings page);
                                 without it the character gets the whole story. Needs --book-data, with the book indexed by it
          --embedding-url <url>  address of a server that asks for no key: --embedding is then the name of its model
          --query-mode <mode>    recent (default), rewrite or last: how the search is written, as RAG_QUERY_MODE for the site
          --passages <n>         passages given to the character, 1 to 10 (default 5), as RAG_PASSAGES
          --help                 this text

        Every run is saved in evals/runs/ (not committed: it holds the replies). With the LANGFUSE_* variables set (shell or .env) it
        is also sent to Langfuse: the dataset, one trace per answer, and the grades as scores.

        Without --chat and --judge the tool shows the list of models and asks. In a script, name them here or with
        EVAL_CHAT_PROVIDER and EVAL_JUDGE_PROVIDER. The data folder is DATA_DIRECTORY (default ./data), the same as the site's.
        """;

    // The first argument must be "run" (or --help). A repeated, unknown or incomplete option is an error that says so.
    public static EvalOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        if (args.Count == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            return new EvalOptions(true, DefaultRepeats, null, null, null, null);
        }

        if (args[0] != "run")
        {
            throw new EvalException($"Unknown command '{args[0]}'. The only command is 'run'.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var localOnly = false;
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            if (option == "--local-only")
            {
                localOnly = true;
                index--;
                continue;
            }

            if (option is not ("--repeats" or "--chat" or "--judge" or "--questions" or "--judge-prompt" or "--prompts-file" or "--label" or "--max-tokens"
                or "--book-data" or "--embedding" or "--embedding-url" or "--query-mode" or "--passages"))
            {
                throw new EvalException($"Unknown option '{option}'. Use --help to see the options.");
            }

            if (index + 1 >= args.Count)
            {
                throw new EvalException($"The option {option} needs a value.");
            }

            if (!values.TryAdd(option, args[index + 1]))
            {
                throw new EvalException($"The option {option} is given twice.");
            }
        }

        var repeats = DefaultRepeats;
        if (values.TryGetValue("--repeats", out var repeatsText)
            && (!int.TryParse(repeatsText, NumberStyles.None, CultureInfo.InvariantCulture, out repeats) || repeats is < 1 or > MaxRepeats))
        {
            throw new EvalException($"--repeats must be a whole number from 1 to {MaxRepeats}.");
        }

        var label = values.GetValueOrDefault("--label")?.Trim();
        if (label is not null && (label.Length is 0 or > MaxLabelLength || label.Any(char.IsControl)))
        {
            throw new EvalException($"--label must be a short text of 1 to {MaxLabelLength} characters.");
        }

        int? ceiling = null;
        if (values.TryGetValue("--max-tokens", out var ceilingText))
        {
            if (!int.TryParse(ceilingText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > MaxCeiling)
            {
                throw new EvalException($"--max-tokens must be a whole number from 1 to {MaxCeiling}.");
            }

            ceiling = parsed;
        }

        var bookData = values.GetValueOrDefault("--book-data");
        var retrieval = ReadRetrieval(values, bookData);

        return new EvalOptions(
            ShowHelp: false,
            repeats,
            values.GetValueOrDefault("--questions"),
            values.GetValueOrDefault("--judge-prompt"),
            values.GetValueOrDefault("--chat") ?? Blank(environment("EVAL_CHAT_PROVIDER")),
            values.GetValueOrDefault("--judge") ?? Blank(environment("EVAL_JUDGE_PROVIDER")),
            localOnly,
            values.GetValueOrDefault("--prompts-file"),
            label,
            ceiling,
            bookData,
            retrieval);
    }

    private static RetrievalOptions? ReadRetrieval(Dictionary<string, string> values, string? bookData)
    {
        var embedding = Blank(values.GetValueOrDefault("--embedding"));
        if (embedding is null)
        {
            var alone = SearchOptions.FirstOrDefault(values.ContainsKey);
            return alone is null
                ? null
                : throw new EvalException($"{alone} is about the search of passages: it needs --embedding <name>.");
        }

        if (bookData is null)
        {
            throw new EvalException("--embedding needs --book-data <folder>: the books of examples/ have no passages.");
        }

        var url = values.GetValueOrDefault("--embedding-url");
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var address) || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)))
        {
            throw new EvalException("--embedding-url must be an http or https address, for example http://localhost:11434/v1.");
        }

        var mode = new RagOptions().Mode;
        if (values.TryGetValue("--query-mode", out var modeText))
        {
            mode = modeText.ToLowerInvariant() switch
            {
                "rewrite" => QueryMode.Rewrite,
                "recent" => QueryMode.Recent,
                "last" => QueryMode.Last,
                _ => throw new EvalException("--query-mode must be rewrite, recent or last."),
            };
        }

        var passages = new RagOptions().Passages;
        if (values.TryGetValue("--passages", out var passagesText)
            && (!int.TryParse(passagesText, NumberStyles.None, CultureInfo.InvariantCulture, out passages) || passages is < 1 or > RagOptions.MaxPassages))
        {
            throw new EvalException($"--passages must be a whole number from 1 to {RagOptions.MaxPassages}.");
        }

        return new RetrievalOptions(embedding, url, mode, passages);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
