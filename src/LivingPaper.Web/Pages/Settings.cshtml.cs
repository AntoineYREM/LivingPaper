using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// The settings page (PAPER-5): the language models, adding one (after a successful connection test), deleting one, and
// which one analyzes books and which one chats. A key goes from the form to the server and never back: this model has no
// property that can carry one, and a form that is shown again is shown without it.
// A change that works goes back to the page (POST, redirect, GET), so reloading never repeats it; the short message of
// what was done travels in TempData.
public class SettingsModel(LlmSettingsService settings, ITextLocalizer texts) : PageModel
{
    private const string NoticeKind = "Notice";
    private const string NoticeName = "NoticeName";

    // What the user typed in the form that was refused, to show it again. The key is not among them.
    public sealed record TypedModel(string? Name, string? BaseUrl, string? Model);

    public LlmSettingsView View { get; private set; } = new([], null, null, false, false);

    // Set when the settings file cannot be used: the page then shows this and nothing else.
    public string? FileProblem { get; private set; }

    public string? Notice { get; private set; }

    // A sentence for the field that is wrong, by form field ("name", "baseUrl", "apiKey", "model").
    public IReadOnlyDictionary<string, string> FieldErrors { get; private set; } = new Dictionary<string, string>();

    // Not about one field (too many models) or the failed connection test.
    public string? FormError { get; private set; }

    public bool FormRefused { get; private set; }

    public TypedModel Typed { get; private set; } = new(null, null, null);

    public Task<IActionResult> OnGetAsync() => GuardAsync(async token =>
    {
        View = await settings.GetAsync(token);
        Notice = TakeNotice();
        return Page();
    });

    public Task<IActionResult> OnPostAddAsync(string? name, string? baseUrl, string? apiKey, string? model) => GuardAsync(async token =>
    {
        var result = await settings.AddAsync(new LlmProviderDraft(name, baseUrl, apiKey, model), token);
        if (result.Succeeded)
        {
            return RedirectWithNotice("added", result.Added!.Name);
        }

        View = await settings.GetAsync(token);
        FormRefused = true;
        Typed = new TypedModel(name, baseUrl, model);
        var fieldErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var error in result.Errors)
        {
            if (error.Field.Length == 0)
            {
                FormError = texts.Format("settings.tooMany", new { max = LlmProviderLimits.MaxProviders });
            }
            else
            {
                fieldErrors[error.Field] = DescribeFieldError(error);
            }
        }

        FieldErrors = fieldErrors;
        if (result.Connection is { Success: false } connection)
        {
            FormError = DescribeFailure(connection);
        }

        return Page();
    });

    public Task<IActionResult> OnPostDeleteAsync(string? id) => GuardAsync(async token =>
    {
        var name = (await settings.GetAsync(token)).Providers.FirstOrDefault(provider => provider.Id == id)?.Name;
        if (name is null || !await settings.DeleteAsync(id!, token))
        {
            return NotFound();
        }

        return RedirectWithNotice("deleted", name);
    });

    // An empty choice is "none": that use is turned off. A choice that is no model of the list changes nothing.
    // The values are read from the form itself: model binding would turn an empty text into null, which here means "absent".
    public Task<IActionResult> OnPostSelectAsync() => GuardAsync(async token =>
    {
        string? analysis = Request.Form.TryGetValue("analysis", out var analysisValues) ? analysisValues.ToString() : null;
        string? chat = Request.Form.TryGetValue("chat", out var chatValues) ? chatValues.ToString() : null;
        var ids = (await settings.GetAsync(token)).Providers.Select(provider => provider.Id).ToHashSet(StringComparer.Ordinal);
        if (analysis is null || chat is null
            || (analysis.Length > 0 && !ids.Contains(analysis))
            || (chat.Length > 0 && !ids.Contains(chat)))
        {
            return BadRequest();
        }

        await settings.SelectForAnalysisAsync(analysis.Length > 0 ? analysis : null, token);
        await settings.SelectForChatAsync(chat.Length > 0 ? chat : null, token);
        return RedirectWithNotice("saved", string.Empty);
    });

    // Runs a handler; an unusable settings file becomes a page that says so (the file is never touched).
    private async Task<IActionResult> GuardAsync(Func<CancellationToken, Task<IActionResult>> handler)
    {
        try
        {
            return await handler(HttpContext.RequestAborted);
        }
        catch (SettingsFileException exception)
        {
            FileProblem = texts.Format("settings.file.broken", new { file = exception.FileName, problem = exception.Problem });
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Page();
        }
    }

    private RedirectToPageResult RedirectWithNotice(string kind, string name)
    {
        TempData[NoticeKind] = kind;
        TempData[NoticeName] = name;
        return RedirectToPage();
    }

    private string? TakeNotice()
    {
        var kind = TempData[NoticeKind] as string;
        var name = TempData[NoticeName] as string ?? string.Empty;
        return kind switch
        {
            "added" => texts.Format("settings.notice.added", new { name }),
            "deleted" => texts.Format("settings.notice.deleted", new { name }),
            "saved" => texts["settings.notice.saved"],
            _ => null,
        };
    }

    private string DescribeFieldError(FieldError error)
    {
        var message = error.Code switch
        {
            LlmProviderErrors.Required => texts["settings.error.required"],
            LlmProviderErrors.TooLong => texts.Format("settings.error.too_long", new { max = MaxLength(error.Field) }),
            LlmProviderErrors.InvalidCharacters => texts["settings.error.invalid_characters"],
            LlmProviderErrors.Taken => texts["settings.error.taken"],
            LlmProviderErrors.Invalid => texts["settings.error.invalid"],
            LlmProviderErrors.Scheme => texts["settings.error.scheme"],
            LlmProviderErrors.Credentials => texts["settings.error.credentials"],
            LlmProviderErrors.QueryOrFragment => texts["settings.error.query_or_fragment"],
            _ => texts["settings.error.invalid"],
        };
        return texts.Format("settings.error.prefix", new { message });
    }

    private static int MaxLength(string field) => field switch
    {
        "name" => LlmProviderLimits.Name,
        "baseUrl" => LlmProviderLimits.BaseUrl,
        "apiKey" => LlmProviderLimits.ApiKey,
        _ => LlmProviderLimits.Model,
    };

    private string DescribeFailure(ConnectionTestResult result) => result.Failure switch
    {
        ConnectionFailure.Unreachable => texts["settings.failure.unreachable"],
        ConnectionFailure.KeyRefused => texts.Format("settings.failure.keyRefused", new { status = result.HttpStatus }),
        ConnectionFailure.ModelUnknown => texts.Format("settings.failure.modelUnknown", new { status = result.HttpStatus }),
        ConnectionFailure.ProviderError => texts.Format("settings.failure.providerError", new { status = result.HttpStatus }),
        ConnectionFailure.PromptFile => texts.Format("settings.failure.promptFile", new { detail = result.Detail }),
        _ => texts["settings.failure.invalidResponse"],
    };
}
