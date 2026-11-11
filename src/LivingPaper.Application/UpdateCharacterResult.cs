using LivingPaper.Domain;

namespace LivingPaper.Application;

public enum CharacterUpdateOutcome
{
    Saved,

    // The texts are not acceptable: see the errors. Nothing was changed.
    Invalid,

    // No such book, or no such character in it.
    NotFound,

    // The analysis of the book is not done: its sheets do not exist yet, or are about to be written.
    NotEditable,
}

// The outcome of changing a character sheet: the sheet as saved, or the errors by form field (never the text typed).
public sealed record UpdateCharacterResult(CharacterUpdateOutcome Outcome, IReadOnlyList<FieldError> Errors, Character? Character);
