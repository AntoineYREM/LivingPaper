# Contributing to LivingPaper

Thank you for helping. LivingPaper is a small, self-hosted project made by one maintainer. This file says how to propose a change,
how to propose an example book, and how translations work.

## Before you start

- **Look at the open issues** (`good first issue` and `help wanted` are meant for you) and the [roadmap](ROADMAP.md). For
  anything bigger than a fix, **open an issue first** and say what you want to do: it saves you a pull request that cannot be
  accepted.
- **Read the rules of the project**: `AGENTS.md` (architecture, code rules, security rules, UI rules, tests) and the short
  [architecture note](docs/architecture.md). They apply to humans and to AI agents alike.
- **Be kind.** The [Code of Conduct](CODE_OF_CONDUCT.md) applies everywhere in the project. Report a security problem as said in
  [SECURITY.md](SECURITY.md), never in a public issue.

## The contributor license agreement (CLA)

**Every external contribution needs a signed [CLA](CLA.md).** It lets the maintainer distribute the project under the AGPL-3.0
and also under other terms (a commercial edition, for example). **You keep the ownership of your work.**

You sign once, in your first pull request: a bot (CLA Assistant) asks you to post this comment, word for word, and the pull request
cannot be merged before:

> I have read the CLA Document and I hereby sign the CLA

If the check does not turn green, post `recheck`. Your signature is recorded in this repository, on the branch `cla-signatures`.
If you contribute for your employer, read article 8 of the CLA first.

## How to propose a change

1. **Fork** the repository and create a branch from `main`.
2. **Set up** with [docs/how-to-run.md](docs/how-to-run.md) (.NET 10 SDK; Docker is optional).
3. **Write a test first** for any business logic or bug fix, then the minimal code. Keep the change small and focused: one
   subject per pull request, no unrelated clean-up.
4. **Run `dotnet build` and `dotnet test`** (see [docs/how-to-test.md](docs/how-to-test.md)). Both must pass, with no warning:
   warnings are errors.
5. **Update the documentation** that your change makes wrong (README, `docs/`, `CHANGELOG.md` under *Unreleased*) and the two
   translation files if you add a text for the user.
6. **Open a pull request** with the template filled in. The CI must be green. The maintainer reviews it; expect remarks.

Commit messages are in English, short, and say what changes (`Add an Import button to the library`).

### Contributions written with an AI tool

They are welcome under one rule: **you must have read and understood every line you submit**, be able to explain it in the
review, and be sure that it holds no text you have no right to share. A pull request that looks generated and not read (large,
generic, unrelated to an issue, tests that test nothing) is closed.

### What will not be accepted

- A secret, an API key, or personal data in any file, in the history or in a test.
- A book, or a passage of a book, that is under copyright (examples are public-domain works only, see below).
- A new dependency without a reason written in the pull request and a license compatible with the AGPL-3.0 (the CI checks
  licenses).
- A sign-in, accounts or a password, without an issue that decided it: the reasons are in
  [docs/decisions/0004-no-authentication-in-v0.md](docs/decisions/0004-no-authentication-in-v0.md).

## Licenses of your contribution

The code is under the **AGPL-3.0** ([LICENSE](LICENSE)) and your contribution is covered by the CLA. The files of `examples/` are
under **CC0** ([examples/LICENSE](examples/LICENSE)): by proposing an example you dedicate your analysis to the public domain.

## Proposing an example

The **Examples** page shows books of the public domain that are already analyzed, so that LivingPaper can be tried without a key.
Each one is a file of the `examples/` folder, by pull request.

An example is a **book of LivingPaper as it is stored**: the file `data/books/{id}.json` that the application writes when it has
analyzed a book (it is also the format of an export). To make one:

1. **Choose a book that is in the public domain**, and check it: the author died more than 70 years ago (or the work is old
   enough for the law of the countries you care about), and the edition you analyze adds no rights of its own (a modern
   translation is not in the public domain). Say in the pull request where you took the text and why it is free to use. Never
   propose a book under copyright, and never a book you are not sure about.
