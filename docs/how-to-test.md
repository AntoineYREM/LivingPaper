# How to test LivingPaper

## Run the tests

```bash
dotnet build
dotnet test
```

Both must pass with no warning (warnings are errors). The tests use **xUnit v3 on Microsoft.Testing.Platform** (set in
`global.json`): each test project is an executable.

```bash
# one project
dotnet test --project tests/LivingPaper.Web.IntegrationTests

# one class or one test (pass the filter after --)
dotnet test --project tests/LivingPaper.Application.Tests -- --filter-class "*BookServiceTests"
dotnet test --project tests/LivingPaper.Application.Tests -- --filter-method "*Export*"
```

## What is tested, and where

| Project | What it covers |
|---|---|
| `LivingPaper.Domain.Tests` | The business rules, and the rule of dependencies between layers |
| `LivingPaper.Application.Tests` | The use cases, with every port faked |
| `LivingPaper.Infrastructure.Tests` | The OpenAI-compatible client against a fake HTTP server, EPUB and PDF reading, encryption |
| `LivingPaper.Persistence.Tests` | JSON files: atomic writes, tolerant reading, prompts, examples, export format |
| `LivingPaper.Web.IntegrationTests` | The pages and endpoints through `WebApplicationFactory`, with a temporary data directory; translations; contrast of the design tokens |
| `LivingPaper.E2ETests` | Four journeys and a phone pass in a real Chromium (Playwright), against the application on a real port and a fake language model |

**No test calls a real AI provider**, and none reads your `.env`. The tests of the web application start a host per test, each with
its own temporary data directory and its own empty examples folder.

## End-to-end tests (a real browser)

`tests/LivingPaper.E2ETests` drives **Chromium** with [Playwright for .NET](https://playwright.dev/dotnet/). Each test starts the
application on a free local port with its own temporary data directory, and a **fake language model**: a small OpenAI-compatible
server that answers from the text of the prompts (the connection test, the analysis, the profiles, the chat, streamed). Nothing
leaves your machine, and no book under copyright is used (the EPUB is made by the test, with a text written for it).

```bash
dotnet test --project tests/LivingPaper.E2ETests
```

That is the only command (a plain `dotnet test` at the root runs them too, with all the others). **The first run downloads Chromium** (about 170 MB, into `%LOCALAPPDATA%\ms-playwright` or
`~/.cache/ms-playwright`) and the next ones reuse it. On a Linux machine that lacks the libraries of Chromium, set
`E2E_INSTALL_DEPS=1` to install them too (the CI does it by itself).

What the tests cover:

1. **Add a language model**: a refused connection test saves nothing and keeps what was typed (not the key); a working one is saved,
   chosen for the analysis and the chat, and the key is nowhere in the page.
2. **Analyze a book**: the library shows the analysis running with its progress, then by itself shows it analyzed; the page of the book
   shows the summary and the characters.
3. **Chat**: the reply is seen half written and then complete (the fake model holds its reply to prove the streaming); the model is
   given the sheet of the character; a refusal of the model is shown in the window and the message goes back in the field.
4. **Examples**: an example opens read only and you chat with one of its characters.
5. **On a phone** (390 × 844, touch): settings, library, examples, a book and the chat never scroll sideways and the Send button is
   inside the screen.

When a test fails it keeps a **screenshot and a trace** in `e2e-artifacts` next to the test program (or in the folder of `E2E_ARTIFACTS`);
the CI uploads them as the artifact `e2e-failures`. Open a trace with the `playwright.ps1 show-trace trace.zip` script that the build
puts next to the test program. To watch the browser, set `Headless = false` in `SharedBrowser` (`E2ETest.cs`) for a moment.

These tests are never skipped to make a build pass. If one is flaky, fix the wait (Playwright waits for what you assert) instead.

## How to write a test

- **Test first** for any business logic or bug fix: a failing test, then the minimal code, then refactor.
- **Arrange, Act, Assert**, one subject per test, a name that says the case (`AnImportedBook_IsAddedFinished_NothingIsAnalyzed`).
- Cover the nominal case, invalid input, the empty case and a transient AI error.
- Use Shouldly for assertions (not FluentAssertions) and the fakes of the test project (`BookFakes.cs`, `FakeLlmServer`).
- A check that a test really guards something: change the code on purpose (a *mutation*), and see the test fail.

## Reproduce the CI on Linux

The CI runs on Ubuntu, and Linux has case-sensitive paths that Windows does not. If a test passes on your machine and fails in the CI,
run the same thing in a container, from a clean export of the committed files:

```bash
git archive HEAD | tar -x -C /tmp/ci
docker run --rm -e CI=true -v /tmp/ci:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "dotnet build LivingPaper.sln --configuration Release && dotnet test LivingPaper.sln --no-build --configuration Release"
```

`CI=true` makes the end-to-end tests install Chromium and its libraries in the container (a few minutes the first time). Using `git archive` matters: it shows what GitHub sees, not the files that git ignores on your disk.

## Other checks of the CI

- **Dependency licenses**: `python3 .github/scripts/check-licenses.py` (after `dotnet restore`) fails if a NuGet package has a
  license that is not permissive.
- **Docker image**: `docker build .` must succeed.

## Windows notes

- If `dotnet test` fails with "Access denied" (a named pipe or `OpenProcess`), check that the repository folder does not carry a
  low integrity label (`icacls <folder>`). Fix: `icacls <folder> /setintegritylevel (OI)(CI)M`.
- A running `LivingPaper.Web.exe` locks its DLLs. Stop it before building, or build to another folder with
  `--artifacts-path`.
