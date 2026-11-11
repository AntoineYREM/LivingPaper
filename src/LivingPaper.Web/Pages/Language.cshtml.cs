using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// Remembers the visitor's language in a cookie and sends them back where they were. The form carries the antiforgery token
// (Razor Pages checks it), so another site cannot switch a visitor's language.
public class LanguageModel : PageModel
{
    private static readonly TimeSpan Memory = TimeSpan.FromDays(365);

    // Opening the address by hand does nothing: go home.
    public IActionResult OnGet() => RedirectToPage("/Index");

    public IActionResult OnPost(string? code, string? returnUrl)
    {
        if (!SupportedLanguages.TryFind(code, out var language))
        {
            return BadRequest();
        }

        Response.Cookies.Append(LanguageCookie.Name, language.Code, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.Add(Memory),
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            IsEssential = true,
        });

        // Only an address of this site: otherwise anyone could send a visitor anywhere through this form.
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
