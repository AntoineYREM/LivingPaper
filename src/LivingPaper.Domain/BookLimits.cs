namespace LivingPaper.Domain;

// Limits on a book as a whole. The limits on a character's texts are in CharacterLimits.
public static class BookLimits
{
    public const int Title = 300;
    public const int FileName = 255;
    public const int ErrorDetail = 500;
    public const int ProviderName = 200;
    public const int ModelName = 200;

    // The merge prompt asks for 10 characters by default ({maxCharacters}); this is only a safety net.
    public const int MaxCharacters = 50;
}
