# LivingPaper

> Chat with the characters of your books.

LivingPaper is an open-source web app. The user adds a book (EPUB or PDF), an AI reads it and extracts the main
characters with a personality profile, then the user can **chat with a character** as if texting them.

The user brings their own OpenAI API key. Single-user, self-hosted, no accounts.

This file is for AI coding agents (and humans) working on the repository. Keep it short, factual and free of
personal data, secrets or private URLs.

## Status

Version 0 is under construction. Commands below marked *(planned)* do not exist yet; update this file as the
project grows.

## V0 scope

- Settings page: the user adds and deletes LLMs (any OpenAI-compatible API: base URL + API key + model).
  A connection test must succeed before an LLM is saved. Keys are encrypted at rest and never sent back to the
  browser. This page is also where the user chooses which LLM is used for book analysis and which one for chat.
  A deleted LLM that was selected must be unselected, and analysis/chat stay disabled until another is chosen.
- Books and analyses are stored as JSON files: easy to export and import.
- Add a book (EPUB or PDF) and delete a book. The book file is deleted after a successful analysis, so
  re-analyzing (other LLM, edited prompt) means uploading the book again and ticking "Replace".
- Background analysis: book summary (era, place, tone) and the 10 most important characters, each with a name,
  a short description and a personality profile.
- "My library" page (books the user analyzed) and "Examples" page (read-only, public-domain books shipped
  already analyzed so the app can be tried without a key).
- Chat with a character: first person, short SMS-like replies, streamed. Conversations are not stored.
- UI available in English and French from the start (JSON translation files).

**Out of V0** (planned for later): portraits, group image, video, daily budget, user accounts or site password, in-app reading,
OCR for scanned PDFs, saved conversations.

## Stack and architecture

- **Backend:** ASP.NET Core on .NET 10 LTS, no database: data is stored as **JSON files** in a `data/` directory (Docker volume).
- **Frontend:** Razor Pages rendered server-side, HTML/CSS and small vanilla JavaScript. Page models call the
  `Application` layer directly. The chat streams replies as NDJSON over `fetch` (POST). Everything except
  the chat works without JavaScript.
- **Architecture:** hexagonal (ports and adapters), modular monolith.

  ```text
  LivingPaper.sln
  src/
    LivingPaper.Domain/          business rules, no dependency
    LivingPaper.Application/     use cases and ports (interfaces)
    LivingPaper.Infrastructure/  adapters: OpenAI-compatible client, EPUB/PDF parsing, encryption
    LivingPaper.Persistence/     JSON file storage (adapters)
    LivingPaper.Web/             Razor Pages, HTTP endpoints, composition root
  tests/
    LivingPaper.Domain.Tests/
    LivingPaper.Application.Tests/
    LivingPaper.Infrastructure.Tests/
    LivingPaper.Persistence.Tests/
    LivingPaper.Web.IntegrationTests/
  ```

  Allowed dependencies: `Application` -> `Domain`; `Infrastructure` and `Persistence` -> `Application` and
  `Domain`; `Web` -> all layers (composition only). Razor views contain no data-access code.
- **Background work:** analysis starts automatically after upload (no cost confirmation step); the tokens used and
  the duration are shown at the end. It runs in a hosted `BackgroundService`, one book at a time, with progress stored
  in the database. An analysis with no progress for 10 minutes is marked as interrupted and can be retried.
- **AI:** all AI calls go through a port in `Application` (`IChatModel`) implemented by one adapter that speaks the
  OpenAI-compatible Chat Completions API, so any compatible provider works (OpenAI, local servers, other vendors).
  A provider is `{ id, name, baseUrl, apiKey, model }`. Adding one runs a connection test first. Transient
  failures are retried 3 times with growing delays. The port is faked in tests.
- **Data:** one JSON file per book (`data/books/{id}.json`) holding metadata, analysis status, book summary and
  characters. The book text itself is never stored. Every file has a `schemaVersion`. Writes are atomic (temp
  file then rename). Imported JSON is untrusted input: validate schema, sizes and text lengths before accepting.
  Storage sits behind a port (`IBookRepository`) so another backend can be added later.
- **Language of data:** everything the analysis writes into JSON (book summary, character descriptions,
  personality profiles) is in **English**, whatever the book's language, so files are portable and examples are
  shared. The chat prompt tells the LLM to answer the user **in the user's own language** (the language they write
  in). Prompts sent to the LLM are written in English.
- **Prompts:** no prompt is hard-coded. All prompts (read a part, merge characters, character profile, book
  summary, chat system prompt, connection test) live in `data/prompts.json`, which users can read and edit by
  hand. Defaults are versioned in the repository (`prompts.default.json`) and copied in full to `data/` on first
  start. Prompts are re-read on every analysis and chat message (no restart needed). On update, missing keys are
  added but existing prompts are never overwritten. An invalid file is an explicit error, never a silent
  fallback to defaults. Variables use `{name}` placeholders documented in each prompt's `description`. The
  number of characters extracted (10) is the `{maxCharacters}` variable of the merge prompt: no UI setting.
- **Examples:** public-domain books already analyzed live in `examples/*.json` in the repository, read-only, in the
  same format as user exports.
- **Settings:** providers live in `data/settings.json` with keys encrypted by ASP.NET Core Data Protection. This
  file is never part of a book export.
- **Deployment:** multi-stage Dockerfile and `docker-compose.yml`. `docker compose up` must be enough to run the
  app, with no external service. GitHub Actions publishes a Docker image to GitHub Container Registry (GHCR) for
  each release, so users can run it without building; building from source stays possible for contributors.

## Commands

