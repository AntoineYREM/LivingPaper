# Contributing to LivingPaper

This file is a stub and grows with the project (the contribution process, the CLA, the code rules and the pull request
checklist come with the open source ticket). For now it explains one thing: the translations.

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
