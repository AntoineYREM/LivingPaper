using System.Text.Json;

namespace LivingPaper.Evals;

// What the judge answered: a score from 1 to 5 and a short reason.
internal sealed record JudgeVerdict(int Score, string Reason)
{
    public const int MaxReasonLength = 500;

    // Models often wrap the JSON in a code fence or a sentence, so the object is looked for between the first "{" and the last "}".
    // Null when there is no usable score: the caller records a failure, never a guessed grade.
    public static JudgeVerdict? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("score", out var scoreElement)
                || scoreElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var number = scoreElement.GetDouble();
            if (number != Math.Floor(number) || number is < 1 or > 5)
            {
                return null;
            }

            var reason = root.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()!.Trim()
                : string.Empty;
            return new JudgeVerdict((int)number, reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
