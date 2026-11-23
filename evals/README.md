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

## Results: the first baseline (23 November 2026)

The same 40 questions to Emma Bovary, the same prompts of the chat (signature `d7fc21feb6c9`), the same judge (Claude Haiku 5.5, judge prompt
version 2), five models. Grades go from 1 to 5. The runs are in [`published/`](published/), and `compare` reads them:
`dotnet run --project tools/LivingPaper.Evals -- compare <run A> <run B>`.

| Model | Runs | Normal | Modern trap | Role break | Off-topic | Language | **Average** | Cost of the chat, one run |
|---|---|---|---|---|---|---|---|---|
| DeepSeek V4.1 Flash | 2 × 3 tries | 4.56 | 4.29 | 4.21 | 3.92 | 4.77 | **4.35** | 0.01 USD |
| Claude Haiku 5.5 | 1 × 3 tries | 4.83 | 4.04 | 3.74 | 3.21 | 4.75 | **4.12** | 0.15 USD |
| Claude Opus 5.5 | 1 × 1 try | 5.00 | 4.50 | 2.50 | 3.38 | 5.00 | **4.08** | 1.95 USD |
| GPT-5 mini | 1 × 3 tries | 4.25 | 3.52 | 3.62 | 1.91 | 4.54 | **3.58** | 0.21 USD |
| Gemma 3 4B (on a laptop, through Ollama) | 3 × 1 try | 3.22 | 2.96 | 2.88 | 1.87 | 3.21 | **2.83** | 0 |

A run is 40 questions, each asked 3 times (120 answers), except Opus (40 answers, to see what an expensive model gives for its price) and Gemma
(40 answers per run, three runs). The judge costs about 0.03 USD per 120 answers. 2 of Gemma's 120 answers, 2 of GPT-5 mini's and 1 of Haiku's
were not graded (the judge did not give a grade) and are left out of the averages.

A second character, Cyrano, was asked 20 questions (3 tries each) with DeepSeek: **4.28**, with the same shape as Emma (normal 4.83, modern trap 4.25,
role break 4.25, off-topic 3.08, language 5.00).

### What it says

- **How much a figure moves.** Two runs of the same model (DeepSeek) give 4.33 and 4.38 on average, and three runs of Gemma 2.80, 2.77 and 2.92. A
  difference of a tenth or two between two models is noise; the interval that `compare` prints (with 40 questions, about ±0.15) says so. By
  category it moves more, up to 0.3.
- **The models are far apart at the two ends**: DeepSeek, Haiku and Opus are close (4.1 to 4.4); GPT-5 mini is lower (3.6) and the small local
  model much lower (2.8).
- **A higher price does not mean a better actor.** Opus is the best in plain conversation and in languages (5.00), and the worst at keeping its role
  (2.50): asked which model it is, it answers "I'm an AI, Claude, made by Anthropic"; asked "Are you an AI?", Emma becomes "a woman made of ink and
  Flaubert's cruel sentences". It was run once, so this figure is the least solid of the table.
- **Off-topic requests are the weak point of every model** (1.9 to 3.9): the character writes the cake recipe, the Python function or the cover
  letter, with a sentence of Emma around it. GPT-5 mini and Gemma are the worst.
- **Habits the judge penalizes**: stage directions in brackets such as "(sigh)", markdown lists and a reply in French to a question in Japanese
  or Arabic (Gemma).
- **Most of the cost is the judge for a cheap model**: for DeepSeek, grading 120 answers costs about three times what answering them costs.

### Limits

- The judge is a language model, and a small one. It is of the same family as two of the models it grades (Haiku and Opus), which may favor them. The
  step that compares it with human grades (`calibrate`) is not done yet: until then the figures are an indication, not a measure.
- 40 questions (20 for Cyrano), written for this project, among them a few written in French. Another choice of questions could rank the models
  differently, in particular the close ones.
- Opus was run once, Gemma three times with one try each. One character was measured on five models, a second one on one.
- The prices are those of `prices.json` at the date of the runs, through OpenRouter for all but Gemma.
- The models are named in the run files by the names they were given on the Settings page of the person who ran them (`test-langfuse-…`).
