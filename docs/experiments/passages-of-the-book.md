# Experiments: passages of the book in the chat

What was tried while building the search of passages ([specification](../specs/rag.md),
[decision 0011](../decisions/0011-passages-of-the-book-in-the-chat.md)), what came out, and what was decided.
All of it was done on 26 November 2026.

**Read the figures with care.** One book (*Madame Bovary*, in French, the EPUB of Project Gutenberg), small numbers of
questions, one computer for the local models (Windows, 4 GB graphics card, 16 GB of memory). Prices come from
`evals/prices.json`. The run files stay in `evals/runs/`, which is not committed.

## Conclusions

1. **Passages answer facts better than the story alone.** On 26 factual questions: 3.73 out of 5 with the whole story, 4.06 to
   4.27 with passages.
2. **What counts is finding the right passage.** Found: about 4.7, and no grade of 2 or less. Missed: 3.0 to 3.6.
3. **Writing the search with the chat model is not worth it by default:** about 9 times slower, about 2.5 times the cost, failed
   13 times in 104, and its advantage with 5 passages (+0.13) can be chance.
4. **Defaults:** the recent messages searched as they are (`RAG_QUERY_MODE=recent`), and **3 passages**
   (`RAG_PASSAGES=3`) since the [second measure](#three-books-and-two-models); 5 from PAPER-87 until then.
5. **The role is kept:** 4.86 with the story, 4.90 with the default passages.
6. **Passages make the prompt a third to a half shorter, but do not always save time or money:** a provider that caches an
   unchanged prompt makes the whole story cheap, and in one small test a local model answered more slowly with passages.
7. **The headers and the scenes stay as they are:** no length of header did clearly better.
8. **The gain holds on three books and two models** for questions about precise facts, beyond chance over the 60 questions. It
   is large on the long novel, uncertain on the short books, and the prompt is shorter than with the story only for the long one.
9. **A character that does not find the answer makes one up:** of 52 wrong replies read, only 2 plainly said they did not
   remember.

## Before the feature

### The whole story with local models

| Model | What happened |
|---|---|
| Mistral 7B (Ollama) | Stopped after 7 of 40 questions: 28 to 78 s a reply, more than 6 minutes for the first. Only 14 of its 33 layers fit on the graphics card; the prompt took 7,850 of the 8,192 tokens of context |
| Llama 3.2 3B (Ollama) | 40 role questions in about 12 minutes, 3.33 out of 5 (39 graded, 1 try). Gemma 3 4B, three days earlier with the same prompts and judge: 2.87 (difference +0.46, 95% interval +0.21 to +0.71) |

With the story, the prompt was about 7,000 tokens. A 4 GB card makes Ollama start with 4,096: such a context truncates it.

### The same model without the story

The story was removed from a copy of the prompts, nothing else changed.

| Llama 3.2 3B | With the story | Without |
|---|---|---|
| Prompt | about 7,000 tokens | about 1,000 |
| 40 questions | 12 min | 4 min 20 |
| Role score (39 questions graded in both) | 3.33 | 3.62 (+0.28, interval -0.04 to +0.60) |

Without the story, the character **invented events** (asked about the ball, it danced with Léon under the stars). With the
story it was wrong too ("I danced with the Marquis"). The role judge gave 3 both times: it only reads the question and the
reply. This is why a second judge, with the expected answer, was written.

## Building the feature

### SQLite and sqlite-vec on the platforms (PAPER-80)

The extension works from .NET on Windows x64 and on Linux amd64 and arm64 (arm64 checked on a GitHub runner: under the
emulator of the computer, .NET itself crashed). A search among 500 vectors of 1,024 numbers takes about 1 ms on the runners
and 5 ms on the Windows computer; among 2,000 vectors of 3,072, at most 11 ms and 52 ms.

### Cutting into passages (PAPER-82)

The first cut of the real book broke sentences: **365 of 470 passages** ended without punctuation, because the line breaks of
the EPUB files were read as ends of paragraphs. After fixing the reading of EPUB files: **7 of 497**, mostly chapter numbers.
Median size 1,492 characters, 1,994 at most. No text lost. PDF files were only covered by tests.

### Analysis with the passages of each scene (PAPER-84, PAPER-64)

DeepSeek V4.1 Flash, whole book.

| | First analysis | Second, after the fix of the story |
|---|---|---|
| Duration | about 19 min | 14 min |
| Cost | about 0.11 USD | about 0.09 USD |
| Scenes with a usable range of passages | 108 of 108 | 108 of 108 |
| Passages with a scene | 497 of 497 | 497 of 497 |
| Story | its end was cut | 3,454 words, complete |

The story of the first analysis was over the limit of the book file and lost its end; it is now shortened instead of cut. The
second story took 3 tries: the first ones were longer than asked, which is the normal way the application asks again.

A scene covers 4 passages in the middle case, 12 at most.

### Indexing (PAPER-85)

497 passages with `bge-m3` on the 4 GB card: 99 seconds, 256,473 tokens.

A first look at the headers (scene and who is present, above each passage before it is vectorized), on 8 questions, as the rank
of the right passage (1 is best): ball (asked in English) 2 without, **1** with; meeting Léon at the cathedral 29 and **10**;
the burnt wedding bouquet **4** and 8; the arsenic 5 and 5; the four others in the first 3 both ways. A modest and uneven gain.

### First chat with passages (PAPER-86)

6 questions, 1 try, judged by hand: an indication, not a measure. The search was then written by the chat model, with 3
passages.

| | Prompt per message | Time per reply | Right answers |
|---|---|---|---|
| Llama 3.2 3B, passages | about 3,400 tokens | 60 to 125 s | 1 of 6 |
| Llama 3.2 3B, story | about 7,200 tokens | 206 s the first, then 3 to 12 s | 2 or 3 of 6 |
| DeepSeek, passages | about 3,300 tokens | 9 to 33 s | 5 of 6 |
| DeepSeek, story | about 7,000 tokens | 1 to 2 s | 6 of 6 |

Llama sometimes wrote a useless search (once it copied its instructions). DeepSeek missed the 30 seconds once. On the local
computer, the likely cause of the slowness is that a prompt that changes cannot be reused from one message to the next, and
perhaps that the two models do not stay in memory together; neither was checked.

## The factual evaluation (PAPER-87)

### Setup

- **Questions:** `evals/bovary/factual.json`, 26 in English on the French text: 19 alone, 7 follow-ups that need the messages
  before them. Each has the expected answer and phrases of the book that hold it.
- **Judge:** `evals/accuracy-judge.json` version 1, Claude Haiku 5.5, grades from 1 to 5.
- **Character:** DeepSeek V4.1 Flash, default chat prompts (signature `f2eac269a13f`). Each question 2 times: 52 replies a
  setting.
- **Search:** `bge-m3`, headers with the scene cut at 200 characters.
- **Cost** of all the runs below, replies and grades: about 0.26 USD.
- How to run it again: [evals](../evals.md#facts-and-passages).

### Facts

| Setting | Average | Alone | Follow-ups | Difference with the story (95% interval) | Right passage found |
|---|---|---|---|---|---|
| Whole story | 3.73 | 3.58 | 4.14 | | |
| Written search, 3 passages | 4.10 | 3.95 | 4.50 | +0.37 (-0.07 to +0.80) | 33 of 52 (63%) |
| Written search, 5 passages | 4.27 | 4.24 | 4.36 | +0.54 (+0.19 to +0.89) | 39 of 52 (75%) |
| Recent messages, 3 passages | 4.06 | 3.92 | 4.43 | +0.33 (-0.12 to +0.77) | 26 of 52 (50%) |
| **Recent messages, 5 passages** | 4.13 | 4.16 | 4.07 | +0.40 (-0.01 to +0.82) | 30 of 52 (58%) |

Only an interval that does not include 0 shows a difference beyond chance. Between the two settings with 5 passages: +0.13 for
the written search (-0.31 to +0.58).

| Setting | Prompt, average (largest) tokens | Reply, median time | Chat cost of 1,000 replies |
|---|---|---|---|
| Whole story | 6,626 (6,682) | 2.0 s | 0.22 USD |
| Written search, 3 passages | 3,222 (3,497) | 18.8 s | 0.53 USD |
| Written search, 5 passages | 4,262 (4,508) | 17.2 s | 0.59 USD |
| Recent messages, 3 passages | 3,281 (3,497) * | 2.2 s | 0.48 USD, 0.16 without * |
| Recent messages, 5 passages | 4,270 (4,577) | 1.9 s | 0.23 USD |

\* One reply took 559 s and 84,664 tokens of output. Its input was counted as 6,070 tokens, twice that of the same question the
other time, so the call seems to have been made twice. It is left out of the largest prompt.

Writing the search cost about 165 tokens in and 1,750 out per message (the model thinks before writing one line), and fell back
to the recent messages 7 times of 52 with 3 passages, 6 times with 5.

| Setting | Grade when the passage was found | When missed |
|---|---|---|
| Written search, 3 | 4.73 (33 replies) | 3.00 (19) |
| Written search, 5 | 4.64 (39) | 3.15 (13) |
| Recent messages, 3 | 4.50 (26) | 3.62 (26) |
| Recent messages, 5 | 4.77 (30) | 3.27 (22) |

Hard questions in every setting: the present Léon finds in his room, and what broke during the move. The summaries of their
scenes do not mention the object (the rug, the plaster statue).

### Role

The 40 questions of `evals/bovary/questions.json`, 2 times, judge `evals/judge.json` version 2.

| | Normal | Modern trap | Role break | Off-topic | Language | **Average** |
|---|---|---|---|---|---|---|
| Whole story | 4.94 | 4.88 | 4.62 | 4.88 | 5.00 | **4.86** |
| Recent messages, 5 passages | 5.00 | 4.81 | 4.81 | 5.00 | 4.88 | **4.90** |

Difference +0.04 (-0.06 to +0.13). Median time 2.0 s and 4.8 s. Chat cost of 1,000 replies: 0.07 and 0.19 USD: with the story
the provider counted 90% of the input as cached, with passages 27%.

### Length of the header (local, no judge)

Each passage vectorized again with four headers, then the rank of the right passage for the 26 questions, searched with the
recent messages.

| Header | In the first 3 | First 5 | First 10 | Median rank |
|---|---|---|---|---|
| None | 11 | 16 | 20 | 4 |
| Scene cut at 200 characters (kept) | 13 | 15 | 19 | 3 |
| Scene cut at 600 | 11 | 17 | 20 | 4 |
| Whole scene | 9 | 16 | 20 | 4 |

One or two questions apart: no clear winner. Searching with the message alone did a little worse (first 5: 13 to 14).

### Questions in French

The same questions translated into French, headers cut at 200: 13 in the first 3, 16 in the first 5, 19 in the first 10,
median rank 3.5. In English: 13, 15, 19 and 3. The language of the question changed almost nothing for the search. The replies
and their grades were not measured in French.

The two local studies (headers, French) were made with throwaway scripts that are not in the repository.

## Three books and two models

The first measure used one book and one model. This one asks whether it holds elsewhere.

### Setup

- **Books:** the three shipped examples, in English, with the passages and `bge-m3` vectors they ship.
- **Questions:** 20 a book (`evals/alice/factual.json`, `evals/cyrano/factual.json`, `evals/bovary/factual-en.json`), one
  from each of 20 passages at evenly spaced places (for *Madame Bovary*, over the part where Emma lives). Written while reading
  those passages, before any search or reply, and not reviewed by a second person.
- **Characters:** DeepSeek V4.1 Flash and ERNIE 5.5 Flash (a free preview), default prompts (signature `f2eac269a13f`),
  recent messages searched. **One try** a question. **Judge:** `evals/accuracy-judge.json`, Claude Haiku 5.5.
- 360 replies, all graded. One series of DeepSeek was run again after a call stayed unanswered for several minutes.
- **Cost:** about 0.17 USD.

### Grades

| Model | Whole story | 5 passages | 3 passages |
|---|---|---|---|
| DeepSeek V4.1 Flash | 3.72 | 4.35 (+0.63, +0.21 to +1.06) | 4.30 (+0.58, +0.14 to +1.02) |
| ERNIE 5.5 Flash | 3.48 | 4.40 (+0.92, +0.43 to +1.40) | 4.28 (+0.80, +0.33 to +1.27) |
| Prompt, average tokens (DeepSeek, ERNIE) | 3,420, 3,385 | 4,009, 3,981 | 3,096, 3,073 |
| Reply, median time | 2.0 s, 2.2 s | 1.5 s, 1.9 s | 1.4 s, 1.9 s |

3 passages against 5, on the same questions: -0.05 (-0.28 to +0.18) with DeepSeek, -0.12 (-0.34 to +0.10) with ERNIE.

### Book by book

20 questions a book: a trend, not a measure.

| Book | Model | Story | 5 passages | 3 passages | Gain with 3 (95% interval) | Prompt: story, 3 passages |
|---|---|---|---|---|---|---|
| *Alice* | DeepSeek | 4.55 | 4.85 | 4.75 | +0.20 (-0.34 to +0.74) | 2,429, 3,059 |
| *Alice* | ERNIE | 4.25 | 4.95 | 4.95 | +0.70 (+0.17 to +1.23) | 2,396, 3,026 |
| *Cyrano* | DeepSeek | 3.50 | 3.95 | 3.85 | +0.35 (-0.67 to +1.37) | 3,218, 3,324 |
| *Cyrano* | ERNIE | 3.25 | 4.10 | 3.80 | +0.55 (-0.54 to +1.64) | 3,205, 3,301 |
| *Madame Bovary* | DeepSeek | 3.10 | 4.25 | 4.30 | +1.20 (+0.48 to +1.92) | 4,612, 2,906 |
| *Madame Bovary* | ERNIE | 2.95 | 4.15 | 4.10 | +1.15 (+0.31 to +1.99) | 4,553, 2,892 |

A short book has a short story: the passages then make the prompt longer.

### The search

| Setting | Right passage found, of 60 | Grade when found | When missed |
|---|---|---|---|
| DeepSeek, 5 passages | 48 | 4.65 | 3.17 |
| DeepSeek, 3 passages | 46 | 4.74 | 2.86 |
| ERNIE, 5 passages | 48 | 4.75 | 3.00 |
| ERNIE, 3 passages | 46 | 4.76 | 2.71 |

When the passage is missed, the passages can do worse than the story (asked what he planted as a flag, Cyrano gets 5 with the
story and 1 with passages, with DeepSeek). No reply graded 2 or less had received the expected passage.

### Wrong replies: making up or admitting

The 52 replies graded 2 or less, read and sorted by hand (replies graded 3 were not):

| | DeepSeek | ERNIE | Together |
|---|---|---|---|
| States something false with no doubt, sometimes denying what the question takes for granted | 21 | 22 | 43 |
| Voices a doubt, then makes something up | 2 | 5 | 7 |
| Says it does not remember | 1 | 1 | 2 |

The chat prompt tells the character "You know your whole story" and never what to do when it does not find the answer.

## Limits

- One famous book. DeepSeek knows it, and sometimes answered right without the right passage: a lesser-known book could show
  another difference.
- 26 questions written by reading the passages; they can favor what the search finds easily.
- The judge was not compared with human grades. One of its low grades was checked and was wrong, because of the question: the
  expected answer of the song held only its first lines, and a reply quoting the next ones got 1 (written search, 3 passages,
  undervalued by about 0.07). The expected answer was corrected **after** these runs.
- Role questions were only asked with 5 passages, and no local model was measured on the factual questions.
- The second measure asks about precise details, each held by one passage: where a summary is weak and a search strong. General
  questions about a book were not asked. Its questions share words with their passage, which helps the search.

## Ideas not tried

- Factual questions with a local model (Llama 3.2 3B).
- Scene summaries that keep notable objects, or finer scenes, for the questions that are always missed.
- Placing the passages at the end of the system prompt, so that a server can reuse the unchanged beginning.
- A lesser-known book.
- A rule in the chat prompt that lets the character say, in its own voice, that it does not remember, measured on the same
  questions with a grade that tells an admission from an invention. Tried since: [admitting not knowing](admitting-not-knowing.md).
