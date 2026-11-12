# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Choose the model in a list (PAPER-26): in Settings the user types a name, an address and a key, presses "Get the models",
  chooses a model in the list the provider returns (`GET {baseUrl}/models`) and saves it. The list proves that the key works,
  so nothing else is tested. The draft, key included, stays in the memory of the server (15 minutes, 5 drafts at most, never on
  disk) and the page only carries a random identifier: the key never comes back to the browser. A provider with no list of
  models (HTTP 404, 405, 501 or an answer that is not a list) falls back to typing the model, which is tested with a short
  request before it is saved, as before. The addresses are checked as for the chat, no redirect is followed, the answer is
  limited in time and in size, and no error body, address or key appears in a message. This replaces the former form with a
  free "model" field. New classes: `IModelCatalog`, `PendingProviderStore`, `OpenAiCompatibleModelCatalog`.

- Version number (PAPER-20): the version is written once, as `<Version>` in `Directory.Build.props`, and shown in the folio at
  the foot of every page ("LivingPaper, version 0.1.0", translated). A test checks that the page shows the version of the
  project. The release tag is `v` followed by that number (`v0.1.0`), and the image published for it carries the same version.
- Release image (PAPER-19): pushing a tag `v*` runs the build and all tests, then publishes the Docker image to GitHub
  Container Registry for amd64 and arm64, tagged `0.1.0`, `0.1` and `latest` (from `v0.1.0`), with the OCI label
  `org.opencontainers.image.version`. The workflow uses the `GITHUB_TOKEN` (no secret) and `packages: write` only; the CI
  workflow is reused with `workflow_call`. `docs/how-to-run.md` explains how to run the published image.
- Solution skeleton: .NET 10 solution with the hexagonal layers (`Domain`, `Application`, `Infrastructure`,
  `Persistence`, `Web`), one test project per layer, architecture tests that guard the allowed dependencies,
  a smoke test of the web application, and a GitHub Actions workflow that builds and tests every push.
  Tests use xUnit v3 on Microsoft.Testing.Platform.
- JSON storage of analyzed books (PAPER-4): `Book`, `Character` and `BookId` in the Domain, the `IBookRepository` and
  `IAnalysisStepStore` ports in the Application, and JSON file adapters in Persistence. One file per book
  (`books/{id}.json`) with a `schemaVersion`, atomic writes (temporary file then rename), length limits per field,
  tolerant reading (an unreadable file is reported without breaking the list) and intermediate analysis steps in
  `books/{id}.steps.json`.
- Editable prompts (PAPER-15): all six prompts (`analysis.readPart`, `analysis.mergeCharacters`,
  `analysis.characterProfile`, `analysis.bookSummary`, `chat.system`, `llm.connectionTest`) live in `data/prompts.json`
  instead of the code. The defaults (`prompts.default.json`, in English) are copied in full on the first start; after an
  update only the missing prompts are added and an existing prompt is never overwritten. Prompts are read again at
  every use. An invalid file (bad JSON, missing or empty prompt, unknown variable) is an explicit error naming the
  file and the prompt, never a silent fallback. The data directory is set with `DATA_DIRECTORY` (default `data`).
  First `README.md` section, on the prompts.
- OpenAI-compatible language model client (PAPER-6): the `IChatModel` port (a streamed reply and a complete reply, with
  the tokens used) and `IChatModelFactory`, implemented by an adapter for `POST {baseUrl}/chat/completions` that works
  with any OpenAI-compatible provider. A reply cut before `[DONE]` and a provider content filter are errors, never a
  shorter reply. Failures carry a kind and the HTTP status; the body of an HTTP error, the URL and the key never appear
  in a message. Passing failures (429, 5xx, 408, network, time limit, cut or empty reply) are retried three times
  after 2, 5 and 10 seconds; refusals (400, 401, 403, 404) and filtered replies are not. Redirects are never followed.
  New dependency: `Microsoft.Extensions.Logging.Abstractions` (Infrastructure), to log retries.
