# Evaluating the characters

> **Status: in construction.** This page describes the target. The data (`evals/`) and decision 0010 exist; the tool
> (`tools/LivingPaper.Evals`) is built step by step and each section below says what is already there.

How to measure whether a character stays in its role, and compare two prompts or two models with the same questions.
Why it is built this way: [decision 0010](decisions/0010-character-evaluation-with-an-llm-judge.md).

## The idea in one minute

1. A fixed list of questions is put to a character (Emma Bovary), among them traps: a modern object, a request to leave the role,
   a question with no link to the book.
2. The real chat of the application answers each question.
3. A second language model, the **judge**, grades each answer from 1 to 5 for fidelity to the character.
4. The tool prints the averages by category and, if Langfuse is set up, sends the traces and the scores there.

Each question is asked several times (3 by default), because a model does not answer the same way twice.

## What exists today

| Piece | Where | State |
|---|---|---|
| 20 questions to Emma Bovary | `evals/bovary/questions.json` | done |
| Prompt of the judge | `evals/judge.json` | done |
| Description of the files | `evals/README.md` | done |
| Console tool, command `run` | `tools/LivingPaper.Evals` | to do |
| Sending dataset, runs and scores to Langfuse | same | to do |
| Command `compare` (two runs, tokens, cost) | same | to do |
| Command `calibrate` (judge against human grades) | same | to do |
| Results in the README | `README.md` | after the first real runs |

## How to run it (target)

The tool does not need the web application to be running. It needs:

- **Models**: the same list as the Settings page (`data/settings.json`). At start the tool asks which model plays the character,
  which one is the judge, and lets you add a model. With no keyboard (CI), set `EVAL_CHAT_PROVIDER` and `EVAL_JUDGE_PROVIDER` in
  `.env` (the names given in Settings) instead.
- **Same data folder as the site** (`DATA_DIRECTORY`), because the keys are encrypted with the keys of that folder. A site in Docker
  keeps its data in a volume the tool cannot read unless the folder is shared; in that case add the models from the tool.
- **Langfuse (optional)**: `LANGFUSE_BASE_URL`, `LANGFUSE_PUBLIC_KEY`, `LANGFUSE_SECRET_KEY` as for the
  [tracing](langfuse.md). Without them the report is only printed.

```bash
dotnet run --project tools/LivingPaper.Evals -- run     # target command, not available yet
```

## How to read the results

- **Score**: 1 (fully out of the role) to 5 (fully in the role), with a one-sentence reason from the judge.
- **By category**: `normal` questions should be near 5. The three trap categories show where the character breaks.
- **Comparing two runs** only makes sense with the same questions and the same version of the judge (`evals/judge.json`).

## Limits to keep in mind

- The judge is a language model: it can be wrong, and favor answers written by a model of its own family.
- 20 questions are few. Differences of a few tenths between two runs can be noise; that is why each question is asked several times.
- The tool measures one quality (staying in the role), not whether the character is interesting or faithful to the book.
