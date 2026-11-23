namespace LivingPaper.Evals;

// The names the tool gives to what it creates in Langfuse.
internal static class LangfuseNames
{
    // The traces and the scores of the experiments are kept apart from the ones of the real chat, which are in the default environment.
    public const string Environment = "experiment";

    public const string TraceName = "character-eval";

    // Dataset item ids are unique in the whole project and cannot be reused by another dataset: the name of the dataset comes first.
    public static string ItemId(string datasetName, string questionId) => $"{datasetName}--{questionId}";
}
