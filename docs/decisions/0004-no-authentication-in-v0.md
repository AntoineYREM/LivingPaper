# 0004. No authentication in version 0

**Status:** accepted

## Context

LivingPaper is for one person, on their own computer or home network. It holds that person's API keys. A sign-in system (accounts,
password reset, sessions, roles) is a large surface to build and to get right, and it does not serve that use.

## Decision

Version 0 has **no authentication**. Anyone who can reach the application can use the saved keys and the library.

The consequences are written down where users will read them: the README and the Docker setup say that the application **must not
be exposed to the Internet** without a reverse proxy that requires a sign-in, and `docker-compose.yml` publishes the port on
`127.0.0.1` only. `SECURITY.md` says that "there is no login" is a known limit and not a vulnerability.

Do not add accounts or a password without an issue that decides it.

## Consequences

- The attack surface stays small, and the code is simple.
- Private addresses are allowed for model URLs, and uploads and imports are bounded, because the user is trusted.
- Sharing an installation with other people, or hosting it for others, is not supported. A site password, accounts and a daily
  budget are on the [roadmap](../../ROADMAP.md) as possible work, to be decided on their own terms.
- This decision should be revisited before any hosted offer.
