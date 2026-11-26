using System.Globalization;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Infrastructure;
using LivingPaper.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LivingPaper.Evals;

// The composition of the `run` command: the same pieces as the site (settings, prompts, examples, the chat), assembled for one run.
// The site does not need to be running. The data folder is the site's (see DataFolder): the models and their encrypted keys are read
// from it, and the prompts too, so the character is played with the prompts you edited.
// Every run is saved in evals/runs/. When Langfuse is configured (LANGFUSE_*), the run is also sent to it: the dataset, one trace per
// try, the grades as scores. A problem with Langfuse never loses the run: it is saved first, and the tool says what went wrong.
internal static class EvalCommand
{
    private const string DatasetDescription = "Questions put to a character of an example book (LivingPaper evaluation tool).";

    public static async Task<int> RunAsync(EvalOptions options, ITerminal terminal, Func<string, string?> environment, CancellationToken cancellationToken)
    {
        var dataDirectory = DataFolder.Resolve(environment("DATA_DIRECTORY"), Directory.GetCurrentDirectory());
        terminal.WriteLine($"Data folder: {dataDirectory}");

        var questions = await QuestionFile.ReadAsync(Locate(options.QuestionsPath, "evals/bovary/questions.json", "questions file"), cancellationToken).ConfigureAwait(false);
        var judgePrompt = await JudgePrompt.ReadAsync(Locate(options.JudgePromptPath, "evals/judge.json", "judge file"), cancellationToken).ConfigureAwait(false);
        var langfuse = options.LocalOnly ? null : ReadLangfuse();
        if (judgePrompt.NeedsExpectedAnswer && questions.Items.FirstOrDefault(item => item.ExpectedOutput.Length == 0) is { } unanswered)
        {
            throw new EvalException($"This judge compares each reply with an expected answer, and the question {unanswered.Id} has no expectedOutput.");
        }

        // With another prompts file, the prompts of the data folder are not used: they are left as they are.
        if (options.PromptsPath is null)
        {
            try
            {
                await PromptFileSynchronizer.EnsureAsync(dataDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (PromptFileException exception)
            {
                throw new EvalException(exception.Message);
            }
        }

        var promptsPath = options.PromptsPath is null ? null : Path.GetFullPath(options.PromptsPath);
        if (promptsPath is not null && !File.Exists(promptsPath))
        {
            throw new EvalException($"The prompts file {Path.GetFileName(promptsPath)} does not exist.");
        }

        var prompts = new FilePromptProvider(dataDirectory, promptsPath);
        string promptsHash;
        try
        {
            promptsHash = await prompts.ChatFingerprintAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PromptFileException exception)
        {
            throw new EvalException(exception.Message);
        }

        var keys = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dataDirectory, "keys")), builder => builder.SetApplicationName("LivingPaper"));
        var site = new JsonLlmSettingsRepository(dataDirectory, new DataProtectionSecretProtector(keys));

        using var factory = new OpenAiCompatibleChatModelFactory(
            NullLoggerFactory.Instance,
            new OpenAiCompatibleOptions { MaxOutputTokens = options.MaxOutputTokens },
            tracing: new LlmTracingOptions { CaptureContent = langfuse?.CaptureContent ?? false });
        using var catalog = new OpenAiCompatibleModelCatalog();
        using var embeddings = new OpenAiCompatibleEmbeddingModelFactory(NullLoggerFactory.Instance);
        var tester = new LlmConnectionTester(factory, embeddings, prompts);
        var pending = new PendingProviderStore(TimeProvider.System);
        using var settings = new LlmSettingsService(site, tester, catalog, pending);

        var (book, character) = await FindCharacterAsync(questions, options.BookDataPath, environment, cancellationToken).ConfigureAwait(false);

        var chooser = new ProviderChooser(terminal, site, new ProviderAdder(terminal, settings));
        var chatProvider = await chooser.ChooseAsync("chat", options.ChatProvider, cancellationToken).ConfigureAwait(false);
        var judgeProvider = await chooser.ChooseAsync("judge", options.JudgeProvider, cancellationToken).ConfigureAwait(false);

