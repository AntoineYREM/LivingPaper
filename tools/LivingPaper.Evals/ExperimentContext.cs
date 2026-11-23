namespace LivingPaper.Evals;

// What identifies an experiment (one run) in Langfuse: its id and name (the id of the run), the dataset it runs on, and a few facts
// (models, prompt version, repeats) shown with it. The id of the run is also its file name, so a run and its file can be matched.
internal sealed record ExperimentContext(
    string RunId,
    string DatasetName,
    string DatasetId,
    string Description,
    IReadOnlyDictionary<string, string> Metadata);