2. **Analyze it with the application**, with a language model of your choice, and wait for the status "Analyzed". Keep the
   default prompts if you can: the examples show what the application does out of the box. Read the characters and the sheets:
   fix what is wrong with the editing form of the sheet, and analyze again with a better model if the result is poor.
3. **Take the file**: the **Export** button of the book gives it (or `data/books/{id}.json`). Copy it to `examples/{a-readable-name}.json` (for example `alice-in-wonderland.json`).
   The name of the file is free, the `id` written in it must be unique among the examples.
4. **Check that it holds nothing from the book**: no passage of the text, no quotation longer than a few words. The summary and
   the sheets are written in your own words (the analysis does that, but a model sometimes quotes: read them). The file holds no
   key, no address and no path of your machine (it never does: look anyway).
5. **Run `dotnet test`.** One test reads every file of `examples/` with the same validation as an imported book (schema, sizes,
   lengths of the texts) and refuses a file that is not an analyzed book with at least one character, or that has the same id or
   title as another example. A file that fails these rules is ignored by the application, with a warning in its logs.

The files are copied next to the application and into the Docker image. The examples are read only in the application: nobody can
change them or delete them, but anyone can add one to their library, where it becomes an ordinary book.

## Translations

Every text the user reads is in a translation file, never in a view or in the code. The interface is available in English
(`en`, the source and the fallback) and French (`fr`).

### Where the texts are

`src/LivingPaper.Web/Resources/en.json` and `fr.json`: one flat object of `"key": "text"` pairs.

```json
{
  "home.tagline": "Chat with the characters of your books.",
  "greeting": "Hello {name}",
  "book.characters.one": "{count} character",
  "book.characters.other": "{count} characters"
}
```

- **Keys** are letters, digits and underscores, in parts separated by single dots: `home.tagline`, `settings.error.name_taken`.
  They are case sensitive and defined once per file.
- **Texts** are plain text, never HTML: the characters `<` and `>` are refused, and views encode the text anyway.
  A text is never empty.
- **Variables** are written `{name}` (letters and digits). They are replaced by the values the code gives. A value is
  never replaced again, so a `{name}` inside a value stays as it is.
- **Plurals** use two keys, `<key>.one` and `<key>.other`, and the variable `{count}`: `1 character`, `8 characters`.
  English uses `one` for 1; French uses it for 0 and 1.

### Using a text

In a view, `T` is the localizer: `@T["home.tagline"]`, `@T.Format("greeting", new { name })`,
`@T.Plural("book.characters", count)`. In C#, inject `ITextLocalizer` (call it `T`, `texts` or `localizer`: the tests that look
for the keys in use know these names). Never write a text in a view or in the code; the tests fail if you do.

Everything the analysis writes into a book (summary, descriptions, profiles) stays in English, whatever the language of the
interface: it is data, not interface.

### What the tests check

- every key of `en.json` is in every other language, and the other way round;
- a text has the same `{variables}` in every language, and each plural has both its forms;
- a sentence is not left identical in English and French;
- every key used in a view or in the code exists in `en.json`;
- no view contains a text written in it (the design reference page `/Design`, shown in development only, is the exception);
- every language of `SupportedLanguages` has its file, and every file is a supported language.

If a text is missing in a language at run time, the English text is shown and a warning is logged; if it exists nowhere, the
key is shown and an error is logged. A translation file that is not valid stops the application when it starts, with the
name of the file and the key.

### Adding a language

1. Copy `Resources/en.json` to `Resources/<code>.json` (for example `de.json`) and translate every text. Keep the
   `{variables}`; add the plural forms your language needs under `.one` and `.other`.
2. Add the language to the list in `src/LivingPaper.Web/Localization/SupportedLanguages.cs`, with its code and its own name
   (`new("de", "Deutsch")`).
3. If the language does not use the rule of English or French for plurals (`one` for 1, or for 0 and 1), add its rule in
   `TextLocalizer.IsSingular`.
4. Run `dotnet test`: it tells you which key or variable is missing.

The language of a visitor is their choice (a cookie, set by the switcher in the running head), else the language of their
browser if it is one of ours, else English.