        // Everything the search needs is checked before the first call, so that no run is paid for and then found to be useless.
        LlmProvider? embeddingProvider = null;
        SqlitePassageIndex? index = null;
        if (options.Retrieval is { } search)
        {
            embeddingProvider = search.EmbeddingUrl is { } url
                ? new LlmProvider(Guid.NewGuid().ToString("N"), $"{search.Embedding} (no key)", url, string.Empty, search.Embedding)
                : await chooser.ChooseAsync("embedding", search.Embedding, cancellationToken).ConfigureAwait(false);
            index = new SqlitePassageIndex(Path.Combine(Path.GetFullPath(options.BookDataPath!), "rag.db"));
            await PassageCheck.RunAsync(index, book, embeddingProvider.Model, questions, cancellationToken).ConfigureAwait(false);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var runId = RunStore.NewId(startedAt, chatProvider.Model);
        var metadata = new Dictionary<string, string>
        {
            ["chat_provider"] = chatProvider.Name,
            ["chat_model"] = chatProvider.Model,
            ["judge_provider"] = judgeProvider.Name,
            ["judge_model"] = judgeProvider.Model,
            ["judge_prompt_version"] = judgePrompt.Version,
            ["repeats"] = options.Repeats.ToString(CultureInfo.InvariantCulture),
            ["character"] = character.Name,
            ["prompts_hash"] = promptsHash,
        };
        if (options.Label is not null)
        {
            metadata["label"] = options.Label;
        }

        var runRetrieval = options.Retrieval is { } used
            ? new RunRetrieval(embeddingProvider!.Model, used.Mode.ToString().ToLowerInvariant(), used.Passages)
            : null;
        if (runRetrieval is not null)
        {
            metadata["embedding_model"] = runRetrieval.EmbeddingModel;
            metadata["query_mode"] = runRetrieval.QueryMode;
            metadata["passages"] = runRetrieval.Passages.ToString(CultureInfo.InvariantCulture);
        }

        // Langfuse is prepared before the run: a wrong key is found now, not after the cost of the run.
        using var api = langfuse is null ? null : new LangfuseApi(langfuse);
        using var tracerProvider = langfuse is null
            ? null
            : Sdk.CreateTracerProviderBuilder()
                .ConfigureResource(resource => resource.AddService("livingpaper-evals", serviceVersion: typeof(EvalCommand).Assembly.GetName().Version?.ToString()))
                .AddLangfuseExporter(langfuse)
                .Build();
        IExperimentTracer tracer = NoExperimentTracer.Instance;
        if (api is not null)
        {
            tracer = await PrepareLangfuseAsync(api, questions, runId, metadata, terminal, cancellationToken).ConfigureAwait(false) ?? tracer;
        }

        var chatMeter = new ChatUsageMeter();
        var judgeMeter = new ChatUsageMeter();
        using var chatSettings = new LlmSettingsService(new ChatOverrideSettings(site, chatProvider.Id, embeddingProvider), tester, catalog, pending);
        var recorder = index is null
            ? new RetrievalRecorder(factory, total: chatMeter)
            : new RetrievalRecorder(factory, embeddings, index, chatMeter);
        var retriever = options.Retrieval is { } rag
            ? new PassageRetriever(recorder, chatSettings, recorder, recorder, prompts, new RagOptions { Mode = rag.Mode, Passages = rag.Passages })
            : null;
        var chat = new CharacterChatService(chatSettings, recorder, prompts, retriever);
        var runner = new EvalRunner(
            new ChatServiceReplier(chat, book, character, recorder),
            new ModelJudge(new MeteredChatModelFactory(factory, judgeMeter), judgeProvider, judgePrompt, character.Name, book.Title),
            tracer);

        var total = questions.Items.Count * options.Repeats;
        terminal.WriteLine();
        terminal.WriteLine($"{character.Name} ({book.Title}) answers with {chatProvider.Name} ({chatProvider.Model}).");
        terminal.WriteLine($"{judgeProvider.Name} ({judgeProvider.Model}) judges, prompt version {judgePrompt.Version}.");
        terminal.WriteLine($"Chat prompts {promptsHash}{(options.Label is null ? string.Empty : $", label \"{options.Label}\"")}.");
        terminal.WriteLine(runRetrieval is null
            ? "The character reads the whole story."
            : $"The character reads {runRetrieval.Passages} passages found with {runRetrieval.EmbeddingModel}, query mode {runRetrieval.QueryMode}.");
        terminal.WriteLine($"{questions.Items.Count} questions, {options.Repeats} times each: {total} answers to grade.");
        terminal.WriteLine();

        var done = 0;
        var results = await runner.RunAsync(
            questions,
            options.Repeats,
            result => terminal.WriteLine($"[{++done}/{total}] {result.QuestionId}: {(result.Score?.ToString(CultureInfo.InvariantCulture) ?? result.Failure)}"),
            cancellationToken).ConfigureAwait(false);

        var chatTokens = TokenUsage.From(chatMeter);
        var judgeTokens = TokenUsage.From(judgeMeter);
        var record = new RunRecord(
            RunRecord.CurrentSchemaVersion,
            runId,
            startedAt,
            DateTimeOffset.UtcNow,
            questions.Dataset,
            questions.BookId,
            character.Name,
            options.Repeats,
            new RunModel(chatProvider.Name, chatProvider.Model),
            new RunJudge(judgeProvider.Name, judgeProvider.Model, judgePrompt.Version, judgePrompt.ScoreName),
            new RunTokens(chatTokens, judgeTokens),
            results,
            options.Label,
            promptsHash,
            runRetrieval);

        // Saved before anything is sent: whatever happens next, the run is kept.
        var path = await RunStore.SaveAsync(RunsFolder(), record, cancellationToken).ConfigureAwait(false);

