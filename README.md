# LivingPaper

> Chat with the characters of your books.

[![CI](https://github.com/AntoineYREM/LivingPaper/actions/workflows/ci.yml/badge.svg)](https://github.com/AntoineYREM/LivingPaper/actions/workflows/ci.yml)
[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

LivingPaper is an open-source web app. You add a book (EPUB or PDF), an AI reads it and extracts the main characters with a
personality profile, then you chat with a character as if texting them. It is **self-hosted** and for one person: you bring your
own API key for any OpenAI-compatible provider (OpenAI, another vendor, or a model running on your computer).

## What it does

- **Add a book** (EPUB or PDF): it is analyzed in the background, with progress, a retry that does not pay twice, and the tokens
  used shown at the end.
- **Get the 10 main characters**, each with a short description and a personality profile that you can edit.
- **Chat with a character**: first person, short text-message replies, streamed, in the language you write in. Nothing is stored.
- **Try it without a key**: the **Examples** page has public-domain books that are already analyzed (*Le Malade imaginaire*,
  *L'Avare*, *Don Juan*); a model is only needed to chat.
- **Export and import** a book as a JSON file, to move it or share it.
- **Read and edit every prompt** in `data/prompts.json`.
- **English and French** interface.
- **Your data stays with you**: plain JSON files in a folder, API keys encrypted at rest, book text deleted after the analysis.

## Quick start (Docker)

You need Docker with Compose.

```bash
git clone https://github.com/AntoineYREM/LivingPaper.git
cd LivingPaper
docker compose up --build
```

Then open <http://localhost:8080>:

1. Open **Settings** and add a language model (name, base URL, API key, model name). A connection test runs before it is saved.
   Choose it for the analysis and for the chat.
2. On the home page, **Add a book**. The analysis starts by itself; the list shows where it is.
3. Open the book, pick a character and press **Chat**.

Your data is kept in the Docker volume `livingpaper-data` and survives `docker compose down` (not `down -v`). To use the
published image instead of building it, run from source, change a setting or reach a model on your computer (Ollama, LM Studio), see [docs/how-to-run.md](docs/how-to-run.md).

### Use a model running on your computer

With Ollama, add a model in **Settings** with the base URL `http://host.docker.internal:11434/v1` (from Docker; `localhost` would
be the container itself), any text as the key, and the name of a model you pulled. Private addresses are allowed on purpose so that
local models work. Details and LM Studio: [docs/how-to-run.md](docs/how-to-run.md#add-a-language-model).

## Warnings

- **LivingPaper has no authentication.** Anyone who can reach it can use the API keys you saved. It is made for personal use on
  your own computer or network. **Do not expose it to the Internet** without a reverse proxy that requires a sign-in. Docker
  publishes the port on `127.0.0.1` only, on purpose. See [SECURITY.md](SECURITY.md).
- **Spoilers.** Character sheets and the chat cover the **whole book**: a character may reveal the ending.
- **Costs.** Analyzing a book calls your provider many times (one play, with a small model, used about 110,000 input and 75,000 output
  tokens). The tokens used are shown at the end of each analysis. A local model costs nothing.
- **Copyright.** Add books you have the right to read and analyze. The examples are public-domain works only. The text of a book
  is sent to the provider you chose, and nowhere else.

## Documentation

| | |
|---|---|
| [docs/how-to-run.md](docs/how-to-run.md) | Docker, from source, configuration, local models, backup, troubleshooting |
| [docs/how-to-test.md](docs/how-to-test.md) | Run and write tests, reproduce the CI |
| [docs/architecture.md](docs/architecture.md) | The layers, the ports, the main flows |
| [docs/decisions](docs/decisions/README.md) | Why the project is the way it is (ADRs) |
| [ROADMAP.md](ROADMAP.md) | What exists, what may come |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute (fork and pull request, CLA, AI-assisted contributions) |
| [CHANGELOG.md](CHANGELOG.md) | What changed in each version |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | Dependencies and their licenses |

## Contributing

Contributions are welcome: read [CONTRIBUTING.md](CONTRIBUTING.md). Every external contribution needs a signed
[CLA](CLA.md) (you keep the ownership of your work). Everyone is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
Report a security problem as said in [SECURITY.md](SECURITY.md).

## License

LivingPaper is free software under the **GNU Affero General Public License v3.0** ([LICENSE](LICENSE)): you can use, study, share
and modify it, and if you run a modified version for other people over a network you must publish your changes. Copyright (C) 2026
AntoineYREM. The analyses of the `examples/` folder are under CC0 ([examples/LICENSE](examples/LICENSE)). The licenses of the
dependencies, all compatible with the AGPL, are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

# Reference

The sections below describe each part of the application in detail.

## The prompts

Every prompt sent to the AI lives in a file you can read and edit: `prompts.json` in the data directory (`./data` by
default, the `data/` volume in Docker; set `DATA_DIRECTORY` to move it). Nothing is written in the code.

- **Where:** `data/prompts.json`, created in full the first time the application starts.
- **How to edit:** open it in any text editor. Each prompt has a `description` (what it is for and which variables it
  accepts) and a `template` (the text sent to the AI, with `\n` for a new line). A variable is written between braces,
  like `{bookTitle}`. Anything else between braces, such as a JSON example, is plain text.
- **When it applies:** prompts are read again at every analysis and every chat message. No restart is needed.
- **Number of characters:** the analysis keeps 10 characters. To change that, write another number in place of
  `{maxCharacters}` in the `analysis.mergeCharacters` prompt.
- **If the file is wrong:** invalid JSON, a missing prompt, an empty prompt or an unknown variable stops the analysis
  or the chat with an error that names the file and the prompt. There is no silent fallback to the default prompts,
  and the file is never overwritten. The same error is written to the logs at startup.
- **After an update:** prompts that the new version adds are appended to your file. A prompt that is already in it,
  edited or not, is never changed.
- **Back to the defaults:** delete `data/prompts.json` and restart the application. The default prompts are copied
  again.

The default prompts and the exact variables of each one are in
[`src/LivingPaper.Persistence/prompts.default.json`](src/LivingPaper.Persistence/prompts.default.json).

Prompts are written in English: everything the analysis writes (book summary, character descriptions and profiles) is
in English whatever the language of the book, and the chat prompt tells the AI to answer in the language you write in.

## Language models and your API keys

You bring your own language model: any OpenAI-compatible API (OpenAI, a local server such as Ollama or LM Studio,
another vendor). Each model is a name, a base URL, an API key (optional for a local server) and a model name. A model
is saved only after a connection test succeeds: the application sends the `llm.connectionTest` prompt of
`data/prompts.json` and checks that a usable reply comes back. You choose one model to analyze books and one to chat;
they can be the same.

- **Where:** `data/settings.json` holds the models and your choices. `data/keys/` holds the keys that encrypt your API
  keys. Both are in the data directory (`./data` by default, the `data/` volume in Docker) and both are ignored by git.
- **What is protected:** an API key is encrypted before it is written, is never sent back to your browser, never
  written to the logs and never part of a book export.
- **What is not:** in a container on Linux, the encryption keys in `data/keys/` are stored as plain files next to
  `settings.json`. The encryption therefore protects a `settings.json` that leaks on its own (shared, copied, partly
  backed up). It does not protect against someone who gets the whole data volume.
- **Back up both together.** If `data/keys/` is lost, the saved API keys can no longer be read: the settings say so
  and you add those models again.
- **Local addresses are allowed on purpose** (`localhost`, `192.168.x.x`, `host.docker.internal`) so that local models
  work, and the application follows no redirect.

**Warning: LivingPaper is for personal use and has no authentication.** Anyone who can reach the application can use
the models and keys you saved. Do not expose it to the Internet without a reverse proxy that protects it.

## Your books

The home page is **My library**: your books, set like the contents of a book, each with its status. **Add a book** sends an
EPUB or a PDF (20 MB at most) and takes you to the page of the book; you can delete a book from the library.

- **The format is checked on the content of the file**, not on its name: a text file renamed `.epub` is refused.
- **A book is read as soon as you send it**, so a problem shows at once. A PDF made of pictures of pages (a scan, with
  almost no text) is refused: use the EPUB of the same book. Damaged, encrypted or protected files are refused too. An
  EPUB that unpacks to far more than a book can hold (a "zip bomb") is refused.
- **The title** is the one you type, else the one written in the file, else the name of the file. A second book with
  the same title is refused unless you tick "Replace", which deletes the earlier book and its analysis.
- **Where it goes:** `data/books/{id}.json` holds the book (title, status, and later its summary and characters), and
  `data/uploads/{id}.txt` holds its text until the analysis has finished successfully, then it is deleted. The text of a
  book is never in the book's JSON file, never in an export and never in the logs. Deleting a book deletes all of it.
  Re-analyzing a book (another model, an edited prompt) means sending it again and ticking "Replace".

PDF files are read with [PdfPig](https://github.com/UglyToad/PdfPig) (Apache License 2.0, compatible with the AGPL).

## The analysis

As soon as a book is sent, it is analyzed in the background, one book at a time, so you can leave the page. The list of
the books shows where it is ("3 of 12 parts read", then "4 of 10 profiles written") and refreshes by itself (a small
script; without it, reload the page). The language model used is the one you chose for analysis in the settings.

1. **The book is read in parts** of about 40 pages (a PDF by real pages, an EPUB by about 2,000 letters a page), and the
   model notes the characters of each part.
2. **The notes are merged**: a character called by several names becomes one, and the 10 most important are kept.
   A very long book never overflows the model: past about 150,000 letters of notes, each part keeps fewer characters.
3. **One profile per character** and **a short summary of the atmosphere** (era, place, tone) are written.

Everything the analysis writes is in English, whatever the language of the book. The prompts are in `data/prompts.json`.

- **If it fails** (provider down, wrong key, unreadable replies), the book says why and offers **Retry**: every step that
  succeeded is saved in `data/books/{id}.steps.json`, so the retry resumes where it stopped and does not pay for the
  steps already done. A reply that is not the JSON asked for is asked again twice, then the analysis stops with a clear
  error: nothing is invented to hide it.
- **A restart** loses nothing: the books that were waiting start again by themselves, and a book that was running is shown
  as interrupted, with Retry. A running analysis that has not moved for 10 minutes is shown as interrupted too.
- **The content filter** of some providers refuses passages of dark books. That part is read again with simpler notes; if
  it is refused again it is left out, and the book says how many parts were.
- **At the end** the page shows the number of characters, the model, the duration and the tokens used. The steps and the
  stored text of the book are then deleted.
- **Two settings** for the environment: `ANALYSIS_PAGES_PER_PART` (default 40; lower it for a model with a small context
  window) and `ANALYSIS_PARALLEL_CALLS` (default 3; set 1 for a small local server or a provider with strict limits).
  A value that is not a whole number in range stops the application at start.

## The page of a book and the sheets of its characters

The page of a book (`/Book/{id}`) shows, once the analysis is done, the summary of its atmosphere, a warning that characters
know the whole book (**spoilers**), and the characters, the most important first. While the analysis runs it shows the
progress and, with JavaScript, opens on the characters by itself when it ends; a failed or interrupted analysis shows why,
with **Retry**.

Each character has a **sheet**: a short description and a personality profile, which you can **edit** and save. Name, short
description and profile are validated on the server (100, 600 and 3,000 characters at most; a name must differ from the other
characters of the book). The chat always uses the texts saved here, so a sheet you correct is the sheet the character plays.
A sheet can only be edited once the analysis of its book is done.

Everything works without JavaScript except the chat; the script only refreshes the progress.

## The chat

From a character's sheet or card, **Chat with ...** opens a conversation with the character, played by the language model you
chose for the chat in the settings. The character speaks in the first person, in short messages like text messages (no
described gestures, no formatting), stays in its role, never says it is an AI, and **answers in the language of your last
message**. The reply is shown as it is written. If the model fails, the error is shown in the window and your message goes back
into the field.

- **Nothing is stored.** The conversation lives in the page and disappears when you leave it. The browser sends the whole
  conversation with each message; only the last 20 messages go to the model.
- **Limits**, checked on the server (a request that breaks them is refused with 400): at most 100 messages, 4,000 characters a
  message, roles `user` and `assistant` only, the last message being yours.
- **The prompt** is `chat.system` in `data/prompts.json` (in English, with the book, the name and the profile of the character
  as saved on its sheet). The text of the messages and of the replies is never written to the logs.
- **The chat needs JavaScript** (the reply streams in as NDJSON lines through `fetch`); without it the chat buttons are hidden.
  With no model chosen for the chat, the page says so and links to the settings.

## The examples

The **Examples** page shows public-domain books that are already analyzed, so you can try LivingPaper without a key: read the
summary and the sheets, and chat with the characters once a model is chosen for the chat. Examples are read only: they cannot be
edited, retried or deleted. **Add to my library** copies one into your library (a book titled the same as one you already have is
refused), where you can edit its sheets like any other book.

They are the files of the `examples/` folder, in the format of a book of the library, checked like an imported book when the
application starts: an example that is not valid is ignored, with a warning in the logs. `EXAMPLES_DIRECTORY` points to another
folder. See `CONTRIBUTING.md` to propose one.

## Exporting and importing a book

**Export** (on the page of an analyzed book, and in the library) downloads the book as a `.json` file: its title, its summary,
its characters and their sheets (the edits you made included), and how it was analyzed (model, tokens, dates). It is the same
format as the files of `data/books/` and of the examples. It holds no API key, no setting, no path of your machine and not the
text of the book: the file is built field by field from the book, so nothing else can get in.

**Import** (`/Import`, from the library) adds an analyzed book from such a file, on this installation or another one. The file is
untrusted input and is checked before anything is kept: at most 1 MB, valid JSON of the shape of a book, a known `schemaVersion`
(a file from a newer LivingPaper is refused with its own message), required fields, numbers in range, texts no longer than the
limits of the analysis, sheets that follow the rules of a sheet (names distinct, no control characters). Only a book whose
analysis is done, with at least one character, can be imported, and nothing is analyzed.

- **The identifier of the file is kept** when it is free, so importing a file again updates the same book. If a book of your
  library already has that identifier or the same title, the import is refused with an explanation; **tick "Replace" and choose
  the file again** to confirm: the old book, its analysis and its edited sheets are then deleted. A file whose identifier is that of
  an example gets a new one, so the example stays where it is.
- A file you export and import again gives the same book.

