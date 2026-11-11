using System.Globalization;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;

namespace LivingPaper.Web;

// Puts a book into words for the pages that show it (the library and the page of a book): its status, its progress, what
// went wrong and what the analysis cost. The views only write these words down. Every text comes from the translation files.
public sealed class BookPresenter(BookService books, ITextLocalizer texts)
{
    // A book as a page shows it. Active: the analysis is waiting or running here, so the page refreshes by itself.
    // ProgressValue and ProgressMax: for the progress bar, null when nothing is counted yet.
    public sealed record Row(
        Book Book,
        string Status,
        string StatusClass,
        string? Progress,
        int? ProgressValue,
        int? ProgressMax,
        string? Problem,
        string? Warning,
        bool CanRetry,
        bool Active,
        string Meta);

    public Row Describe(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        var interrupted = books.IsInterrupted(book);
        return book.Status switch
        {
            BookStatus.Done => Make(book, "books.status.done", "status--ready", Warning: SkippedWarning(book), Meta: DoneMeta(book)),
            BookStatus.Failed => Make(
                book,
                "books.status.failed",
                "status--failed",
                Problem: Problem(book.ErrorCode, book.ErrorDetail),
                CanRetry: book.ErrorCode != nameof(AnalysisFailure.TextMissing)),
            _ when interrupted => Make(book, "books.status.interrupted", "status--failed", Problem: Problem(nameof(AnalysisFailure.Interrupted), null), CanRetry: true),
            BookStatus.Running => Running(book),
            _ => Make(book, "books.status.waiting", string.Empty, Active: true),
        };
    }

    // What the analysis cost, for the page of a finished book.
    public IReadOnlyList<(string Label, string Value)> Facts(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        var culture = CultureInfo.GetCultureInfo(texts.Language);
        var facts = new List<(string, string)>();
        if (book.FileName.Length > 0)
        {
            facts.Add((texts["books.file"], book.FileName));
        }

        if (book.Model is not null)
        {
            facts.Add((texts["books.model"], texts.Format("books.modelValue", new { model = book.Model, provider = book.ProviderName ?? string.Empty })));
        }

        if (book.StartedAt is { } started && book.FinishedAt is { } finished && finished >= started)
        {
            facts.Add((texts["books.duration"], Duration(finished - started)));
        }

        if (book.InputTokens + book.OutputTokens > 0)
        {
            facts.Add((texts["books.tokens"], texts.Format("books.tokensValue", new
            {
                input = book.InputTokens.ToString("N0", culture),
                cached = book.CachedInputTokens.ToString("N0", culture),
                output = book.OutputTokens.ToString("N0", culture),
            })));
        }

        return facts;
    }

    private Row Running(Book book)
    {
        if (book.ProfilesTotal > 0)
        {
            return Make(book, "books.status.running", string.Empty, texts.Format("books.progress.profiles", new { done = book.ProfilesDone, total = book.ProfilesTotal }), book.ProfilesDone, book.ProfilesTotal, Active: true);
        }

        return book.PartsTotal > 0
            ? Make(book, "books.status.running", string.Empty, texts.Format("books.progress.parts", new { done = book.PartsDone, total = book.PartsTotal }), book.PartsDone, book.PartsTotal, Active: true)
            : Make(book, "books.status.running", string.Empty, texts["books.progress.starting"], Active: true);
    }

    // The key is written where it is used (a literal), so the tests that look for the keys in use can find it.
    private Row Make(
        Book book,
        string statusKey,
        string statusClass,
        string? Progress = null,
        int? ProgressValue = null,
        int? ProgressMax = null,
        string? Problem = null,
        string? Warning = null,
        bool CanRetry = false,
        bool Active = false,
        string? Meta = null) =>
        new(book, StatusText(statusKey), statusClass, Progress, ProgressValue, ProgressMax, Problem, Warning, CanRetry, Active, Meta ?? FileOnly(book));

    private string StatusText(string key) => key switch
    {
        "books.status.done" => texts["books.status.done"],
        "books.status.failed" => texts["books.status.failed"],
        "books.status.interrupted" => texts["books.status.interrupted"],
        "books.status.running" => texts["books.status.running"],
        _ => texts["books.status.waiting"],
    };

    private static string FileOnly(Book book) => book.FileName;

    private string DoneMeta(Book book)
    {
        var parts = new List<string>();
        if (book.FileName.Length > 0)
        {
            parts.Add(book.FileName);
        }

        parts.Add(texts.Plural("books.characterCount", book.Characters.Count));
        if (book.Model is not null)
        {
            parts.Add(texts.Format("books.analyzedWith", new { model = book.Model }));
        }

        return string.Join(" · ", parts);
    }

    private string? SkippedWarning(Book book) =>
        book.PartsSkipped > 0 ? texts.Plural("books.skipped", book.PartsSkipped) : null;

    private string Duration(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? texts.Format("books.duration.minutes", new { minutes = (int)span.TotalMinutes, seconds = span.Seconds })
            : texts.Format("books.duration.seconds", new { seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds)) });

    private string Problem(string? code, string? detail)
    {
        var message = code switch
        {
            nameof(AnalysisFailure.NoModel) => texts["books.failure.noModel"],
            nameof(AnalysisFailure.ModelUnreachable) => texts["books.failure.modelUnreachable"],
            nameof(AnalysisFailure.ModelRefused) => texts["books.failure.modelRefused"],
            nameof(AnalysisFailure.InvalidReply) => texts["books.failure.invalidReply"],
            nameof(AnalysisFailure.ContentFiltered) => texts["books.failure.contentFiltered"],
            nameof(AnalysisFailure.NoCharacters) => texts["books.failure.noCharacters"],
            nameof(AnalysisFailure.TextMissing) => texts["books.failure.textMissing"],
            nameof(AnalysisFailure.PromptFile) => texts["books.failure.promptFile"],
            nameof(AnalysisFailure.Interrupted) => texts["books.failure.interrupted"],
            _ => texts["books.failure.unexpected"],
        };
        // The message already says everything for an interruption; its detail is a technical note in English for the file.
        return string.IsNullOrWhiteSpace(detail) || code == nameof(AnalysisFailure.Interrupted)
            ? message
            : texts.Format("books.failure.withDetail", new { message, detail });
    }
}
