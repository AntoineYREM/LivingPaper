# Specification: passages of the book in the chat (RAG)

**Status:** validated on 26 November 2026. Implemented so far: the model for vectorization (section 3,
PAPER-81) and the cutting into passages (section 4, PAPER-82; nothing uses it yet). Why: [decision 0011](../decisions/0011-passages-of-the-book-in-the-chat.md).

## 1. Goal

Give a character the exact passages of the book that match what the user is talking about, with a shorter prompt than today.

Today the whole story, scene by scene, is sent with every message: about 7,000 to 7,850 tokens for *Madame Bovary*. That is
slow with a small local model, and the character still gets details wrong, because the story is a summary.

### Success criteria

Compared with the full story, on *Madame Bovary*:

1. A better average on the factual questions (section 11).
2. A system prompt of at most 3,500 tokens with 3 passages.
3. No drop of the role score. "Drop" means that the 95% interval of the paired difference lies entirely below 0.

### Out of scope

An external vector database, a "sources" panel in the chat, OCR, a spoiler-free mode, saved conversations.

## 2. Overview

The feature is **optional**. It is on for a book when an embedding model is chosen and the book is indexed. In every other
case the chat works as today, with the full story.

| | Full story (today, and the fallback) | With passages |
|---|---|---|
| Profile and rules | yes | yes |
| Story scene by scene | yes | no |
| Short plot summary | no | yes |
| Passages matching the conversation | no | yes (3 by default) |

## 3. Settings

- Models are added as today. A third list, **Vectorization**, sits next to Analysis and Chat. Its default is **None**.
- Any OpenAI-compatible `POST /embeddings` endpoint can be used (local or online). A multilingual model is recommended in the
  documentation, since the user may write in another language than the book.
- A deleted model that was chosen is unselected.
- Choosing another model for vectorization sends one test embedding. A model that cannot answer is refused: the previous
  vectorization choice stays, and the analysis and chat choices of the same form are saved.
- A provider with no model list: the chat test runs first, then, if it fails, the embedding test. One success is enough to
  save the model.
- A model that is added **fills the choices that have no model**, including one the user set to None. A choice already made is
  never replaced, and deleting a model leaves its choices empty. While a choice is empty, the new model is tried to know which
  it can fill:

  | Result of the try | The new model fills, where empty |
  |---|---|
  | It answers the chat test | Analysis and chat |
  | The chat test is **refused** (an HTTP refusal) and the embedding test succeeds | Vectorization only |
  | Anything else (too slow, unreachable, failing provider) | Analysis and chat |

  A slow model is thus never taken for an embedding model: some chat models can also vectorize. A model of a provider with a
  list is saved whatever the try says, since the list proved the key.

## 4. Cutting the book into passages

- Target about **1,500 characters**, never more than **2,000**, the repeated sentence included.
- Cut at the end of a **paragraph**, else of a **sentence**, else between two **words** (a text with none of these, over
  thousands of characters, is cut anywhere, never inside a character). A PDF has lost its paragraphs at extraction, so it is
  cut between sentences. The point of a title or of an initial ("M. Bovary") does not end a sentence.
- A paragraph is a real one: in an EPUB, a line break of the file itself is read as a space, as a browser does (many books
  wrap their pages every 70 characters or so). This also changes the text the analysis reads, not its words.
- Each passage starts again with the **last sentence of the previous one**; a sentence longer than **200 characters** is not
  repeated whole, only its last words.
- Each part of the analysis is cut on its own: a passage never straddles two parts. The sentence repeated crosses the limit.
- Passages are numbered from the start of the book. The cut is deterministic: a retried analysis finds the same passages.
- Boilerplate removed from the analysis (`BookBoilerplate`) is not cut into passages.

## 5. Analysis

### Which passages belong to which scene

- The analysis model receives the text with numbered markers (`[P112]`), without the repeated sentences.
- For each scene, the prompt `analysis.readPart` asks for the first and last passage it covers, besides `summary` and `present`.
  No extra call is made.
- Everything the model answers is checked: a number outside the part, or a reversed range, is ignored.
- The limit of **12 scenes per part** is kept, so a header covers about 5 passages. The factual evaluation tells whether finer
  scenes are needed.
- A passage that no valid scene covers (wrong numbers, simple notes, a prompt edited by the user) has no header:
  it is still searched, and its presence is "unknown".
- Markers are added only when a vectorization model is chosen.

### Short plot summary

- A new prompt writes the plot in **300 to 500 words**, ending included, in English. It condenses the **finished story,
  scene by scene**, so it is written after it.
