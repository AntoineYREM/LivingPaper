# 0011. Passages of the book in the chat, found by vector search

**Status:** accepted

## Context

A character receives the whole story, scene by scene, with every message ([0007](0007-story-of-the-book-scene-by-scene.md)):
about 7,000 tokens for a long novel. A small local model reads it slowly, and the details are missing: the story is a summary,
and the text of the book is deleted after the analysis ([0001](0001-json-files-instead-of-a-database.md)).

## Decision

- An **optional** retrieval of passages of the book. With it, the prompt holds a short plot summary and the few passages that
  match the conversation. Without it, or when it fails, the chat sends the full story as before.
- It is on only when the user chooses an **embedding model**, from any OpenAI-compatible provider.
- The book is cut into passages of about 1,500 characters. The analysis says which passages each scene covers, so a passage
  carries who was there, and this header is vectorized with the text.
- Passages and vectors live in an embedded **SQLite** database searched with **sqlite-vec**. No service is added.
- The **text of the book is then kept** on the user's machine. An export always includes the passages and their vectors when
  the book has them; vectors are reused on import only when the embedding model matches.

The details are in the [specification](../specs/rag.md).

## Consequences

- Replaces, when the feature is on, the rule of 0001 that the text of a book is never kept, and adds a database beside the
  JSON files. Book files remain the format of exports and examples.
- Completes 0007: the full story becomes the fallback.
- Every export of an indexed book contains its text, which may be protected by copyright, and the file is several times
  bigger (up to 30 MB).
- Two native dependencies to follow, one of them young.
- Books analyzed earlier must be sent again to use the feature.
- One more model call per message when the query is rewritten, and a re-indexing when the embedding model changes.
