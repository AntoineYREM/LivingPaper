namespace LivingPaper.Application;

// Why an analysis stopped. Stored as text in the book (ErrorCode) and translated by the interface; the detail is a short
// technical text in English that never holds a passage of the book or a secret.
public enum AnalysisFailure
{
    // No language model is chosen to analyze books (or its key cannot be read). Choose one in the settings and retry.
    NoModel,

    // The model cannot be reached, or kept failing after the retries. Detail: the HTTP status or the kind of failure.
    ModelUnreachable,

    // The provider refused for good: a wrong key, an unknown model, a bad request (HTTP 400, 401, 403, 404).
    ModelRefused,

    // The model's replies could not be read (not the JSON asked for, or empty) after several tries.
    InvalidReply,

    // The model's content filter refused a step that has no simpler version (the merge, a profile, the summary).
    ContentFiltered,

    // The merge found no character in the book.
    NoCharacters,

    // The text of the book is no longer there: it must be sent again.
    TextMissing,

    // The prompts file cannot be used. Detail: what to fix (never a prompt text).
    PromptFile,

    // The application stopped, or the analysis stopped moving, while it ran.
    Interrupted,

    // A bug or a disk error. Detail: the kind of error.
    Unexpected,
}
