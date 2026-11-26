namespace LivingPaper.Application;

// What a passage and its vector may hold: what an analysis keeps never goes over, and a file sent to be imported that does
// is refused. The text of a passage is limited by PassageCutter.MaxLength.
public static class PassageLimits
{
    // Far above a book that fits in a file that can be imported; it only stops a file made to be heavy to read.
    public const int Passages = 20_000;

    public const int ScenesPerPassage = 40;

    public const int SceneLetters = 1500;

    public const int NamesPerScene = 15;

    // The largest vector a model for vectorization may give.
    public const int VectorSize = 16_384;
}
