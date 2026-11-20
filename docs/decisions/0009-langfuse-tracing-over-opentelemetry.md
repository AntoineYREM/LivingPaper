# 0009. Traces of the model calls go to Langfuse over OpenTelemetry, off by default

**Status:** accepted

## Context

An analysis makes dozens of calls to a language model. To tune the prompts and understand cost and failures, the maintainer wants to
see each call: model, tokens, duration, outcome. Langfuse does this, but has no .NET SDK. Its documented way in is OpenTelemetry
(OTLP over HTTP). The calls hold the text of the books, and the application is self-hosted and private.

## Decision

- Each call is an `Activity` of one `ActivitySource` (`LlmTelemetry`, part of .NET, no package in `Application`). A decorator,
  `TracingChatModel`, wraps the chat model below the retry layer, so each try is its own generation.
- The Web project adds the OpenTelemetry OTLP exporter **only when** `LANGFUSE_PUBLIC_KEY` and `LANGFUSE_SECRET_KEY` are set. Without
  them nothing is exported and no activity is created. One key alone, or keys without `LANGFUSE_BASE_URL`, stops the application at
  start, naming the variable.
- `LANGFUSE_BASE_URL` has no default: the cloud address would send data to a third party the user did not choose.
- **Metadata only by default.** Prompts and replies are recorded only with `LANGFUSE_CAPTURE_CONTENT=true`, and a warning is logged at
  start. Failures record the kind and HTTP status, never the message. The keys are never logged.
- Two packages added (Apache-2.0, compatible with the AGPL): `OpenTelemetry.Extensions.Hosting` and
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`.

## Consequences

- Zero cost and no behavior change for users who do not set the keys.
- The vocabulary of Langfuse (`langfuse.observation.*` attributes) is in the decorator; another backend would need its own mapping.
