namespace LivingPaper.Evals;

// The keyboard and the screen, behind an interface so the menus can be tested.
internal interface ITerminal
{
    // False when the input is redirected (a script, a CI job): nobody can answer a question.
    bool IsInteractive { get; }

    void WriteLine(string text = "");

    // Null when the input ends.
    string? ReadLine(string prompt);

    // Like ReadLine, but what is typed is not shown (an API key).
    string? ReadSecret(string prompt);
}
