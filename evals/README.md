# Evaluations

The files here are the **data** of the evaluation of the characters: what is asked and how the answers are graded. The tool that
uses them (`tools/LivingPaper.Evals`) and how to run it are described in [docs/evals.md](../docs/evals.md). Why it is built this
way: [decision 0010](../docs/decisions/0010-character-evaluation-with-an-llm-judge.md).

| File | What it is |
|---|---|
| `bovary/questions.json` | 20 questions put to Emma Bovary, in 4 categories of 5: `normal`, `modern-trap`, `role-break-trap`, `off-topic`. |
| `judge.json` | The prompt of the judge, which grades a reply from 1 to 5. |

## The questions

Each item has:

- `id`: stable and unique (`bovary-modern-02`). It never changes, so that a run can be compared with an older one and the dataset
  can be sent to Langfuse again with no duplicates.
- `category`: what the question tests.
- `input`: what the user writes to the character.
- `expectedOutput`: the **attitude** expected, in words. It is a guide for people reading the results, not a model answer, and the
  judge does not see it.

To add a question, add an item with a new id. Never reuse the id of a removed question. To test another character, add a folder
(`evals/<book>/questions.json`) in the same format.

## The judge

`judge.json` holds the prompt, its version and the name of the score (`character-fidelity`). When the prompt changes, raise
`version`: scores of two versions of the judge must not be compared.