- It is written at **every analysis**, with or without an embedding model.
- It is stored in the book file (new field, `schemaVersion` 4; versions 1 to 3 are still read).
- The full story is still written and stored: it is the fallback.

### A part that fails

As today, a part refused by a content filter, read with simple notes only, or answered without scenes **does not block the
book**, with or without a vectorization model: the story is marked partial. The passages of that part are still cut from the
text and indexed, without header.

This holds even when **no** part gives a scene: the book is usable, with no story and no short summary. With passages, the
chat then sends the profile and the passages found, none of them with a header. If the search fails, it sends the profile
alone, as today for such a book. To get a story, the book is sent again with "Replace".

### Prompts

New and changed prompts live in `prompts.json` like the others. The hash of each previous default is added to
`ReplacedDefaults`; a prompt the user edited is not overwritten.

## 6. Index

### Header

Each passage that has a scene gets a header, and **header and text are vectorized together**:

```text
[Scene: At the ball, Emma accepts a waltz from the Viscount. | Present: Emma Bovary, Charles Bovary, Viscount]
...text of the passage...
```

The scene text of a header is cut to 200 characters. A passage covering several scenes lists them all.

### Storage

- **SQLite** embedded in the application, searched with the **sqlite-vec** extension. No service to install.
- Packages: `Microsoft.Data.Sqlite` 11.0.0 and `sqlite-vec` 0.1.7-alpha.2.1 (the NuGet package, a prerelease).
- Vectors are stored in an **ordinary table** and compared with `vec_distance_cosine`, so any size of vector fits. The
  virtual table of sqlite-vec is not used: its size is fixed when it is created, and it changes with the model.
- One file, `data/rag.db`, holding the passages (text, number, scenes, characters present), their vectors, and
  for each book the embedding model that made them.
- Book files stay the source of truth for everything else.
- If SQLite or the extension cannot be loaded, the feature is off, the settings page says so, and the chat uses the full story.

### When passages are kept

| Situation | Result |
|---|---|
| No vectorization model when analyzing | No text is kept, as today |
| Model chosen, indexing succeeds | Passages and vectors kept |
| Model chosen, indexing fails | The analysis succeeds, passages are kept, the book offers **Retry indexing** |

Passages are saved before the text of the book is deleted. Indexing is a step of the analysis, shown in its progress, and it
keeps the heartbeat that detects an interrupted analysis. The embedding tokens are shown with the other tokens.

### Life of an index

- Deleting a book deletes its passages and vectors. Replacing a book replaces them.
- Changing the embedding model marks indexed books **to be re-indexed**. They use the full story until the user presses
  **Re-index**, which computes new vectors from the kept passages. Nothing is recomputed automatically.
- Books analyzed before this feature have no passages: they must be **sent again** with "Replace".

## 7. Chat

For each message:

1. Build the search query (see the modes below).
2. Embed it and take the closest passages of the book.
3. Mark each passage from the character's point of view: **lived** (present in the scene), **learned later**, or unknown.
   All passages can be returned: the character knows the whole book.
4. Render the system prompt: profile, rules, short plot summary, passages. The character uses them without reciting them.

### Context of the model

The chat with passages is designed for a model with a context of **at least 8,192 tokens**: a system prompt of at most 3,500
tokens, and the rest for the conversation (still up to 20 messages) and the reply. The documentation says so, and that some
local servers start with less (Ollama uses 4,096 by default on a 4 GB graphics card).

### Query modes and settings

| Mode | Query |
|---|---|
| `rewrite` (default) | The **chat model** writes a standalone search query from the recent messages (prompt in `prompts.json`) |
| `recent` | The user's message and the previous ones (3 previous) |
| `last` | The user's message only |

Environment variables `RAG_QUERY_MODE` (default `rewrite`) and `RAG_PASSAGES` (default `3`, 1 to 10). No setting
in the interface.

### Failures

| Failure | Behavior |
|---|---|
| Rewriting fails or times out | Search with the `recent` mode |
| Embedding or search fails | Send the full story |

Both show a **short notice** in the chat (translated, no technical detail, no upstream error body). A book that
is not indexed, or to be re-indexed, shows its state on its page, with no notice in the chat.

The rewriting adds one model call before the first word of each reply.

## 8. Export and import

### What a file holds

- An export **always** holds the passages and their vectors **when the book has them**. There is no box to tick. A book with
  passages and no vectors (indexing failed) exports its passages.
