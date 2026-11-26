using System.Text;
using Shouldly;

namespace LivingPaper.Application.Tests;

// PAPER-64: a story longer than its limit is shortened scene by scene; it is never cut at the end, which would lose how the
// book ends.
public class StoryFitterTests
{
    // About 300 characters, with who is present at the end.
    private static string Scene(int number, int sentences = 3) =>
        $"Scene {number}. " + string.Concat(Enumerable.Range(1, sentences).Select(sentence => $"In moment {sentence} of scene {number}, Emma walks to the window and says something to Charles about the town. "))
        + $"Present: Emma Bovary, Charles Bovary {number}.";

    private static string Story(int scenes, int sentences = 3) => string.Join("\n\n", Enumerable.Range(1, scenes).Select(number => Scene(number, sentences)));

    [Fact]
    public void AStoryWithinTheLimit_IsLeftAsItIs()
    {
        var story = Story(5);

        StoryFitter.Fit(story, story.Length).ShouldBe(story);
        StoryFitter.Fit(story, 100_000).ShouldBe(story);
    }

    [Fact]
    public void ALongStory_KeepsEveryScene_ItsPresentCharacters_AndItsLastSceneWhole()
    {
        var story = Story(80);

        var fitted = StoryFitter.Fit(story, 14_000);

        fitted.Length.ShouldBeLessThanOrEqualTo(14_000);
        fitted.Length.ShouldBeGreaterThan(9_000); // Not shortened more than needed by far.
        var paragraphs = fitted.Split("\n\n");
        paragraphs.Length.ShouldBe(80);
        for (var number = 1; number <= 80; number++)
        {
            paragraphs[number - 1].ShouldStartWith($"Scene {number}. In moment 1 of scene {number},");
            paragraphs[number - 1].ShouldEndWith($". Present: Emma Bovary, Charles Bovary {number}.");
        }

        paragraphs[^1].ShouldBe(Scene(80));
    }

    [Fact]
    public void ShortScenesStayWhole_TheLongOnesAreShortened()
    {
        var story = string.Join("\n\n", Scene(1, 1), Scene(2, 10), Scene(3, 1), Scene(4, 1));

        var paragraphs = StoryFitter.Fit(story, 900).Split("\n\n");

        paragraphs[0].ShouldBe(Scene(1, 1));
        paragraphs[2].ShouldBe(Scene(3, 1));
        paragraphs[3].ShouldBe(Scene(4, 1));
        paragraphs[1].Length.ShouldBeLessThan(Scene(2, 10).Length);
        paragraphs[1].ShouldEndWith("about the town. Present: Emma Bovary, Charles Bovary 2."); // Cut between two sentences.
    }

    [Fact]
    public void ALongListOfCharacters_StaysWithItsScene_EvenWhenTheSceneIsMuchShortened()
    {
        // The list is longer than half of what the scene is allowed.
        const string Present = "Present: Emma Bovary, Charles Bovary, Homais, Canivet, Larivière, Bournisien, Félicité, Lestiboudois, Binet.";
        var story = string.Join("\n\n", Enumerable.Range(1, 20).Select(number => $"Scene {number}. " + string.Concat(Enumerable.Repeat("Emma waits at the window. ", 12)) + Present));

        var paragraphs = StoryFitter.Fit(story, 4_000).Split("\n\n");

        paragraphs.Length.ShouldBe(20);
        paragraphs.ShouldAllBe(paragraph => paragraph.EndsWith(Present, StringComparison.Ordinal));
        paragraphs.ShouldAllBe(paragraph => paragraph.Contains("Emma waits", StringComparison.Ordinal));
    }

    [Fact]
    public void ASceneOfClausesSeparatedBySemicolons_IsCutAtOneOfThem()
    {
        var story = "Scene 1. " + string.Concat(Enumerable.Repeat("Emma waits at the window; ", 20)) + "Charles sleeps. Present: Emma.\n\nScene 2. The end. Present: Emma.";

        var first = StoryFitter.Fit(story, 300).Split("\n\n")[0];

        first.ShouldEndWith("Emma waits at the window. Present: Emma.");
    }

    [Fact]
    public void AStoryInASingleBlock_KeepsItsBeginningAndItsEnd()
    {
        var sentences = Enumerable.Range(1, 400).Select(number => $"Event number {number} happens to Emma in the town.").ToList();

        var fitted = StoryFitter.Fit(string.Join(' ', sentences), 8_000);

        fitted.Length.ShouldBeLessThanOrEqualTo(8_000);
        fitted.ShouldStartWith(sentences[0]);
        fitted.ShouldEndWith(sentences[^1]);
    }

    [Theory]
    [InlineData(200)] // Too many scenes for the limit to give each a few words.
    [InlineData(3_000)]
    public void WhenTheScenesCannotAllBeKept_TheBeginningAndTheEndStay(int max)
    {
        var story = Story(500);

        var fitted = StoryFitter.Fit(story, max);

        fitted.Length.ShouldBeLessThanOrEqualTo(max);
        fitted.ShouldStartWith("Scene 1.");
        fitted.ShouldEndWith("Charles Bovary 500.");
    }

    [Fact]
    public void ATextWithNoSpaceNorSentence_StillFits_KeepsItsEnd_AndNeverHalvesACharacter()
    {
        var text = "a" + string.Concat(Enumerable.Repeat("\U00020000", 3000)) + "z";

        foreach (var max in new[] { 100, 101, 1000, 1001 })
        {
            var fitted = StoryFitter.Fit(text, max);

            fitted.Length.ShouldBeLessThanOrEqualTo(max);
            fitted.ShouldEndWith("z");
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(fitted)).ShouldBe(fitted);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnyText_FitsTheLimit_AndIsAlwaysShortenedTheSameWay(int seed)
    {
        var random = new Random(seed);
        string[] pieces = ["word", " ", " ", " ", ". ", "; ", "\n", "\n\n", "Present: A, B.", "Scene 1. ", new('x', 700)];
        var text = string.Concat(Enumerable.Range(0, 3000).Select(_ => pieces[random.Next(pieces.Length)]));

        foreach (var max in new[] { 1, 50, 500, 5000 })
        {
            var fitted = StoryFitter.Fit(text, max);

            fitted.Length.ShouldBeLessThanOrEqualTo(max);
            StoryFitter.Fit(text, max).ShouldBe(fitted);
        }
    }
}
