using System.Globalization;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Infrastructure;
using LivingPaper.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LivingPaper.Evals;

// The composition of the `run` command: the same pieces as the site (settings, prompts, examples, the chat), assembled for one run.
// The site does not need to be running. The data folder is the site's (DATA_DIRECTORY, default ./data): the models and their encrypted
// keys are read from it, and the prompts too, so the character is played with the prompts you edited.
internal static class EvalCommand
{
    public static async Task<int> RunAsync(EvalOptions options, ITerminal terminal, Func<string, string?> environment, CancellationToken cancellationToken)
    {
        var dataDirectory = DataFolder.Resolve(environment("DATA_DIRECTORY"), Directory.GetCurrentDirectory());
        terminal.WriteLine($"Data folder: {dataDirectory}");

        var questions = await QuestionFile.ReadAsync(Locate(options.QuestionsPath, "evals/bovary/questions.json", "questions file"), cancellationToken).ConfigureAwait(false);
        var judgePrompt = await JudgePrompt.ReadAsync(Locate(options.JudgePromptPath, "evals/judge.json", "judge file"), cancellationToken).ConfigureAwait(false);

        try
        {
            await PromptFileSynchronizer.EnsureAsync(dataDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (PromptFileException exception)
        {
            throw new EvalException(exception.Message);
        }

        var prompts = new FilePromptProvider(dataDirectory);
        var keys = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dataDirectory, "keys")), builder => builder.SetApplicationName("LivingPaper"));
        var site = new JsonLlmSettingsRepository(dataDirectory, new DataProtectionSecretProtector(keys));

        using var factory = new OpenAiCompatibleChatModelFactory(NullLoggerFactory.Instance);
        using var catalog = new OpenAiCompatibleModelCatalog();
        var tester = new LlmConnectionTester(factory, prompts);
        var pending = new PendingProviderStore(TimeProvider.System);
        using var settings = new LlmSettingsService(site, tester, catalog, pending);

        var (book, character) = await FindCharacterAsync(questions, environment, cancellationToken).ConfigureAwait(false);

        var chooser = new ProviderChooser(terminal, site, new ProviderAdder(terminal, settings));
        var chatProvider = await chooser.ChooseAsync("chat", options.ChatProvider, cancellationToken).ConfigureAwait(false);
        var judgeProvider = await chooser.ChooseAsync("judge", options.JudgeProvider, cancellationToken).ConfigureAwait(false);

        var chatMeter = new ChatUsageMeter();
        var judgeMeter = new ChatUsageMeter();
        using var chatSettings = new LlmSettingsService(new ChatOverrideSettings(site, chatProvider.Id), tester, catalog, pending);
        var chat = new CharacterChatService(chatSettings, new MeteredChatModelFactory(factory, chatMeter), prompts);
        var runner = new EvalRunner(
            new ChatServiceReplier(chat, book, character),
            new ModelJudge(new MeteredChatModelFactory(factory, judgeMeter), judgeProvider, judgePrompt, character.Name, book.Title));

        var total = questions.Items.Count * options.Repeats;
        terminal.WriteLine();
        terminal.WriteLine($"{character.Name} ({book.Title}) answers with {chatProvider.Name} ({chatProvider.Model}).");
        terminal.WriteLine($"{judgeProvider.Name} ({judgeProvider.Model}) judges, prompt version {judgePrompt.Version}.");
        terminal.WriteLine($"{questions.Items.Count} questions, {options.Repeats} times each: {total} answers to grade.");
        terminal.WriteLine();

        var done = 0;
        var results = await runner.RunAsync(
            questions,
            options.Repeats,
            result => terminal.WriteLine($"[{++done}/{total}] {result.QuestionId}: {(result.Score?.ToString(CultureInfo.InvariantCulture) ?? result.Failure)}"),
            cancellationToken).ConfigureAwait(false);

        var summary = ReportWriter.Summarize(results, [.. questions.Items.Select(item => item.Category).Distinct(StringComparer.Ordinal)]);
        ReportWriter.Write(Console.Out, summary, results, TokenUsage.From(chatMeter), TokenUsage.From(judgeMeter));
        return summary.Scored == 0 ? 1 : 0;
    }

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
        Func<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var examples = environment("EXAMPLES_DIRECTORY") is { Length: > 0 } configured ? Path.GetFullPath(configured) : RepoFiles.Find("examples");
        if (examples is null)
        {
            throw new EvalException("The examples folder was not found: run the tool from the repository, or set EXAMPLES_DIRECTORY.");
        }

        _ = BookId.TryParse(questions.BookId, out var bookId);
        var book = await new JsonExampleCatalog(examples).GetAsync(bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new EvalException($"The example book {questions.BookId} was not found in the examples folder.");
        var character = book.Characters.FirstOrDefault(candidate => string.Equals(candidate.Name, questions.Character, StringComparison.OrdinalIgnoreCase))
            ?? throw new EvalException($"{book.Title} has no character named {questions.Character}. Its characters: {string.Join(", ", book.Characters.Select(candidate => candidate.Name))}.");
        return (book, character);
    }
}
