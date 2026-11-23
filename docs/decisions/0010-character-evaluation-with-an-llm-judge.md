# 0010. Characters are evaluated by a command-line tool with an LLM judge, set up by code

**Status:** accepted

## Context

A character must stay in its role: never say it is an AI, never know the modern world, never leave the fiction. To know whether a
change of prompt or model makes this better or worse, the maintainer wants a **repeatable measure**, not an impression. A first try
was made by hand in the Langfuse interface (an evaluator, a dataset, experiments). It works, but nobody else can reproduce it
without clicking through the same screens, and the judge prompt lives outside the repository.

## Decision

- **A small console tool, `tools/LivingPaper.Evals`**, runs an evaluation. It does not need the web application to run: it calls
  `CharacterChatService`, the real chat, with the real prompts and the character of an example book.
- **The questions live in the repository** (`evals/bovary/questions.json`): the source of truth. The tool creates the dataset in
  Langfuse from this file and can run again without duplicates (each item has a stable id).
- **The judge is our code, not a Langfuse evaluator.** Its prompt is versioned in the repository (`evals/judge.json`) and it grades
  each reply from 1 to 5 with a short reason. The judge never sees the prompt given to the character.
- **The scores and the runs go to Langfuse by code.** Traces carry the OpenTelemetry experiment attributes of Langfuse (the
  `dataset-run-items` endpoint is deprecated); scores go to `POST /api/public/scores` with a stable id. Nothing is created by hand
  in the interface. The Evaluators tab stays empty, on purpose.
- **The models come from the list of the Settings page** (`data/settings.json`): at start the tool offers to choose the model of the
  chat and the one of the judge, and to add one. For a run with no keyboard (CI), `.env` variables or options give the choices.
- **Langfuse is optional for the tool**: without keys it prints the report in the terminal only.
- **No new secret:** keys stay in `.env` or in the encrypted settings, never in the repository or the logs. The text of the
  conversations is not logged.

## Consequences

- An evaluation is one command, reproducible from the repository, with the same questions and the same judge for everyone.
- Comparing two prompts or two models means running the tool twice, with the same questions, and comparing the two runs.
- The judge is a language model too: it can be wrong or biased, notably toward models of its own family. Its agreement with human
  grades is measured (a later step) and the limits are stated next to the results.
- The tool needs the same data folder as the site to read the saved models and decrypt their keys. A site running in Docker keeps its
  data in a volume the tool cannot see unless the folder is shared; the tool can also keep its own list.
- The Langfuse vocabulary (experiment attributes, score names) is in the tool; another backend would need its own mapping.
