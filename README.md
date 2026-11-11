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
