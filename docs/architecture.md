# Architecture

LivingPaper is a **modular monolith** in the style of **ports and adapters** (hexagonal). The business rules know nothing of the
web, the files or the AI provider; those are adapters, plugged in at one place.

```text
                        ┌────────────────────────────────────────────┐
   browser ───────────► │ LivingPaper.Web                            │
                        │ Razor Pages, HTTP endpoints, composition   │
                        │ root (Program.cs), background worker       │
                        └───────┬───────────────┬────────────────────┘
                                │ uses          │ plugs in
                                ▼               ▼
              ┌───────────────────────┐   ┌─────────────────────────────┐
              │ LivingPaper.Application│   │ LivingPaper.Infrastructure  │
              │ use cases (BookService,│◄──│ OpenAI-compatible client,   │
              │ BookAnalyzer, chat…)   │   │ EPUB/PDF reading, encryption│
              │ and the PORTS          │   ├─────────────────────────────┤
              │ (IChatModel,           │◄──│ LivingPaper.Persistence     │
              │ IBookRepository…)      │   │ JSON files, atomic writes,  │
              └───────────┬────────────┘   │ prompts, examples, export   │
                          │ uses           └──────────────┬──────────────┘
                          ▼                               │ uses
              ┌───────────────────────┐                   │
              │ LivingPaper.Domain    │◄──────────────────┘
              │ Book, Character, rules│
              │ no dependency         │
              └───────────────────────┘
```

## The rule of dependencies

| Project | May depend on |
|---|---|
| `Domain` | nothing |
| `Application` | `Domain` |
| `Infrastructure`, `Persistence` | `Application`, `Domain` |
| `Web` | all of them (composition only) |

Architecture tests (`ArchitectureTests.cs` in the test projects of each layer) fail if a project references a layer it must not. Razor views
contain no data access: a page model calls the Application layer.

## Ports and their adapters

| Port (in `Application`) | Adapter | Where |
|---|---|---|
| `IChatModel`, `IChatModelFactory`, `ILlmConnectionTester` | Talk to any OpenAI-compatible Chat Completions API (streaming, retries, failure kinds) | Infrastructure |
| `IBookTextExtractor` | Reads the text of an EPUB or a PDF, checked on content, with bounds | Infrastructure |
| `ISecretProtector` | Encrypts API keys with ASP.NET Core Data Protection | Infrastructure |
| `IBookRepository`, `IAnalysisStepStore`, `IBookTextStore` | One JSON file per book, steps file, text file | Persistence |
| `ILlmSettingsRepository`, `IPromptProvider` | `data/settings.json`, `data/prompts.json` | Persistence |
| `IExampleCatalog`, `IBookArchive` | `examples/*.json`, export and import of a book | Persistence |
| `IAnalysisScheduler` | A hosted `BackgroundService` that analyzes one book at a time | Web |

Every port is faked in the tests: **no test calls a real AI provider.**

## The main flows

- **Add a book**: the page checks the file by content and size, `BookService` reads the text at once (so a problem shows
  immediately), saves the book as *Waiting* and hands it to the scheduler.
- **Analysis** (background): read the book in parts, note the characters of each part, merge them into the 10 most important,
  write one profile per character and a short summary. Each step is saved, so a failure resumes where it stopped. At the end
  the stored text is deleted.
- **Chat**: the browser sends the conversation (nothing is stored), the server checks it, builds the prompt from the saved sheet
  and streams the model's reply back as NDJSON.
- **Export / import**: a book is a JSON file with a `schemaVersion`; an import is validated like any untrusted input.

## Where the data lives

Everything is in the `data/` directory (the `/data` volume in Docker): `books/`, `uploads/` (the text of a book until its analysis
ends), `settings.json`, `keys/`, `prompts.json`. The shipped examples are in `examples/`. See
[ADR 0001](decisions/0001-json-files-instead-of-a-database.md).

## Decisions

The reasons behind the choices that shape the project are in [docs/decisions](decisions/README.md).
