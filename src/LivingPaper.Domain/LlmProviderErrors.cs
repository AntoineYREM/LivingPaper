namespace LivingPaper.Domain;

// The codes of FieldError for a language model. The interface turns each code into a sentence, in the user's language.
public static class LlmProviderErrors
{
    public const string Required = "required";
    public const string TooLong = "too_long";
    public const string InvalidCharacters = "invalid_characters";

    // The name is already used by another language model.
    public const string Taken = "taken";

    // The base URL is not an absolute URL.
    public const string Invalid = "invalid";

    // The base URL is not http or https.
    public const string Scheme = "scheme";

    // The base URL contains a user name or a password. It is stored in clear: the key goes in the key field.
    public const string Credentials = "credentials";

    public const string QueryOrFragment = "query_or_fragment";

    // Not about one field (the field is empty): the maximum number of language models is reached.
    public const string TooMany = "too_many";

    // The model is not among the models the provider offered.
    public const string NotInList = "not_in_list";
}
