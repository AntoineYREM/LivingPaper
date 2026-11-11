namespace LivingPaper.Domain;

// The codes of FieldError for a character sheet. The interface turns each code into a sentence, in the user's language.
public static class CharacterErrors
{
    public const string Required = "required";
    public const string TooLong = "too_long";

    // A control character (a name or a description may not hold a line break; the profile may, but nothing else).
    public const string InvalidCharacters = "invalid_characters";

    // Another character of the book already has this name.
    public const string Taken = "taken";
}
