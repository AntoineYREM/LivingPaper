using System.Globalization;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// The home page: the books, sending a book, deleting one and retrying its analysis (PAPER-7, PAPER-8). The page of the
// library comes with PAPER-9.
// A change that works goes back to the page (POST, redirect, GET), so reloading never repeats it; the short message of
// what was done travels in TempData. The file and the text of a book never go in a log.
// The request may be a little larger than the largest book (the form carries other fields too); anything bigger is
// refused by the server before it is read, so a huge upload cannot fill the disk.
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public class IndexModel(BookService books, LlmSettingsService settings, ITextLocalizer texts) : PageModel
{
    private const long MaxRequestBytes = BookService.MaxBookBytes + (5L * 1024 * 1024);
    private const string NoticeKind = "Notice";
    private const string NoticeTitle = "NoticeTitle";

    // What the user typed in the form that was refused, to show it again. A file cannot be shown again: it is chosen again.
    public sealed record TypedBook(string? Title, bool Replace);

    // A book as the list shows it, already in words: the view only writes them down.
    // Active: the analysis is waiting or running here, so the list refreshes by itself.
    public sealed record BookRow(
        Book Book,
        string Status,
        string? Progress,
        string? Problem,
        string? Warning,
        bool CanRetry,
        bool Active,
        IReadOnlyList<(string Label, string Value)> Facts);

    public IReadOnlyList<BookRow> Rows { get; private set; } = [];

    public IReadOnlyList<UnreadableBook> Unreadable { get; private set; } = [];

    // False when no model is chosen to analyze books (or it cannot be told): the page warns that books will wait.
    public bool AnalysisOff { get; private set; }

    public string? Notice { get; private set; }

    // The sentence for the file field, when the book was refused.
    public string? FileError { get; private set; }

    public bool FormRefused { get; private set; }

    public TypedBook Typed { get; private set; } = new(null, false);

    public static int MaxMegabytes => (int)(BookService.MaxBookBytes / (1024 * 1024));

    // The sentence that names the stored book files that could not be read.
    public string UnreadableMessage => texts.Plural("books.unreadable", Unreadable.Count, new { names = string.Join(", ", Unreadable.Select(book => book.StorageName)) });

    public async Task OnGetAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        Notice = TakeNotice();
    }

    // Only the list of the books, for the page to refresh it while an analysis runs (see wwwroot/js/books.js).
    public async Task<IActionResult> OnGetListAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        return Partial("_BookList", this);
    }

    public async Task<IActionResult> OnPostAddAsync(IFormFile? file, string? title, bool replace)
    {
        var token = HttpContext.RequestAborted;
        AddBookResult result;
        if (file is null)
        {
            result = new AddBookResult(null, BookRejection.Empty);
        }
        else
        {
            await using var content = file.OpenReadStream();
            result = await books.AddAsync(content, file.FileName, title, replace, token);
        }

        if (result.Succeeded)
        {
            return RedirectWithNotice(result.Replaced ? "replaced" : "added", result.Added!.Title);
        }

        await LoadAsync(token);
        FormRefused = true;
        Typed = new TypedBook(title, replace);
        FileError = Describe(result.Rejection!.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? id)
    {
        var token = HttpContext.RequestAborted;
        var book = await FindAsync(id, token);
        if (book is null || !await books.DeleteAsync(book.Id, token))
        {
            return NotFound();
        }

        return RedirectWithNotice("deleted", book.Title);
    }

    public async Task<IActionResult> OnPostRetryAsync(string? id)
    {
        var token = HttpContext.RequestAborted;
        var book = await FindAsync(id, token);
        if (book is null || !await books.RetryAsync(book.Id, token))
        {
            return NotFound();
        }

        return RedirectWithNotice("retried", book.Title);
    }

    private async Task<Book?> FindAsync(string? id, CancellationToken cancellationToken) =>
        BookId.TryParse(id, out var bookId)
            ? (await books.ListAsync(cancellationToken)).Books.FirstOrDefault(candidate => candidate.Id == bookId)
            : null;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var listing = await books.ListAsync(cancellationToken);
        Rows = [.. listing.Books.Select(Describe)];
        Unreadable = listing.Unreadable;
        try
        {
            AnalysisOff = !(await settings.GetAsync(cancellationToken)).IsAnalysisEnabled;
        }
        catch (SettingsFileException)
        {
            // The settings page says what is wrong with the file; this page does not repeat it.
            AnalysisOff = true;
        }
    }

    private RedirectToPageResult RedirectWithNotice(string kind, string title)
    {
        TempData[NoticeKind] = kind;
        TempData[NoticeTitle] = title;
        return RedirectToPage();
    }

    private string? TakeNotice()
    {
        var kind = TempData[NoticeKind] as string;
        var title = TempData[NoticeTitle] as string ?? string.Empty;
        return kind switch
        {
            "added" => texts.Format("books.notice.added", new { title }),
            "replaced" => texts.Format("books.notice.replaced", new { title }),
            "deleted" => texts.Format("books.notice.deleted", new { title }),
            "retried" => texts.Format("books.notice.retried", new { title }),
            _ => null,
        };
    }

    // ---- A book, in words ----

    private BookRow Describe(Book book)
    {
        var interrupted = books.IsInterrupted(book);
        return book.Status switch
        {
            BookStatus.Done => new BookRow(book, texts["books.status.done"], null, null, SkippedWarning(book), CanRetry: false, Active: false, DoneFacts(book)),
            BookStatus.Failed => new BookRow(book, texts["books.status.failed"], null, Problem(book.ErrorCode, book.ErrorDetail), null, CanRetry: book.ErrorCode != nameof(AnalysisFailure.TextMissing), Active: false, []),
            _ when interrupted => new BookRow(book, texts["books.status.interrupted"], null, Problem(nameof(AnalysisFailure.Interrupted), null), null, CanRetry: true, Active: false, []),
            BookStatus.Running => new BookRow(book, texts["books.status.running"], Progress(book), null, null, CanRetry: false, Active: true, []),
            _ => new BookRow(book, texts["books.status.waiting"], null, null, null, CanRetry: false, Active: true, []),
        };
    }

    private string Progress(Book book)
    {
        if (book.ProfilesTotal > 0)
        {
            return texts.Format("books.progress.profiles", new { done = book.ProfilesDone, total = book.ProfilesTotal });
        }

        return book.PartsTotal > 0
            ? texts.Format("books.progress.parts", new { done = book.PartsDone, total = book.PartsTotal })
            : texts["books.progress.starting"];
    }

    private string? SkippedWarning(Book book) =>
        book.PartsSkipped > 0 ? texts.Plural("books.skipped", book.PartsSkipped) : null;

    private List<(string Label, string Value)> DoneFacts(Book book)
    {
        var facts = new List<(string, string)>
        {
            (texts["books.characters"], book.Characters.Count.ToString(CultureInfo.GetCultureInfo(texts.Language))),
        };
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
            var culture = CultureInfo.GetCultureInfo(texts.Language);
            facts.Add((texts["books.tokens"], texts.Format("books.tokensValue", new
            {
                input = book.InputTokens.ToString("N0", culture),
                cached = book.CachedInputTokens.ToString("N0", culture),
                output = book.OutputTokens.ToString("N0", culture),
            })));
        }

        return facts;
    }

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
        return string.IsNullOrWhiteSpace(detail) ? message : texts.Format("books.failure.withDetail", new { message, detail });
    }

    private string Describe(BookRejection rejection)
    {
        var message = rejection switch
        {
            BookRejection.Empty => texts["books.error.empty"],
            BookRejection.TooLarge => texts.Format("books.error.tooLarge", new { max = MaxMegabytes }),
            BookRejection.Unsupported => texts["books.error.unsupported"],
            BookRejection.ScannedPdf => texts["books.error.scannedPdf"],
            BookRejection.DamagedEpub => texts["books.error.damagedEpub"],
            BookRejection.DamagedPdf => texts["books.error.damagedPdf"],
            BookRejection.NoText => texts["books.error.noText"],
            BookRejection.Duplicate => texts["books.error.duplicate"],
            _ => texts["books.error.unsupported"],
        };
        return texts.Format("books.error.prefix", new { message });
    }
}
