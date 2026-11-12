# Security policy

## Reporting a vulnerability

**Do not open a public issue for a vulnerability.** Use GitHub's private reporting instead: on the repository, open the
**Security** tab, then **Report a vulnerability**. Say what you found, how to reproduce it and what it lets someone do.

You will get an answer within a few days. A confirmed vulnerability is fixed in a new version, noted in `CHANGELOG.md`, and
credited to you if you wish.

## Supported versions

LivingPaper is at version 0. Only the latest release receives security fixes.

## What the application protects, and what it does not

- **API keys** are encrypted at rest, never sent back to the browser, never logged and never part of a book export. The
  encryption keys sit next to the settings in the data directory (see the README): protect the whole `data/` volume.
- **Uploaded files and imported JSON** are untrusted input: type checked on content, size limited, parsed with bounds.
- **Book text and chat messages** are never written to the logs.
- **There is no authentication in version 0.** Anyone who can reach the application can use the saved API keys. It is made for
  one person on their own computer or network. **Do not expose it to the Internet** without a reverse proxy that requires a
  sign-in. This is a documented limit, not a vulnerability; a report that only says "there is no login" will be closed.
- **LLM base URLs are chosen by the user** and the server calls them, including private addresses, so that local models
  (Ollama, LM Studio) work. This is deliberate and follows from the previous point.