        var summary = ReportWriter.Summarize(results, [.. questions.Items.Select(item => item.Category).Distinct(StringComparer.Ordinal)]);
        ReportWriter.Write(Console.Out, summary, results, chatTokens, judgeTokens);
        terminal.WriteLine();
        terminal.WriteLine($"Run saved: {path}");

        if (api is not null && tracer is not NoExperimentTracer)
        {
            await SendScoresAsync(api, tracerProvider!, record, terminal, cancellationToken).ConfigureAwait(false);
        }

        return summary.Scored == 0 ? 1 : 0;
    }

    private static LangfuseOptions? ReadLangfuse()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        try
        {
            return LangfuseSettings.Read(configuration);
        }
        catch (InvalidOperationException exception)
        {
            // The message names the variable, never its value. --local-only skips Langfuse.
            throw new EvalException($"{exception.Message} (Use --local-only to run without Langfuse.)");
        }
    }

    // Creates the dataset and its items (found again by their ids, so never duplicated). Null, with a message, when Langfuse cannot be
    // used: the run then goes on without it.
    private static async Task<LangfuseExperimentTracer?> PrepareLangfuseAsync(
        LangfuseApi api,
        QuestionSet questions,
        string runId,
        IReadOnlyDictionary<string, string> metadata,
        ITerminal terminal,
        CancellationToken cancellationToken)
    {
        try
        {
            var datasetId = await api.EnsureDatasetAsync(questions.Dataset, DatasetDescription, cancellationToken).ConfigureAwait(false);
            foreach (var question in questions.Items)
            {
                await api.UpsertItemAsync(questions.Dataset, question, cancellationToken).ConfigureAwait(false);
            }

            terminal.WriteLine($"Langfuse: dataset {questions.Dataset} ready ({questions.Items.Count} items), experiment {runId}.");
            return new LangfuseExperimentTracer(new ExperimentContext(runId, questions.Dataset, datasetId, DatasetDescription, metadata));
        }
        catch (EvalException exception)
        {
            terminal.WriteLine($"Langfuse will not be used for this run: {exception.Message}");
            return null;
        }
    }

    // The traces are sent by the exporter in the background: they are flushed first, then the scores (which point at the traces).
    private static async Task SendScoresAsync(LangfuseApi api, TracerProvider tracerProvider, RunRecord record, ITerminal terminal, CancellationToken cancellationToken)
    {
        try
        {
            if (!tracerProvider.ForceFlush(30_000))
            {
                terminal.WriteLine("Langfuse: the traces were not all sent in time; the scores are sent anyway.");
            }

            var scores = LangfuseScores.From(record);
            await api.PostScoresAsync(scores, cancellationToken).ConfigureAwait(false);
            terminal.WriteLine($"Langfuse: {scores.Count} scores sent. Look in the Datasets / Experiments page, environment \"{LangfuseNames.Environment}\".");
        }
        catch (EvalException exception)
        {
            terminal.WriteLine($"Langfuse: the scores could not be sent ({exception.Message}). The run is saved in the file above.");
        }
    }

    // evals/runs/ of the repository (found by going up), or ./evals/runs when the tool is run from elsewhere.
    private static string RunsFolder() =>
        Path.Combine(RepoFiles.Find("evals") ?? Path.Combine(Directory.GetCurrentDirectory(), "evals"), "runs");

    private static string Locate(string? given, string defaultRelativePath, string what)
    {
        if (given is not null)
        {
            return given;
        }

        return RepoFiles.Find(defaultRelativePath)
            ?? throw new EvalException($"The {what} {defaultRelativePath} was not found: run the tool from the repository, or give its path.");
    }

    private static async Task<(Book Book, Character Character)> FindCharacterAsync(
        QuestionSet questions,
        string? bookData,
        Func<string, string?> environment,
        CancellationToken cancellationToken)
    {
        _ = BookId.TryParse(questions.BookId, out var bookId);
        Book book;
        if (bookData is not null)
        {
            book = await new JsonBookRepository(Path.Combine(Path.GetFullPath(bookData), "books")).GetAsync(bookId, cancellationToken).ConfigureAwait(false)
                ?? throw new EvalException($"The book {questions.BookId} was not found in the library of the folder given to --book-data.");
        }
        else
        {
            var examples = environment("EXAMPLES_DIRECTORY") is { Length: > 0 } configured ? Path.GetFullPath(configured) : RepoFiles.Find("examples");
            if (examples is null)
            {
                throw new EvalException("The examples folder was not found: run the tool from the repository, or set EXAMPLES_DIRECTORY.");
            }

            book = await new JsonExampleCatalog(examples).GetAsync(bookId, cancellationToken).ConfigureAwait(false)
                ?? throw new EvalException($"The example book {questions.BookId} was not found in the examples folder.");
        }

        var character = book.Characters.FirstOrDefault(candidate => string.Equals(candidate.Name, questions.Character, StringComparison.OrdinalIgnoreCase))
            ?? throw new EvalException($"{book.Title} has no character named {questions.Character}. Its characters: {string.Join(", ", book.Characters.Select(candidate => candidate.Name))}.");
        return (book, character);
    }
}
