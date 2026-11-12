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

**No test calls a real AI provider**, and none reads your `.env`. The tests of the web application start a host per test, each with
its own temporary data directory and its own empty examples folder.

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
docker run --rm -v /tmp/ci:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "dotnet build LivingPaper.sln --configuration Release && dotnet test LivingPaper.sln --no-build --configuration Release"
```

Using `git archive` matters: it shows what GitHub sees, not the files that git ignores on your disk.

## Other checks of the CI

- **Dependency licenses**: `python3 .github/scripts/check-licenses.py` (after `dotnet restore`) fails if a NuGet package has a
  license that is not permissive.
- **Docker image**: `docker build .` must succeed.

## Windows notes

- If `dotnet test` fails with "Access denied" (a named pipe or `OpenProcess`), check that the repository folder does not carry a
  low integrity label (`icacls <folder>`). Fix: `icacls <folder> /setintegritylevel (OI)(CI)M`.
- A running `LivingPaper.Web.exe` locks its DLLs. Stop it before building, or build to another folder with
  `--artifacts-path`.
