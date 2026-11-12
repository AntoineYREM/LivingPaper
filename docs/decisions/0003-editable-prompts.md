# 0003. Prompts live in a file the user can edit

**Status:** accepted

## Context

The quality of the characters depends on the prompts, and it depends on the model and on the book. A user who is not a developer
should be able to see what is sent to the AI and to improve it, with no rebuild.

## Decision

**No prompt is written in the code.** Every prompt (read a part, merge characters, character profile, book summary, chat,
connection test) is in `data/prompts.json`, copied in full from `prompts.default.json` the first time the application starts.
Each prompt has a `description` (what it is for and its variables) and a `template` with `{name}` placeholders.

- Prompts are read again at every analysis and every chat message: no restart.
- On an update, prompts that are new are appended; one that exists is **never** overwritten, edited or not.
- An invalid file, a missing or empty prompt or an unknown variable is an **explicit error** that names the file and the prompt.
  There is no silent fallback to the defaults, which would hide a mistake and spend the user's money on something they did not
  write.
- The number of characters (10) is the `{maxCharacters}` variable of the merge prompt, not a setting.

## Consequences

- Users can tune, translate or replace the behaviour, and a contribution to the defaults is a change to one JSON file.
- A user who breaks their file gets a clear error and a way back: delete the file and restart.
- Prompts are part of the behaviour that the tests check through the variables each one accepts.