- Everything travels in the same JSON file: passages with their scenes and characters, the vectors, and once for the book the
  name of the embedding model and the size of its vectors. A vector is written as base64 of 32-bit floats (little-endian),
  about three times smaller than a list of numbers.
- A warning next to the export button of such a book says that the file holds the text of the book, which may be protected
  by copyright.
- Size limit of a file with passages: **30 MB**. Everything except passages and vectors stays within the present 1 MB limit.
  A book over the limit is exported without its vectors, with a message.

### Reusing imported vectors

A vector is only usable with the model that made it. Imported vectors are reused when **all** of these hold:

1. the model chosen for vectorization has the same name (a `:latest` suffix is ignored);
2. the vectors have the same size;
3. a test passage, embedded again, gives nearly the same vector (cosine similarity of at least 0.99).

If one fails, the book is **to be re-indexed**: new vectors are computed from the passages when the user asks. An import keeps
passages and vectors even without an embedding model; the check then runs when the user presses **Index**.

### Untrusted input

Passages and vectors of an imported file are validated before accepting: number and length of the passages, numbering,
names, number and size of the vectors, finite values, and total size.

## 9. Examples

- The maintainer analyzes the examples again and exports them **with passages and vectors**, made with **`bge-m3`**
  (multilingual, free to run locally, 1,024 dimensions).
- Only public-domain books **and public-domain translations** can be shipped this way. The files grow to a few megabytes each.
- An example with passages shows an **Index** button when an embedding model is chosen. It reuses the shipped vectors when
  they match (section 8), so a user of `bge-m3` computes nothing; otherwise it computes new ones. Until then, the chat uses the
  full story.
- The examples stay read-only. Their index is kept apart from a library book with the same address.

## 10. Observability

- Langfuse traces show the mode, the rewritten query, and the numbers and distances of the passages found.
- The text of the query and of the passages is sent only with `LANGFUSE_CAPTURE_CONTENT=true`.
- Evaluation run files keep the same information.
- Book text, queries and messages are never written to logs.

## 11. Evaluation

The present judge sees only the question and the reply, so it cannot detect an invented fact.

- A new set of **factual questions** on *Madame Bovary*, each with its expected answer (at least 20).
- A second judge prompt grades **accuracy** against the expected answer.
- The tool also reports whether the expected passage was **retrieved**, which separates a search failure from an answer
  failure.
- New options choose the embedding model, the query mode and the number of passages, and one option runs the full story, so
  that `compare` can set them side by side.

## 12. Technical shape

- `Application`: passage cutting, retrieval and prompt assembly; ports `IEmbeddingModel` and `IPassageIndex`.
- `Infrastructure`: the OpenAI-compatible embeddings adapter, with the same retries, timeouts and URL rules as the chat.
- `Persistence`: the SQLite adapter.
- New dependencies (SQLite provider, sqlite-vec) must be documented and pass the license check. The check needs a
  hand-verified exception for the `SQLite` package (public domain, declared by a license file).
- Verified before building on them (PAPER-80): Windows x64, and Linux amd64 and arm64 in the runtime image of the application.
  The `sqlite-vec` package has binaries for `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64` only: elsewhere
  the feature is off. Publishing keeps only the native files of the platforms that are shipped (all platforms add about 52 MB).
- Tests use a fake embedding model: no real provider. They cover nominal cases, invalid model answers, empty results,
  transient errors and every fallback. One browser scenario adds a book, indexes it and chats.
- Every new string goes to `en.json` and `fr.json`.
- `AGENTS.md`, the README, the architecture and run guides, and the changelog are updated: "the book text is never stored" is
  no longer true when this feature is on.

## 13. Delivery

Jira epic **PAPER-79**, one ticket per step.

| # | Step | Ticket |
|---|---|---|
| 1 | Check SQLite and sqlite-vec on Windows and Docker | PAPER-80 |
| 2 | Embedding port and adapter, "Vectorization" choice in the settings | PAPER-81 |
| 3 | Cutting into passages | PAPER-82 |
| 4 | SQLite passage index | PAPER-83 |
| 5 | Analysis: markers, passages of each scene, short summary, `schemaVersion` 4 | PAPER-84 |
| 6 | Indexing: progress, retry, re-index, deletion | PAPER-85 |
| 7 | Chat: query modes, retrieval, prompt, fallback and notice | PAPER-86 |
| 8 | Factual evaluation and new options of the tool | PAPER-87 |
| 9 | Export and import with passages and vectors, "Index" button of the examples | PAPER-88 |
| 10 | Examples analyzed again, documentation, release | PAPER-89 |

Step 8 comes before the export so that the benefit is measured as early as possible.
