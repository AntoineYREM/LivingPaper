namespace LivingPaper.Domain;

// One character found by the analysis. Texts are in English whatever the book's language (see AGENTS.md).
// Position: 1 = the most important character, in the order of the analysis.
public sealed record Character(int Position, string Name, string ShortDescription, string ChatProfile);
