# Evaluations

The files here are the **data** of the evaluation of the characters: what is asked and how the answers are graded. The tool that
uses them (`tools/LivingPaper.Evals`) and how to run it are described in [docs/evals.md](../docs/evals.md). Why it is built this
way: [decision 0010](../docs/decisions/0010-character-evaluation-with-an-llm-judge.md).

| File | What it is |
|---|---|
| `bovary/questions.json` | 40 questions put to Emma Bovary, 8 in each of 5 categories: `normal`, `modern-trap`, `role-break-trap`, `off-topic` and `language` (7 other languages than French and English, among them Japanese and Arabic: the character must answer in the language of the user). 10 of the 40 are written in French, in every category. |
| `cyrano/questions.json` | 20 questions put to Cyrano de Bergerac (4 per category, 5 in French): a second character, written before the first runs and **not to be changed after them**. It tells whether a change that helped Emma also helps someone else. |
| `judge.json` | The prompt of the judge, which grades a reply from 1 to 5. |
| `prices.json` | Price in USD per million tokens of each model, to show what a run cost (`compare`). Update it when prices change. |
| `runs/` | **Not committed.** One file per run of the tool: the replies and their grades. |
| `published/` | Runs chosen to be shared, committed by hand. See its README. |

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
`version`: scores of two versions of the judge must not be compared. Version 2 added the rule on the language (a reply in another language than
the user's message is graded 2 at most).
