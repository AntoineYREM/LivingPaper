using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// The home page: the books, sending a book and deleting one (PAPER-7). The page of the library comes with PAPER-9.
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

    public IReadOnlyList<Book> Books { get; private set; } = [];

    public IReadOnlyList<UnreadableBook> Unreadable { get; private set; } = [];

    // False when no model is chosen to analyze books (or it cannot be told): the page warns that books will wait.
    public bool AnalysisOff { get; private set; }

    public string? Notice { get; private set; }

    // The sentence for the file field, when the book was refused.
    public string? FileError { get; private set; }

    public bool FormRefused { get; private set; }

    public TypedBook Typed { get; private set; } = new(null, false);

    // The sentence that names the stored book files that could not be read.
    public string UnreadableMessage => texts.Plural("books.unreadable", Unreadable.Count, new { names = string.Join(", ", Unreadable.Select(book => book.StorageName)) });

    public string StatusText(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.Status switch
        {
            BookStatus.Running => texts["books.status.running"],
            BookStatus.Failed => texts["books.status.failed"],
            BookStatus.Done => texts["books.status.done"],
            _ => texts["books.status.waiting"],
        };
    }

    public static int MaxMegabytes => (int)(BookService.MaxBookBytes / (1024 * 1024));

    public async Task OnGetAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        Notice = TakeNotice();
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
        if (!BookId.TryParse(id, out var bookId))
        {
            return NotFound();
        }

        var book = (await books.ListAsync(token)).Books.FirstOrDefault(candidate => candidate.Id == bookId);
        if (book is null || !await books.DeleteAsync(bookId, token))
        {
            return NotFound();
        }

        return RedirectWithNotice("deleted", book.Title);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var listing = await books.ListAsync(cancellationToken);
        Books = listing.Books;
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
            _ => null,
        };
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
