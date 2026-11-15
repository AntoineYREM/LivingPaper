# 0007. The characters know the story of the book, scene by scene

**Status:** accepted

## Context

A character was given its profile only, 150 to 300 words, in which the key events of its story had to fit. The notes the analysis
takes on each part of the book (including what happens to each character) were thrown away after the profiles were written.
Asked about a precise scene, the character improvised.

## Decision

- The characters **know the whole book**, from the beginning to the end, spoilers included: they talk from after the end, which is
  what the spoiler warning already says.
- The analysis lists the **scenes** of each part while it reads (what happens, and who is present), and writes **one story of the
  whole book, scene by scene**, shared by all the characters. Each scene says who is present, so a character speaks of a scene it
  lived as a memory, and of the others as something it learned.
- The story takes **800 to 3,000 words**, growing with the length of the book (the story is sent to the model with every message of a
  chat: it must stay affordable, also with a small local model). A model often writes more than it is told, so a story more than a
  tenth over the budget is asked again with a limit 20% tighter, up to twice, and the shortest is kept; it is never cut in the
  middle, which would lose the end of the book.
- The prompts ask for **concrete events** (who does what, what changes), not commentary, for **named characters** only in each
  scene, and for **the end of the book**, because a small model drifts to vague, thematic scenes and stops before the end.
- The **profile** keeps who the character is (personality, voice, relationships, what it knows): 200 to 400 words, without the
  events.
- A part that gave no scenes (the content filter refused it, only simple notes were possible, or the model forgot the scenes) is
  a **hole**: the story is written from the other parts and marked `storySummaryPartial`. With no scene at all there is no story and
  the characters chat as before.
- The story is stored in the file of the book (`storySummary`, `schemaVersion` 2). Version 1 files are still read.
- The story is given to the chat by its own prompt, `chat.story`, put in place of `{story}` in `chat.system`, so a book without a
  story has no empty section. Like every prompt, it is a text the user can edit.

## Consequences

- A chat message is longer (up to about 4,000 tokens more), so a bit slower and dearer.
- The examples analyzed before this decision have no story: they are archived (`examples/archive/`), and new ones are analyzed
  again with this version.
- A user who edited their prompts keeps them: the new prompts are added to `data/prompts.json`, the old ones are not overwritten,
  so they must copy the new `analysis.readPart`, `analysis.characterProfile` and `chat.system` from `prompts.default.json` to get the
  story.
- A mode "spoiler-free up to chapter N" stays possible later: each scene could carry its chapter.
