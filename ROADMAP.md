# Roadmap

What exists, what is next, and what is not planned. Open tasks are in the GitHub issues (`good first issue` and
`help wanted` mark the ones open to contributions).

## Version 0 (current)

- Add an LLM (any OpenAI-compatible API): get the models of the provider, choose one in the list; choose one for the analysis and one for the chat.
- Add a book (EPUB or PDF); background analysis: summary and the 10 main characters with a personality profile.
- My library, editable sheets, export and import of a book as a JSON file.
- Examples: public-domain books already analyzed, to try the application without a key.
- Chat with a character, streamed, in the language of the user.
- Interface in English and French.
- Docker image and `docker compose up`.

## Possible next steps

Nothing here is promised or dated; it is what the project thinks about.

- **Spoiler-free mode**: characters and chat that know the book only up to a chapter.
- **Saved conversations** (today a conversation disappears when the page closes).
- **Scanned PDFs** (OCR).
- **In-app reading** of the book next to the chat.
- **Portraits** of the characters, a group image.
- **More languages** for the interface (a translation is one JSON file, see `CONTRIBUTING.md`).
- **More examples** (public-domain books, see `CONTRIBUTING.md`).
- **Protection for a shared installation**: a site password or accounts, a daily budget. The reason for waiting is in
  [docs/decisions/0004-no-authentication-in-v0.md](docs/decisions/0004-no-authentication-in-v0.md).

## Not planned

- A hosted service run by the project. LivingPaper is self-hosted: you bring your own key.
- Storing books or their text on a server other than yours.
