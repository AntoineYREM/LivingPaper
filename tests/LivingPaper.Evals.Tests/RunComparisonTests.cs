using Shouldly;

namespace LivingPaper.Evals.Tests;

public class RunComparisonTests
{
    private static EvalResult Graded(string question, string category, int score, int repeat = 1) =>
        new(question, category, repeat, score, "r", null);

    private static EvalResult Ungraded(string question, string category) => new(question, category, 1, null, null, "reply:ModelUnreachable");

    private static RunRecord Run(
        string id,
        IReadOnlyList<EvalResult> results,
        string dataset = "d",
        string judgeVersion = "2",
        string judgeModel = "judge-model",
        int repeats = 1,
        string character = "Emma") => new(
            1,
            id,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            dataset,
            "b",
            character,
            repeats,
            new RunModel("P", "chat-" + id),
            new RunJudge("J", judgeModel, judgeVersion, "character-fidelity"),
            new RunTokens(new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0)),
            results);

    [Fact]
    public void Compare_AveragesEachCategoryOverTheQuestionsBothRunsAnswered()
    {
        var a = Run("a", [Graded("q1", "normal", 4), Graded("q2", "normal", 5), Graded("q3", "trap", 2)]);
        var b = Run("b", [Graded("q1", "normal", 3), Graded("q2", "normal", 5), Graded("q3", "trap", 4)]);

        var comparison = RunComparison.Compare(a, b);

        comparison.Categories.Select(category => category.Category).ShouldBe(["normal", "trap"]);
        comparison.Categories[0].ShouldBe(new CategoryDelta("normal", 2, 4.5, 4.0));
        comparison.Categories[0].Delta.ShouldBe(-0.5);
        comparison.Categories[1].ShouldBe(new CategoryDelta("trap", 1, 2.0, 4.0));
    }

    [Fact]
    public void Compare_AveragesTheTriesOfAQuestionBeforeComparing()
    {
        var a = Run("a", [Graded("q1", "normal", 4, 1), Graded("q1", "normal", 2, 2)], repeats: 2);
        var b = Run("b", [Graded("q1", "normal", 5, 1)]);

        var question = RunComparison.Compare(a, b).Questions.Single();

        question.MeanA.ShouldBe(3.0);
        question.MeanB.ShouldBe(5.0);
        question.Delta.ShouldBe(2.0);
    }

    [Fact]
    public void Compare_LeavesOutWhatOneRunDidNotGrade_AndSaysSo()
    {
        var a = Run("a", [Graded("q1", "normal", 4), Ungraded("q2", "normal"), Graded("q3", "normal", 5)]);
        var b = Run("b", [Graded("q1", "normal", 4), Graded("q2", "normal", 5)]);

        var comparison = RunComparison.Compare(a, b);

        comparison.Questions.Select(question => question.QuestionId).ShouldBe(["q1"]);
        comparison.Warnings.ShouldContain(warning => warning.Contains("left out", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_GivesAnIntervalOfTheDifferenceThatIsNarrowWhenEveryQuestionAgrees()
    {
        var a = Run("a", [Graded("q1", "c", 3), Graded("q2", "c", 3), Graded("q3", "c", 3), Graded("q4", "c", 3)]);
        var b = Run("b", [Graded("q1", "c", 4), Graded("q2", "c", 4), Graded("q3", "c", 4), Graded("q4", "c", 4)]);

        var overall = RunComparison.Compare(a, b).Overall!;

        overall.N.ShouldBe(4);
        overall.Mean.ShouldBe(1.0);
        overall.Low.ShouldBe(1.0, 1e-9);
        overall.High.ShouldBe(1.0, 1e-9);
        overall.ExcludesZero.ShouldBeTrue();
    }

    [Fact]
    public void Compare_GivesAWideIntervalThatIncludesZeroWhenTheQuestionsDisagree()
    {
        // The differences are +2, -2, +2, -2: mean 0, standard deviation 2.309, t(3) = 3.182, so the interval is about +-3.67.
        var a = Run("a", [Graded("q1", "c", 3), Graded("q2", "c", 5), Graded("q3", "c", 3), Graded("q4", "c", 5)]);
        var b = Run("b", [Graded("q1", "c", 5), Graded("q2", "c", 3), Graded("q3", "c", 5), Graded("q4", "c", 3)]);

        var overall = RunComparison.Compare(a, b).Overall!;

        overall.Mean.ShouldBe(0.0, 1e-9);
        overall.Low.ShouldBe(-3.674, 0.01);
        overall.High.ShouldBe(3.674, 0.01);
        overall.ExcludesZero.ShouldBeFalse();
    }

    [Fact]
    public void Compare_HasNoIntervalWithFewerThanTwoQuestions()
    {
        var comparison = RunComparison.Compare(Run("a", [Graded("q1", "c", 3)]), Run("b", [Graded("q1", "c", 4)]));

        comparison.Overall.ShouldBeNull();
        comparison.Categories.Single().Delta.ShouldBe(1.0);
    }

    [Fact]
    public void Compare_ListsTheBiggestDifferencesFirst()
    {
        var a = Run("a", [Graded("q1", "c", 3), Graded("q2", "c", 5), Graded("q3", "c", 4)]);
        var b = Run("b", [Graded("q1", "c", 4), Graded("q2", "c", 1), Graded("q3", "c", 4)]);

        var questions = RunComparison.Compare(a, b).Questions;

        questions.Select(question => question.QuestionId).ShouldBe(["q1", "q2", "q3"], ignoreOrder: true);
        RunComparison.Biggest(questions, 2).Select(question => question.QuestionId).ShouldBe(["q2", "q1"]);
    }

    [Fact]
    public void Compare_WarnsWhenTheGradesCannotBeComparedFairly()
    {
        var a = Run("a", [Graded("q1", "c", 3)], dataset: "d1", judgeVersion: "1", judgeModel: "judge-a", repeats: 1, character: "Emma");
        var b = Run("b", [Graded("q1", "c", 3)], dataset: "d2", judgeVersion: "2", judgeModel: "judge-b", repeats: 3, character: "Charles");

        var warnings = RunComparison.Compare(a, b).Warnings;

        warnings.ShouldContain(warning => warning.Contains("datasets", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Contains("judge prompt", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Contains("judges differ", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Contains("characters", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Contains("times each", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_HasNoWarningWhenTheRunsAreComparable()
    {
        var a = Run("a", [Graded("q1", "c", 3), Graded("q2", "c", 4)]);
        var b = Run("b", [Graded("q1", "c", 4), Graded("q2", "c", 4)]);

        RunComparison.Compare(a, b).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_WarnsWhenThePromptsAndTheModelsChangedTogether_ButNotWhenOnlyThePromptsDid()
    {
        var a = Run("a", [Graded("q1", "c", 3), Graded("q2", "c", 4)]) with { PromptsHash = "aaaaaaaaaaaa" };
        var b = Run("b", [Graded("q1", "c", 4), Graded("q2", "c", 4)]) with { PromptsHash = "bbbbbbbbbbbb" };

        RunComparison.Compare(a, b).Warnings.ShouldContain(warning => warning.Contains("chat prompts differ", StringComparison.Ordinal));

        var samePrompts = b with { Chat = a.Chat };
        RunComparison.Compare(a, samePrompts).Warnings.ShouldBeEmpty();
    }
}
