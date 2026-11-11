# LivingPaper

> Chat with the characters of your books.

LivingPaper is an open-source web app. You add a book (EPUB or PDF), an AI reads it and extracts the main characters
with a personality profile, then you can chat with a character as if texting them. You bring your own API key for
any OpenAI-compatible provider.

**Status:** version 0 is under construction. This README is a stub and grows with the project.

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
