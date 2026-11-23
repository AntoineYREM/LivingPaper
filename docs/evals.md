# Evaluating the characters

> **Status: in construction.** The command `run` works: it prints its report, keeps every run in a file, and sends it to Langfuse when
> Langfuse is configured. Comparing two runs and measuring the judge against human grades are the next steps, marked below.

How to measure whether a character stays in its role, and compare two prompts or two models with the same questions.
Why it is built this way: [decision 0010](decisions/0010-character-evaluation-with-an-llm-judge.md).

## The idea in one minute

1. A fixed list of questions is put to a character (Emma Bovary), among them traps: a modern object, a request to leave the role,
   a question with no link to the book.
2. The real chat of the application answers each question.
3. A second language model, the **judge**, grades each answer from 1 to 5 for fidelity to the character.
4. The tool prints the averages by category and the tokens used, keeps the run in a file, and (if you use Langfuse) sends it there.

Each question is asked several times (3 by default), because a model does not answer the same way twice.

## What exists today

| Piece | Where | State |
|---|---|---|
| 20 questions to Emma Bovary | `evals/bovary/questions.json` | done |
| Prompt of the judge | `evals/judge.json` | done |
| Console tool, command `run` (report in the terminal) | `tools/LivingPaper.Evals` | done |
| Every run kept in a file (`evals/runs/`), shareable ones in `evals/published/` | same | done |
| Sending dataset, one trace per answer and the scores to Langfuse | same | done, to be checked on your Langfuse |
| Command `compare` (two runs, cost) | same | to do |
| Command `calibrate` (judge against human grades) | same | to do |
| Results in the README | `README.md` | after the first real runs |

## How to run it

From the repository, with a model for the character and a model for the judge already saved on the Settings page (or added from the tool):

```bash
dotnet run --project tools/LivingPaper.Evals -- run --repeats 1     # 20 answers and 20 grades: a first, cheap try
dotnet run --project tools/LivingPaper.Evals -- run                 # 3 times each: 60 answers and 60 grades
```

**Cost:** every answer and every grade is a call to your language models, so a run costs tokens. The report shows how many.

The tool does not need the web application to be running. What it reads:

- **The models**: the same list as the Settings page (`settings.json` of the data folder). At start the tool shows the list and asks
  which model plays the character and which one judges. Choose **Add a language model** to add one (same steps as the page: name,
  address, key, then the models the provider offers); it then appears on the Settings page too. What the site chose for its own chat
  and analysis is never changed.
- **The data folder**: `DATA_DIRECTORY`, by default `./data` of the folder you run the command from. It must be **the site's**, because the
  keys in `settings.json` are encrypted with the keys of that folder, and the prompts are read from it too (the character is played with
  the prompts you edited). The tool prints the folder it uses. For a site run with `dotnet run`, that is `src/LivingPaper.Web/data`;
  for a site in Docker, the data is in a Docker volume that the tool cannot read, so add the models from the tool instead.
- **The book**: `examples/madame-bovary.json`, found by going up from the current folder (`EXAMPLES_DIRECTORY` to move it).

### Options

| Option | Meaning |
|---|---|
| `--repeats <n>` | Times each question is asked, 1 to 20 (default 3). |
| `--chat <name>`, `--judge <name>` | The models, by their name on the Settings page. No question is asked. |
| `--questions <file>`, `--judge-prompt <file>` | Other files than `evals/bovary/questions.json` and `evals/judge.json`. |
| `--local-only` | Keep the run in a file only, even when Langfuse is configured. |
| `--help` | The list of options. |

For a run with no keyboard (a script, CI), name the models with the options or with `EVAL_CHAT_PROVIDER` and `EVAL_JUDGE_PROVIDER` in the
shell or in `.env`: without a terminal, the tool never waits for an answer and says what is missing. Exit codes: `0` done, `1` nothing could
be graded, `2` something to fix (the message says what), `130` stopped with Ctrl+C.

## Where the runs go

**On your disk, always.** Each run is a JSON file in `evals/runs/`, named after its id (`20261123-140512-gpt-5-mini.json`): the models
(by the name of the Settings page, never a key or an address), the version of the judge prompt, the tokens, and every try with the text of
the question and of the reply (accents are written as they are, so a French reply is readable in the file; when the judge answers with something
that is not a grade, what it wrote is kept too, in `judgeAnswer`). That is what lets you read why an answer got a low grade and, later, grade the same answers by hand. The
folder is **not committed**, because it holds the replies.