- Language model settings, logic (PAPER-5, first part; the page comes after the design and the translations): the rules
  for adding, deleting and choosing models (`LlmSettings`, `LlmProviderValidator`), `LlmSettingsService` (a model is
  saved only after a successful connection test), `ILlmConnectionTester` (one try, 60 s limit, tells an unreachable
  address, a refused key, an unknown model, an invalid reply or a provider error apart), `data/settings.json` with the
  API keys encrypted by ASP.NET Core Data Protection (encryption keys in `data/keys`, a key that can no longer be read is
  reported and never used), and `IChatModelFactory.Create(provider, retryPassingFailures)`. `data/` is now ignored by
  git at any depth. New dependency: `Microsoft.AspNetCore.DataProtection.Abstractions` (Infrastructure).
- Visual identity (PAPER-16): a printed novel, black ink on white paper, one serif (EB Garamond, SIL OFL, served by the
  application). `PRODUCT.md` and `DESIGN.md` record the product and the design system; style sheets `tokens.css`,
  `base.css` and `components.css` (colors, sizes and spaces defined once, in tokens); a shared Razor layout; the components
  of the first pages (buttons, forms, alerts, list of books as a table of contents, list of characters as the cast of a
  play, chat, settings entries); and a design reference page, `/Design`, served in development only. Tests check the
  contrast of every text color (WCAG AA) from the tokens, that colors are written once, that nothing is loaded from
  outside, that nothing is animated and that reduced motion is respected. The application now serves `wwwroot`.
- Translations (PAPER-13): the interface is available in English and French. Texts live in `Resources/en.json` (the source)
  and `fr.json`, flat `key: text` pairs with `{variables}` and `.one`/`.other` plural forms, embedded in the application,
  read by a small localizer injected in every view as `T` (`@T["key"]`, `T.Format`, `T.Plural`). A missing text falls back to
  English and is logged; an invalid file stops the application at start. The language is the visitor's choice (a functional
  cookie, set by a switcher in the running head that works without JavaScript and only returns to local addresses), else the
  language of their browser, else English; `<html lang>` and `Content-Language` follow it. Tests fail for a key or a variable
  missing in a language, a key used but undefined, and any text written in a view. The home page is the first translated page.
  `CONTRIBUTING.md` explains how to add a language.
- Docker (PAPER-3): a multi-stage `Dockerfile` (SDK to build, ASP.NET runtime to run), `docker-compose.yml` and `.dockerignore`.
  `docker compose up --build` starts the application on <http://localhost:8080>. The container runs as an unprivileged user,
  keeps everything in the `/data` volume (it survives `docker compose down`), carries the examples and no secret, and has a
  health check. The port is published on `127.0.0.1` only, as the application has no sign-in.
- Open source files (PAPER-14, PAPER-18): `LICENSE` (AGPL-3.0), `CLA.md`, `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1),
  `SECURITY.md`, `ROADMAP.md`, `THIRD-PARTY-NOTICES.md`, a complete `README.md` and `CONTRIBUTING.md`, `docs/how-to-run.md`,
  `docs/how-to-test.md`, `docs/architecture.md` and six decision records in `docs/decisions/`. The examples are under CC0.
  Issue and pull request templates, Dependabot, CodeQL (active once the repository is public), and two new steps in the CI:
  a check that every dependency has a license compatible with the AGPL, and a build of the Docker image.
- End-to-end tests (PAPER-17): `tests/LivingPaper.E2ETests` drives Chromium (Playwright for .NET) against the application, started
  on a real port with a temporary data directory, and a fake OpenAI-compatible server that answers from the prompts. Four
  journeys (add a language model, analyze an EPUB made by the test, chat with streaming and a refusal of the model, an example)
  and a pass on a phone screen (no sideways scroll). A failed test keeps a screenshot and a trace, uploaded by the CI. One
  command: `dotnet test --project tests/LivingPaper.E2ETests`.

### Fixed

- The pages of a book, its character sheets and the chat (`Pages/Books`) were never committed because the `books/` rule of
  `.gitignore` also matched them on Windows; the rule is now anchored to the root.
- The tests of the web application no longer reach the limit of file watchers (inotify) of Linux machines, and the test of a
  connection cut in the middle of a reply no longer depends on timing.
