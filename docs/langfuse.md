# Langfuse in LivingPaper

How the optional tracing of the language model calls works (PAPER-75). Why it was built this way: [decision 0009](decisions/0009-langfuse-tracing-over-opentelemetry.md).
For users, the settings are in [how-to-run.md](how-to-run.md#configuration) and [reference.md](reference.md#traces-of-the-model-calls-langfuse).

The same exporter also serves the evaluation tool of the characters, which sends its runs to Langfuse as experiments: see
[evals.md](evals.md#where-the-runs-go).

## What it is for

An analysis makes dozens of calls to a language model. Langfuse shows each one: which model, how many tokens, how long, whether it
failed. It is meant to tune the prompts and understand cost and errors. It is **off by default**.

## Turning it on

Set three variables (environment, `.env` for development, or `environment:` in `docker-compose.yml`):

| Variable | Role |
|---|---|
| `LANGFUSE_BASE_URL` | Address of Langfuse, for example `http://localhost:3000`. No default: the cloud address would send data to a third party. |
| `LANGFUSE_PUBLIC_KEY`, `LANGFUSE_SECRET_KEY` | Keys of a Langfuse project (Settings > API Keys). |
| `LANGFUSE_CAPTURE_CONTENT` | `false` (default): metadata only. `true`: also the prompts and replies. |

Rules checked at start (`LangfuseSettings`):

- Both keys empty: tracing is off, nothing is added, nothing is exported.
- Only one key, no base URL, an address that is not absolute http(s), or a value other than `true`/`false`: the application stops at
  start with a message naming the variable (never its value, which could be a secret).
- On, the log says so with the address only. With content capture on, it is a **warning**.

## How it works

```text
Application   LlmTelemetry.Source (an ActivitySource named "LivingPaper") + spans in AnalysisRun and LlmConnectionTester
Infrastructure TracingChatModel   wraps each model call, below the retries
Web            LangfuseTracing    OpenTelemetry + OTLP exporter, added only when configured
```

- **Langfuse has no .NET SDK.** The way in is OpenTelemetry: the application creates `System.Diagnostics.Activity` objects (part of
  .NET, no package in `Application`) and the Web project exports them over OTLP/HTTP protobuf to
  `{LANGFUSE_BASE_URL}/api/public/otel/v1/traces`.
- **Authentication:** HTTP Basic, public key as the user and secret key as the password. The header
  `x-langfuse-ingestion-version: 4` is sent; without it Langfuse can take up to ten minutes to show a trace.
- **Nothing listens, nothing costs.** Without the exporter, `TracingChatModel` sees no listener and calls the model directly, and the
  spans are never created.
- **`TracingChatModel` changes nothing in the call:** same reply, same exceptions, same tokens added to the caller's usage meter.
  It sits **below** `RetryingChatModel`, so each try is its own generation and a failed try shows as an error.

## What a trace looks like

| Trace or span | Where it starts | Notes |
|---|---|---|
| `book-analysis` | `AnalysisRun.ExecuteAsync` | Root of an analysis. Tag `book.id`; error status with the failure code if it fails. |
| `read-part` | each part of the book | Tag `analysis.part` (1-based). |
| `merge-characters` | merge of the characters | |
| `character-profile` | each profile | Tag `analysis.character` (1-based). |
| `book-summary`, `story-summary` | summary and scene-by-scene story | |
| `connection-test` | `LlmConnectionTester` | Its own trace: not part of an analysis or a chat. |
| `llm-call` | one whole-reply call (analysis) | Langfuse type `generation`. |
| `chat-reply` | one streamed chat reply | A span of the conversation's trace (`character-chat`): the page invents a 32-hex id, sent with each message, used as trace id and Langfuse session. Not stored. An invalid id gives a trace of its own. Records the time of the first piece (`completion_start_time`). |

A generation carries (Langfuse attributes `langfuse.observation.*`): `type=generation`, `model.name`, `metadata.provider` (the name
given in Settings), `usage_details` (input and output tokens), and on failure `level=ERROR` with a status message made of the kind of
failure and the HTTP status, for example `RateLimited (HTTP 429)`. A cancelled call, or a stream the reader stopped early, is a
`WARNING`.

## What is sent, and what is not

| | Default | With `LANGFUSE_CAPTURE_CONTENT=true` |
|---|---|---|
| Model, provider name, tokens, duration, step, outcome | yes | yes |
| Book id (the slug of the title), part and character numbers | yes | yes |
| Prompts and replies (text of books, character profiles, chat messages) | **no** | yes |
| Upstream error messages, API keys, base URLs of the models | never | never |

Book text and chat messages are otherwise never written to logs. Turn content capture on only for a Langfuse you control.

## Tests

- `tests/LivingPaper.Infrastructure.Tests/TracingChatModelTests.cs`: attributes, tokens, failures, streaming, content on and off
  (with `ActivityRecorder`, a listener that collects activities).
- `tests/LivingPaper.Application.Tests/BookAnalyzerTests.cs`: the spans of an analysis.
- `tests/LivingPaper.Web.IntegrationTests/LangfuseTests.cs`: reading of the settings and the wiring.
- No test contacts a real Langfuse or a real model.

## Trying it locally

1. Run Langfuse on your machine (see the Langfuse docs) and create a project and its keys.
2. Put the three variables in `.env` (copy `.env.example`), start the application, analyze a book.
3. Open the Langfuse UI: one `book-analysis` trace with its steps, and the generations under them. From Docker, use
   `http://host.docker.internal:3000` as the base URL.

## Adding a trace

Start an activity from `LlmTelemetry.Source` (`using var span = LlmTelemetry.Source.StartActivity("name");`). Put in it only ids, names
of steps, counts and codes: never a title, a character name or a passage of a book.
