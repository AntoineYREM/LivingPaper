# 0001. JSON files instead of a database

**Status:** accepted

## Context

LivingPaper is used by one person, self-hosted. The data is small: a few books, each with a summary and ten characters.
Users must be able to export, import, read and share an analysis. `docker compose up` must be enough to run the application,
with no external service.

## Decision

No database. Each book is **one JSON file** (`data/books/{id}.json`) with a `schemaVersion`. Settings and prompts are JSON files
too. Writes are atomic (a temporary file, then a rename) and serialized per path. Storage sits behind ports (`IBookRepository`
and others), and a file that cannot be read is reported without breaking the list of the others.

The text of a book is **never** stored in its JSON file: it is kept apart until the analysis succeeds, then deleted.

## Consequences

- An export is the file itself, and the shipped examples are files of the same format: one format for everything.
- Backups are a copy of a directory. Nothing to install, migrate or secure besides the files.
- There are no queries, no transactions across files, and one process is assumed to own the directory. This is fine for one
  user; it is not a basis for a multi-user service.
- A file of a newer version is refused with its own message, an older one is read by the current code: `schemaVersion` leaves
  room for migrations.
- Another backend can be written later behind the same ports without touching the use cases.
