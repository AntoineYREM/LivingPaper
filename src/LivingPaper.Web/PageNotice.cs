using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LivingPaper.Web;

// The short message of what was just done ("Le Malade imaginaire was deleted."), carried from a form that worked to the page
// it goes back to (POST, redirect, GET), so that reloading never repeats the change. It travels in TempData and is read once.
public static class PageNotice
{
    private const string KindKey = "Notice";
    private const string TitleKey = "NoticeTitle";

    public const string Added = "added";
    public const string Replaced = "replaced";
    public const string Deleted = "deleted";
    public const string Retried = "retried";
    public const string CharacterSaved = "characterSaved";

    public static void Set(ITempDataDictionary tempData, string kind, string title)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[KindKey] = kind;
        tempData[TitleKey] = title;
    }

    // The sentence for the notice waiting for this visitor, or null.
    public static string? Take(ITempDataDictionary tempData, ITextLocalizer texts)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        ArgumentNullException.ThrowIfNull(texts);
        var kind = tempData[KindKey] as string;
        var title = tempData[TitleKey] as string ?? string.Empty;
        return kind switch
        {
            Added => texts.Format("books.notice.added", new { title }),
            Replaced => texts.Format("books.notice.replaced", new { title }),
            Deleted => texts.Format("books.notice.deleted", new { title }),
            Retried => texts.Format("books.notice.retried", new { title }),
            CharacterSaved => texts.Format("books.notice.characterSaved", new { title }),
            _ => null,
        };
    }
}