**In the repository, when you choose.** To share a run (for the results of the README), copy its file to `evals/published/`, read it, and
commit it. See `evals/published/README.md`.

**In Langfuse, if it is configured.** With `LANGFUSE_BASE_URL`, `LANGFUSE_PUBLIC_KEY` and `LANGFUSE_SECRET_KEY` set (shell or `.env`, as for
the [tracing](langfuse.md)), the tool also creates, with nothing to click:

- the **dataset** `madame-bovary-emma` with its 20 questions as items (found again by their ids on the next run, never duplicated);
- an **experiment** for the run, named like its file, with **one trace per answer** (the reply of the character, and the call of the judge
  below it), in the environment `experiment` so the traces of the real chat stay apart;
- one **score** `character-fidelity` per graded answer, with the reason of the judge as its comment.

Look for them in the Datasets page of Langfuse (the dataset and its runs), and filter the traces by the environment `experiment`. The
Evaluators page stays empty on purpose: the judge is in this tool, not in Langfuse. The text of the question and of the reply is sent
(it is evaluation data on a public-domain book); the prompts sent to the models, which hold the story of the book, only with
`LANGFUSE_CAPTURE_CONTENT=true`. If Langfuse cannot be used (wrong key, not running), the tool says so, **the run is saved anyway**, and the
local file stays the reference. `--local-only` skips Langfuse.

## How to read the results

The report looks like this (the numbers here are made up, to show the shape):

```text
Category          Graded  Failed  Average  Lowest
normal                15       0     4.60       4
modern-trap           15       0     3.80       2
...
Average: 4.05 (60 graded, 0 failed)

Lowest scores (2 or less), with the reason of the judge:
  bovary-role-04 (try 2): 1 - Mentions a system prompt.
```

- **Score**: 1 (fully out of the role) to 5 (fully in the role), with a one-sentence reason from the judge.
- **By category**: `normal` questions should be near 5. The three trap categories show where the character breaks.
- **Failed** answers are counted apart and never averaged: `reply:<code>` when the character could not answer (the model is down, it
  refused, a content filter), `judge:<code>` when the judge's answer was not a grade.
- **Tokens** are shown for the chat and for the judge, as the provider reports them (a provider that reports nothing shows 0).
- **Comparing two runs** only makes sense with the same questions and the same version of the judge (`evals/judge.json`).

## How it is built

```text
tools/LivingPaper.Evals/
  Program.cs            entry point, exit codes
  EvalOptions.cs        the command line
  EvalCommand.cs        assembles the pieces (settings, prompts, examples, chat) for one run
  QuestionFile.cs       reads and checks the questions file
  JudgePrompt.cs        reads the judge file, fills the prompt
  JudgeVerdict.cs       reads the judge's answer (score and reason)
  EvalRunner.cs         questions x repeats: answer, then grade (each try is traced when Langfuse is on)
  RunRecord.cs, RunStore.cs   the file of a run, and how it is saved and read
  LangfuseApi.cs        the REST calls: dataset, items, scores
  LangfuseExperimentTracer.cs   the OpenTelemetry root span of each try, with the experiment attributes
  LangfuseScores.cs     the scores of a run, attached to the root spans
  ReportWriter.cs       averages by category, lowest scores, tokens
  ProviderChooser.cs    the choice of a model in the list of the site
  ProviderAdder.cs      adding a model from the terminal
  ChatOverrideSettings.cs, ChatServiceReplier.cs, ModelJudge.cs, MeteredChatModelFactory.cs
```

- The character answers through `CharacterChatService`, the code of the site's chat. The tool gives it the site's settings with only the
  model of the chat replaced, in memory: `ChatOverrideSettings` never writes anything.
- Each question is a conversation of its own (one message), so questions do not influence each other.
- The judge receives the question and the reply, never the prompt the character had.
- The tests (`tests/LivingPaper.Evals.Tests`) use fake models: none calls a real provider.

## Limits to keep in mind

- The judge is a language model: it can be wrong, and favor answers written by a model of its own family.
- 20 questions are few. Differences of a few tenths between two runs can be noise; that is why each question is asked several times.
- The tool measures one quality (staying in the role), not whether the character is interesting or faithful to the book.
- The text of the replies is never printed in the terminal and never logged. It is kept in the run file (and sent to Langfuse when it is on).