```bash
dotnet build                                  # build the solution (planned)
dotnet test                                   # run all tests (planned)
docker compose up --build                     # run the app in a container (planned)
```

Configuration comes from environment variables. Copy `.env.example` to `.env` for local development; `.env` is
never committed.

## Security rules

- Never commit passwords, API keys, connection strings or tokens.
- Provider API keys are encrypted at rest with ASP.NET Core Data Protection, never logged, never returned to the
  browser, never included in book exports.
- LLM base URLs are user input and the server calls them (SSRF risk): accept only `http(s)` URLs, set timeouts,
  do not follow redirects blindly, and never echo upstream error bodies containing secrets. Private addresses
  (`localhost`, `192.168.x.x`, `host.docker.internal`) are **allowed** so local models (Ollama, LM Studio) work:
  the app is meant to be self-hosted by one person. Document in the README that it must not be exposed publicly
  without protection.
- Book text and chat messages are never written to logs.
- Validate everything coming from the user or an external service. Check uploaded file types by content, not
  only by extension, and enforce a size limit (20 MB). There is no page limit.
- Use the ORM or parameterized queries only; never concatenate user input into a query.
- Razor forms use antiforgery protection.
- V0 has **no authentication**. Anyone who can reach the app can use the saved LLM keys. The README must warn
  that the app is for personal use and must not be exposed to the Internet without a protecting reverse proxy.
  Do not add accounts or a password without a validated ticket.

## Code rules

- Test-driven development for any business logic or bug fix: write a failing test first, then the minimal code.
- Prefer simple, explicit code over premature abstraction. One responsibility per file, descriptive names.
- Comments only to explain a non-obvious decision or constraint.
- Do not add a dependency without documenting why.
- Do not widen the scope of a task without explicit agreement.

## Tests

- xUnit v3 on Microsoft.Testing.Platform (set in `global.json`; each test project is an executable), Moq and
  Shouldly. Do not use FluentAssertions (paid license since v8).
- On Windows, if `dotnet test` fails with "Access denied" (named pipe or `OpenProcess`), check that the repository
  folder does not carry a low integrity label (`icacls <folder>`): test executables built there would run at low
  integrity and could not talk to `dotnet test`. Fix: `icacls <folder> /setintegritylevel (OI)(CI)M`.
- Test projects mirror the tested code and follow Arrange / Act / Assert.
- Integration tests use `WebApplicationFactory<T>` with a temporary data directory or dedicated fakes.
- Tests never call a real AI provider.
- A few critical end-to-end scenarios run in a real browser with **Playwright** against a fake LLM server, in CI:
  add an LLM, add a book and analyze it, chat with a character.
- Cover nominal cases, invalid input, empty results and transient AI errors.
- Run `dotnet test` before declaring work done and report failures honestly.

## UI rules

- Visual identity: a **printed novel**. Black text on white paper, refined serif typography (headings and body),
  generous margins and line spacing, small details such as drop caps or ornaments. Sober, centered layout with a
  maximum width. Works on phone and desktop. Decisions and tokens live in `DESIGN.md`.
- Use the `emil-design-eng` skill (https://github.com/emilkowalski/skills) for design and motion decisions.
- Every form field has a visible label; keyboard navigation is preserved; errors are shown in the interface.
- Every user-facing string lives in JSON translation files (`Resources/en.json`, `Resources/fr.json`: flat
  `key: text` pairs, `{name}` placeholders), loaded by a small custom `IStringLocalizer` with English as fallback.
  Never hard-code text in views or code. A test fails when a key is missing in a language. English is the source;
  adding a language means copying `en.json` and translating it.
- No decorative animation; respect `prefers-reduced-motion`.
- Support recent Chrome, Edge, Firefox and Safari.

## Workflow for agents

- **When in doubt, ask the user.** Never guess on a requirement, a design choice, a destructive action or an
  ambiguous instruction: stop and ask, then continue once answered. Several questions are welcome.
- Ask clarifying questions only when the request or this file does not settle a point. Present them as
  **multiple-choice questions with a recommended option**; use an open question only when the possible choices
  are not known. Group questions on the same topic, then restate the answers as decisions before changing code.
- Present a plan and wait for agreement before any significant change.
- Make small, coherent, easy-to-review changes. Run the relevant build or tests after each change.
- Never delete files or data, commit, push or deploy without explicit agreement.
- Keep README and docs in sync with the real behavior of the code.
- Git: one commit per ticket, message in English prefixed with the ticket key (e.g. `PAPER-2 Add solution
  skeleton`).

## Definition of done

1. The change answers the request without unnecessary scope.
2. Build and applicable tests pass.
3. The changed behavior has been verified.
4. Related documentation is up to date.
5. No secret was added to tracked files.

## Spoilers

Character profiles and the chat cover the **whole book** and may contain spoilers. The README says so. A
"spoiler-free up to chapter N" mode is a possible future feature, not part of V0.

## Versioning

Semantic Versioning (`0.1.0`, `0.2.0`, ...). `CHANGELOG.md` is updated with each change. Pushing a `v*` tag
publishes the Docker image to GHCR. The version number is shown in the page footer.

## Contributing

External contributions require signing the project's **Contributor License Agreement (CLA)**, which lets the
maintainer relicense the code (for example in a commercial edition). A bot asks for the signature on the first
pull request. Contributors keep ownership of their work. Changes are proposed by fork and pull request; CI must
pass. Contributors must read and understand any AI-assisted code they submit. Roadmap and open tasks are public
(`ROADMAP.md` and GitHub Issues).

## License

AGPL-3.0. Anyone hosting a modified version must publish their changes.
