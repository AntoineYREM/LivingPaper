using LivingPaper.Application;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// Sending a book (PAPER-7). A book that is accepted takes the user to its page, where the analysis shows its progress and then
// the characters. The file and the text of a book never go in a log.
// The request may be a little larger than the largest book (the form carries other fields too); anything bigger is
// refused by the server before it is read, so a huge upload cannot fill the disk.
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public class AddModel(BookService books, LlmSettingsService settings, ITextLocalizer texts) : PageModel
{
    private const long MaxRequestBytes = BookService.MaxBookBytes + (5L * 1024 * 1024);

    // What the user typed in the form that was refused, to show it again. A file cannot be shown again: it is chosen again.
    public sealed record TypedBook(string? Title, bool Replace);

    // False when no model is chosen to analyze books (or it cannot be told): the page warns that the book will wait.
    public bool AnalysisOff { get; private set; }

    // The sentence for the file field, when the book was refused.
    public string? FileError { get; private set; }

    public bool FormRefused { get; private set; }

    public TypedBook Typed { get; private set; } = new(null, false);

    public static int MaxMegabytes => (int)(BookService.MaxBookBytes / (1024 * 1024));

    public async Task OnGetAsync() => await LoadAsync(HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostAsync(IFormFile? file, string? title, bool replace)
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
            PageNotice.Set(TempData, result.Replaced ? PageNotice.Replaced : PageNotice.Added, result.Added!.Title);
            return RedirectToPage("/Books/Index", new { id = result.Added.Id.ToString() });
        }

        await LoadAsync(token);
        FormRefused = true;
        Typed = new TypedBook(title, replace);
        FileError = Describe(result.Rejection!.Value);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
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
