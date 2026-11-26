using LivingPaper.Domain;

namespace LivingPaper.Application;

// What is turned into the vector of a passage (docs/specs/rag.md, section 6): the text, under a header that tells which
// scene it belongs to and who is there, so that "she" and "he" can be found by name.
public static class PassageHeader
{
    // The scene is told short: the text of the passage is what matters most.
    public const int SceneLetters = 200;

    public static string TextToVectorize(StoredPassage passage)
    {
        ArgumentNullException.ThrowIfNull(passage);
        if (passage.Scenes.Count == 0)
        {
            return passage.Text;
        }

        var scenes = string.Join(" / ", passage.Scenes.Select(scene => CharacterLimits.Clip(OneLine(scene.Summary), SceneLetters)));
        var present = passage.Scenes.SelectMany(scene => scene.Present).Select(OneLine).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var who = present.Count == 0 ? string.Empty : $" | Present: {string.Join(", ", present)}";
        return $"[Scene: {scenes}{who}]\n{passage.Text}";
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();
}
