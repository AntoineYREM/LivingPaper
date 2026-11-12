# 0002. Any OpenAI-compatible language model

**Status:** accepted

## Context

Users have different providers and budgets: OpenAI, other vendors, or a local model (Ollama, LM Studio). Tying the project to one
vendor would exclude people and make the tests depend on a paid service.

## Decision

All AI calls go through one port (`IChatModel`) with one adapter that speaks the **OpenAI-compatible Chat Completions API**.
A model is `{ name, base URL, API key, model name }`; the user adds as many as they want, and chooses one for the analysis and one
for the chat. **A connection test must succeed before a model is saved.** Transient failures are retried three times with growing
delays. The key is optional for a local server.

The user brings their own key: there is no hosted service and no shared key.

## Consequences

- A local model works with no account and no cost; `host.docker.internal` reaches it from the container.
- The base URL is user input that the server calls: only `http` and `https`, timeouts, no blind redirects, and an upstream error
  body is never echoed back (it could hold a secret). **Private addresses are allowed on purpose**, which is acceptable because
  the application is for one person (see [ADR 0004](0004-no-authentication-in-v0.md)).
- API keys are encrypted at rest, never returned to the browser, never logged, never exported.
- Features that only some vendors offer (image generation, native structured output) are not used in version 0.
- The tests never call a real provider: the port is faked, and the adapter is tested against a fake HTTP server.
