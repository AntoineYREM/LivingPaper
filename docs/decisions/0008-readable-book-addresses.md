# 0008. The address of a book is the slug of its title

**Status:** accepted

## Context

A book was at `/books/8ca7c5ebd8a0447da89abd51d80486d1`: 32 random characters, impossible to read, to remember or to say. The same
identifier was the name of the book's file (`data/books/{id}.json`), so it also had to be impossible to turn into a path.

## Decision

- The identifier of a book is the **slug of its title**: `Cyrano de Bergerac` is `cyrano-de-bergerac`, at `/books/cyrano-de-bergerac`.
  Accents are removed, everything else becomes a single hyphen, 60 characters at most for the title part.
- The only accepted form stays strict because the identifier still comes from a URL or an import and is a file name: lowercase
  letters and digits separated by single hyphens, 80 characters at most. No dot, no slash, no capital.
- Two books never share an address: a number is added (`cyrano-de-bergerac-2`) when a book of the library or an **example** already
  has it, or when an unreadable file holds the name. A book that is replaced (the "Replace" box) keeps its address.
- The examples have their slug as `id`, so the shipped examples are at `/books/cyrano-de-bergerac` and so on. A copy of an example in
  the library is `cyrano-de-bergerac-2`.
- **`schemaVersion` 3.** Files of version 1 and 2 are still read, and the old 32-character form is still a valid identifier. An
  imported file that has an old identifier gets the slug of its title; a file whose address is free keeps it, so importing a file again
  updates the same book.
- **Migration at start** (`BookAddressMigration`): each book of the library with an old identifier is renamed, with its steps file
  and its waiting text. It is safe to run again.
- **The old addresses are not kept**: no redirect from `/books/{old id}`. The application is for one person and the addresses were
  hard to share anyway; the cost of a redirect table was not worth it.

## Consequences

- A rename of the data on the first start of 0.3.0: **back up the `data/` volume before updating**. Going back to 0.2.0 afterwards is
  not possible without that backup.
- A stray file such as `notes.json` in `data/books/` is now listed as unreadable, because its name could be an address (before, it
  was ignored).
- Changing the title of a book does not change its address (a title cannot be edited today).
