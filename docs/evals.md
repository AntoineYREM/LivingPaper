# Evaluating the characters

> **Status: in construction.** The command `run` works: it prints its report, keeps every run in a file, and sends it to Langfuse when
> Langfuse is configured. The command `compare` sets two runs side by side and `latency` gives the time to answer. Measuring the judge against human grades is the next step.

How to measure whether a character stays in its role and gets the facts of the book right, and compare two prompts, two models
or two ways of giving the book to the character with the same questions.
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
| 40 questions to Emma Bovary, and 20 to Cyrano (a second character, to check that a finding is not specific to Emma) | `evals/bovary/questions.json`, `evals/cyrano/questions.json` | done |
| 26 factual questions to Emma, each with its expected answer and the phrases of the book that hold it | `evals/bovary/factual.json` | done |
| Prompt of the judge of the role, and of the judge of the facts | `evals/judge.json`, `evals/accuracy-judge.json` | done |
| Search of the passages of an indexed book (`--embedding`), kept and reported for each try | `tools/LivingPaper.Evals` | done |
| Console tool, command `run` (report in the terminal) | `tools/LivingPaper.Evals` | done |
| Every run kept in a file (`evals/runs/`), shareable ones in `evals/published/` | same | done |
| Sending dataset, one trace per answer and the scores to Langfuse | same | done, to be checked on your Langfuse |
| Command `compare` (two runs, tokens, cost) and the price file `evals/prices.json` | same | done |
| Command `latency` (time to answer of each run, read from Langfuse) | same | done |
| Command `charts` (three pictures of the README, drawn from the published files) | same, `docs/images/evals/` | done |
| Command `calibrate` (judge against human grades) | same | to do |
| First baseline on five models, with the runs and their limits | `README.md`, `evals/README.md`, `evals/published/` | done (23 November 2026) |

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
- **The book**: `examples/madame-bovary.json`, found by going up from the current folder (`EXAMPLES_DIRECTORY` to move it), or the
  book of a library with `--book-data` (see [Facts and passages](#facts-and-passages)).

### Options

| Option | Meaning |
|---|---|
| `--repeats <n>` | Times each question is asked, 1 to 20 (default 3). |
| `--chat <name>`, `--judge <name>` | The models, by their name on the Settings page. No question is asked. |
| `--questions <file>`, `--judge-prompt <file>` | Other files than `evals/bovary/questions.json` and `evals/judge.json`. `--questions evals/cyrano/questions.json` plays the second character. |
| `--prompts-file <file>` | Another prompts file for the chat, to try a change of prompt without touching the `prompts.json` of your data folder. |
| `--label <text>` | A short name for the run (`rule-no-ai`), kept in the run file and shown by `compare`. |
| `--max-tokens <n>` | A ceiling on the length of every reply (thinking included). Needed with an expensive model on OpenRouter, which sets aside the price of the longest possible reply before answering: without a ceiling, a key with a spending limit is refused (HTTP 402). Not for OpenAI's own newest models, which refuse `max_tokens`. |
| `--local-only` | Keep the run in a file only, even when Langfuse is configured. |
| `--book-data <folder>` | A data folder whose library holds the book (`books/` and `rag.db`), instead of `examples/`. |
| `--embedding <name>` | Search the passages with this model for vectorization (its name on the Settings page). Without it, the character gets the whole story. Needs `--book-data`. |
| `--embedding-url <url>` | The address of a server that asks for no key (a local one): `--embedding` is then the name of its model, and nothing needs to be saved on the Settings page. |
| `--query-mode <mode>` | `recent` (default), `rewrite` or `last`: how the search is written (`RAG_QUERY_MODE` for the site). |
| `--passages <n>` | Passages given to the character, 1 to 10 (default 3, `RAG_PASSAGES`). |
| `--help` | The list of options. |

For a run with no keyboard (a script, CI), name the models with the options or with `EVAL_CHAT_PROVIDER` and `EVAL_JUDGE_PROVIDER` in the
shell or in `.env`: without a terminal, the tool never waits for an answer and says what is missing. Exit codes: `0` done, `1` nothing could
be graded, `2` something to fix (the message says what), `130` stopped with Ctrl+C.

## Facts and passages

What was measured with these questions, and what was decided: [experiments on the passages](experiments/passages-of-the-book.md).

Three more sets ask 20 factual questions each on the shipped examples, in their English text: `evals/alice/factual.json`,
`evals/cyrano/factual.json` and `evals/bovary/factual-en.json`. To search their passages, `--book-data` needs a library that
holds the books **under the same ids** as the question files (adding an example from the interface gives its copy a new id, so
the library has to be prepared apart).

The judge of the role cannot see an invented fact: it knows only the question and the reply. The **factual questions**
(`evals/bovary/factual.json`) give the right answer, and a second judge (`evals/accuracy-judge.json`, score `factual-accuracy`)
grades each reply from 1 to 5 against it: 5 right, 2 "I do not remember", 1 something false. Seven of the 26 questions are
**follow-ups** ("And on the way home, did you find anything?"): they are sent after the earlier messages, and only make sense with
them, which is where the way the search is written matters.

To compare the whole story with the passages, the book must be **indexed**: analyze it on the site with a model for vectorization
chosen, then give the tool the data folder of the site (`--book-data`). Both runs below read the same book file, so the only
difference is what the character is given:

```bash
facts="--questions evals/bovary/factual.json --judge-prompt evals/accuracy-judge.json --book-data src/LivingPaper.Web/data"
dotnet run --project tools/LivingPaper.Evals -- run $facts --label story
dotnet run --project tools/LivingPaper.Evals -- run $facts --label recent-5 --embedding <name> --query-mode recent --passages 5
```

Run the role questions the same two ways (without `--questions` and `--judge-prompt`) to check that the passages do not cost
fidelity. Then `compare` sets two runs side by side.

Before the first call, the tool checks that the book is indexed with the model asked for and that every phrase of the questions is
in a passage: the chat would otherwise use the whole story without saying so. The phrases are in French, as the book analyzed was.
With another text or another cutting, a phrase may not be found: the tool then names the questions.

With passages, each try keeps in the run file the **query** searched, the numbers and distances of the **passages found**, whether
**a phrase of the question was in one of them**, the notices a reader would have seen (query not written, search failed), the
tokens and the time of the reply. The report adds, per reply: the tokens of the prompt (average and largest), those spent writing
the query, the median time, and how many times the expected passage was found. A low grade with the passage found is a failure of
the answer; with the passage missed, of the search.

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

- the **dataset** `madame-bovary-emma` with its 40 questions as items (found again by their ids on the next run, never duplicated);
- an **experiment** for the run, named like its file, with **one trace per answer** (the reply of the character, and the call of the judge
  below it), in the environment `experiment` so the traces of the real chat stay apart;
- one **score** `character-fidelity` per graded answer, with the reason of the judge as its comment.

Look for them in the Datasets page of Langfuse (the dataset and its runs), and filter the traces by the environment `experiment`. The
Evaluators page stays empty on purpose: the judge is in this tool, not in Langfuse. The text of the question and of the reply is sent
(it is evaluation data on a public-domain book); the prompts sent to the models, which hold the story of the book, only with
`LANGFUSE_CAPTURE_CONTENT=true`. If Langfuse cannot be used (wrong key, not running), the tool says so, **the run is saved anyway**, and the
local file stays the reference. `--local-only` skips Langfuse.

## Comparing two runs

```bash
dotnet run --project tools/LivingPaper.Evals -- compare 20261123-173454-deepseek-deepseek-v4-1-flash 20261124-091200-gemma3-4b
```

A run is the path of its file, or its id (the name of the file in `evals/runs/` or `evals/published/`, with or without `.json`). A is the
baseline and B the candidate: a positive difference means B did better. Nothing is sent anywhere and Langfuse is not needed: the two files
are enough. To compare two **models**, run the tool twice with the same questions, the same judge and the same prompts, changing only the
model of the chat (`--chat`); to compare two **prompts**, change the prompt in your data folder between the two runs.

The page shows, in this order:

- **Warnings**, when the comparison is not fair: not the same dataset, character or judge prompt version (grades of two versions of the
  judge are not comparable), not the same judge model, not the same number of tries, or questions that one run did not grade (they are
  left out, so a failure never counts as a bad score).
- **The average by category**, over the questions both runs graded, with the difference B - A.
- **The overall difference with a 95% interval.** The tries of a question are averaged first, then the difference of each question is
  taken (a paired comparison), and the interval is that of the mean of these differences. If the interval does not include 0, B really
  looks better or worse than A on these questions; if it does, the difference could be noise. With 40 questions the interval is still wide: a
  gap of a few tenths is usually inside it. It does not cover the bias of the judge, nor the choice of the questions.
- **The questions that differ most**, to see where to look first (the replies are in the two run files).
- **Tokens and cost** of each run, chat and judge apart.
- **Per reply**, how each run gave the book to the character (the whole story, or the passages), the tokens, the time, and how often
  the expected passage was found.

## Time to answer

```bash
dotnet run --project tools/LivingPaper.Evals -- latency 20261123-191324-deepseek-deepseek-v4-1-flash 20261123-200404-anthropic-claude-opus-5-5 --out evals/published/latency.json
```

Langfuse records, for each answer of the chat, how long the model took (from the request to the end of the streamed reply) and when the first
text arrived. The command asks Langfuse for them (it only reads; the `LANGFUSE_*` variables are needed, and the runs must have been sent to it), keeps
the calls of the model of each run that started while the run was going, and shows the mean, the median, the slowest tenth and the mean time to
the first text. The judge is another call and is not counted. `--out` also writes the figures as JSON, which holds no key and no text:
`evals/published/latency.json` is what it wrote for the published runs.

What it asks is `GET /api/public/v2/observations?type=GENERATION&name=chat-reply&environment=experiment&fromStartTime=<date>` (with the
`fields` `core,basic,time,model`, page after page with the cursor), using your own keys. The figures depend on the network and on how busy the
provider was; the tool does not hide that, and the README says where and when they were measured. A retried call counts as one more call.

## The charts

```bash
dotnet run --project tools/LivingPaper.Evals -- charts
```

Draws the three pictures of the README in `docs/images/evals/`, each as an SVG for a light page (`-light`) and for a dark one (`-dark`); the
README shows the one that matches the theme of GitHub with a `<picture>` tag. It reads only files of the repository (the runs of
`evals/published/`, `evals/published/latency.json`, `evals/prices.json`): nothing is sent anywhere and Langfuse is not needed. So a figure on a
picture is the one of the files, and publishing a new run and running the command again is enough to update them.

- **Before and after**: for each model (and character) that has runs with both prompts, the mean grade with the prompt of
  `evals/prompts/baseline.json` and with the current default prompt, told apart by the signature kept in the run files.
- **Grade against the price**: the models run with the current prompt on the main dataset; the price of 1,000 replies is worked out from the
  tokens of the chat and `prices.json`. A free or local model (price 0) stands in a band on the left, as it has no place on a logarithmic axis;
  a model with no price is left out and the command says so.
- **Time to answer**: the latest run of each model, from `latency.json`, in two parts (until the first text, then until the end).

The name written for a model is the `name` of its entry in `prices.json`. The colors are the first two of the palette of the dataviz skill,
checked for color-blind readers and for contrast in both themes.

### The prices

`evals/prices.json` lists, for each model, the price in USD per million tokens of the input, the cached input and the output. The cost of a
run is `(input - cached) x input price + cached x cache price + output x output price`: the input tokens are all those of the prompt, the
cached ones included, as the providers report them. A model that is not in the file shows `no price for <model>`, never a guess; a model
that runs on your own computer is listed at 0. The prices change: the file says when it was updated, and the values come from the list
of OpenRouter, so the models of another provider (a direct key, for example) need their own entry. `--prices <file>` uses another file.

## How to read the results

The report of `run` looks like this (the numbers here are made up, to show the shape):

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
  PassageCheck.cs       before a run with passages: the book is indexed, the phrases of the questions are in it
  RetrievalRecorder.cs  keeps what each try searched, found and cost; PassageQuotes.cs compares a phrase and a passage
  TraceSummary.cs       the tokens, time and passages found per reply
  JudgePrompt.cs        reads the judge file, fills the prompt
  JudgeVerdict.cs       reads the judge's answer (score and reason)
  EvalRunner.cs         questions x repeats: answer, then grade (each try is traced when Langfuse is on)
  RunRecord.cs, RunStore.cs   the file of a run, and how it is saved and read
  CompareCommand.cs, CompareOptions.cs   the command `compare`
  RunComparison.cs      the maths: averages by category, paired differences, 95% interval, warnings
  ComparisonWriter.cs   the page of `compare`
  LatencyCommand.cs, LatencyOptions.cs, LatencyWriter.cs, RunLatency.cs, ChatTiming.cs   the command `latency`: the time to answer of each run
  ChartsCommand.cs, ChartData.cs, ChartSvg.cs, LatencyFile.cs   the command `charts`: the figures, then the SVG pictures
  PriceList.cs          evals/prices.json and the cost of a run
  RunLocator.cs         finds a run from its id or its path
  LangfuseApi.cs        the REST calls: dataset, items, scores, the timings of the chat calls
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
- The judge receives the question (with the earlier messages), the reply and, for the facts, the expected answer; never the
  prompt the character had.
- With `--prompts-file`, the `prompts.json` of the data folder is neither read nor updated.
- The tests (`tests/LivingPaper.Evals.Tests`) use fake models: none calls a real provider.

## Limits to keep in mind

- The judge is a language model: it can be wrong, and favor answers written by a model of its own family.
- 40 questions are few. Differences of a few tenths between two runs can be noise; that is why each question is asked several times.
- The tool measures staying in the role and getting facts right, not whether the character is interesting.
- 26 factual questions on one book, written by reading its passages: they can favor what the search finds easily. They are in
  English and the text is in French: a reader writing in French may get a different search.
- The text of the replies is never printed in the terminal and never logged. It is kept in the run file (and sent to Langfuse when it is on).
