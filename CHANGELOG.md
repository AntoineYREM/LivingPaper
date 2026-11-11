# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
