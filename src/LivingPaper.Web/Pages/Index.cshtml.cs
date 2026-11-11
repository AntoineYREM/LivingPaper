using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// "My library": the books, their status, and what can be done with each (open, retry, delete). Sending a book is the page
// Add; the page of a book is Books/Index. A change that works goes back to the page (POST, redirect, GET), so reloading never
// repeats it.
public class IndexModel(BookService books, LlmSettingsService settings, BookPresenter presenter, ITextLocalizer texts) : PageModel
{
    public IReadOnlyList<BookPresenter.Row> Rows { get; private set; } = [];

    public IReadOnlyList<UnreadableBook> Unreadable { get; private set; } = [];

    // False when no model is chosen to analyze books (or it cannot be told): the page warns that books will wait.
    public bool AnalysisOff { get; private set; }

    public string? Notice { get; private set; }

    // The sentence that names the stored book files that could not be read.
    public string UnreadableMessage => texts.Plural("books.unreadable", Unreadable.Count, new { names = string.Join(", ", Unreadable.Select(book => book.StorageName)) });

    public async Task OnGetAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        Notice = PageNotice.Take(TempData, texts);
    }

    // Only the list of the books, for the page to refresh it while an analysis runs (see wwwroot/js/books.js).
    public async Task<IActionResult> OnGetListAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        return Partial("_BookList", this);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? id)
    {
        var token = HttpContext.RequestAborted;
        var book = await FindAsync(id, token);
        if (book is null || !await books.DeleteAsync(book.Id, token))
        {
            return NotFound();
        }

        PageNotice.Set(TempData, PageNotice.Deleted, book.Title);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(string? id)
    {
        var token = HttpContext.RequestAborted;
        var book = await FindAsync(id, token);
        if (book is null || !await books.RetryAsync(book.Id, token))
        {
            return NotFound();
        }

        PageNotice.Set(TempData, PageNotice.Retried, book.Title);
        return RedirectToPage();
    }

    private async Task<Book?> FindAsync(string? id, CancellationToken cancellationToken) =>
        BookId.TryParse(id, out var bookId)
            ? (await books.ListAsync(cancellationToken)).Books.FirstOrDefault(candidate => candidate.Id == bookId)
            : null;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var listing = await books.ListAsync(cancellationToken);
        Rows = [.. listing.Books.Select(presenter.Describe)];
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
}
