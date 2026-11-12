# 0005. English for the data and the prompts, the user's language for the chat

**Status:** accepted

## Context

Books are written in many languages and users read the interface in theirs. Analyses are shared (the examples, exports and
imports). If each analysis were written in the language of its book, two copies of the same book analysed by two people could
not be compared, and the examples would be readable by few.

## Decision

- **Everything the analysis writes** into a book (summary, descriptions, personality profiles) is in **English**, whatever
  the language of the book. Files are portable and the examples are shared by everyone.
- **Prompts** are written in English.
- **The chat answers in the language the user writes in** (the chat prompt says "the language of the last message"), so a user
  can talk in French to a character whose sheet is in English.
- **The interface** is translated through JSON files (`en` is the source and the fallback, `fr` is provided), and every text of the
  user interface is in them, never in a view or in the code. A test fails when a key is missing in a language.

## Consequences

- A sheet is not shown in the interface language; it is data. This is stated in the README.
- Adding a language to the interface means copying `en.json`; it does not change the data.
- The quality of the sheets depends on the model's English, which is the best supported language of most models.
