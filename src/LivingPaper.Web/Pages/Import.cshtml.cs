using LivingPaper.Application;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// Importing an analyzed book from the file of an export (PAPER-12). The file is untrusted: the service reads at most 1 MB and
// validates it before keeping anything. A book of the library with the same identifier or title is replaced only when the user
// ticks "Replace" and sends the file again (as for sending a book). A book that is accepted takes the user to its page.
// The request may be a little larger than the largest file; anything bigger is refused by the server before it is read.
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public class ImportModel(BookService books, ITextLocalizer texts) : PageModel
{
    private const long MaxRequestBytes = BookService.MaxImportBytes + (1024 * 1024);

    // What the user ticked in the form that was refused, to show it again. A file cannot be shown again: it is chosen again.
    public bool Replace { get; private set; }

    // The sentence for the file field, when the file was refused.
    public string? FileError { get; private set; }

    // Set when the file was refused because it would replace books: the page explains and asks to tick "Replace".
    public string? ConflictNote { get; private set; }

    public static int MaxKilobytes => (int)(BookService.MaxImportBytes / 1024);

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? file, bool replace)
    {
        ImportBookResult result;
        if (file is null)
        {
            result = new ImportBookResult(null, ImportRejection.Empty, null, [], Replaced: false);
        }
        else
        {
            await using var content = file.OpenReadStream();
            result = await books.ImportAsync(content, replace, HttpContext.RequestAborted);
        }

        if (result.Imported is not null)
        {
            PageNotice.Set(TempData, result.Replaced ? PageNotice.ImportedReplaced : PageNotice.Imported, result.Imported.Title);
            return RedirectToPage("/Books/Index", new { id = result.Imported.Id.ToString() });
        }

        Replace = replace;
        var rejection = result.Rejection ?? ImportRejection.Invalid;
        if (rejection == ImportRejection.Conflict)
        {
            ConflictNote = texts.Format("import.conflict", new { titles = string.Join(", ", result.ConflictTitles.Select(title => "“" + title + "”")) });
        }

        FileError = Describe(rejection, result.Detail);
        return Page();
    }

    private string Describe(ImportRejection rejection, string? detail)
    {
        var message = rejection switch
        {
            ImportRejection.Empty => texts["import.error.empty"],
            ImportRejection.TooLarge => texts.Format("import.error.tooLarge", new { max = MaxKilobytes }),
            ImportRejection.NotJson => texts["import.error.notJson"],
            ImportRejection.UnknownVersion => texts["import.error.unknownVersion"],
            ImportRejection.NewerVersion => texts["import.error.newerVersion"],
            ImportRejection.NotAnalyzed => texts["import.error.notAnalyzed"],
            ImportRejection.Conflict => texts["import.error.conflict"],
            _ => texts.Format("import.error.invalid", new { detail = detail ?? string.Empty }),
        };
        return texts.Format("import.error.prefix", new { message });
    }
}
