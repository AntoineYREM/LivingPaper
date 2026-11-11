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
