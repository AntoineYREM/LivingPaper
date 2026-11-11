# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Two audiences, confirmed by the owner:

- **The reader who hosts it.** One person who reads, or has just read, a novel and runs the application on their own
  computer or server with their own AI key. No account, no team. They add a book, wait for the analysis, then chat.
- **The visitor who explores.** Someone who finds the application through the public-domain examples and wants to try
  talking to a character without bringing a key.

The interface must be clear to the second without losing the first: the host configures models and books, the visitor
only reads and chats.

## Product Purpose

LivingPaper lets you chat with the characters of your books. You add a book (EPUB or PDF), an AI reads it and extracts the
main characters, each with a short description and a personality profile, and then you text a character as if they were
a contact: first person, short SMS-like replies, streamed.

Success is a person who forgets they are using software and is simply talking with Argan or Toinette.

## Positioning

The owner chose the conversation itself as the point: the quality of the characters and of the exchange, over
self-hosting or data ownership as the headline. (Those facts remain true and secondary: bring-your-own key, books kept
as plain JSON files, open source, no account.)

## Operating Context

- Self-hosted web application: ASP.NET Core Razor Pages, server-rendered, small vanilla JavaScript, no external service.
  The chat streams over `fetch`. Everything except the chat works without JavaScript.
- One person per instance, no authentication in V0. For personal use, not exposed to the Internet unprotected.
- The user brings an OpenAI-compatible API key (OpenAI, local servers, other vendors) and chooses one model to analyze
  books and one to chat.
- Books and analyses are JSON files in a `data/` directory. The book file is deleted after a successful analysis.
- Analysis runs in the background, one book at a time, and can take minutes. Progress, tokens used and duration are shown.
- Interface in English and French from the start. Everything the analysis writes is in English whatever the book's
  language; the chat answers in the language the user writes in.

## Capabilities and Constraints

V0 scope: a settings page (add and delete models after a connection test, choose the analysis and chat model), add and
delete a book, background analysis (book summary of era, place and tone, and the 10 main characters), "My library" and
"Examples" pages (public-domain books shipped already analyzed, read-only), chat with a character (not stored).

Out of V0, to be designed for but not built: portraits, group image, video, daily budget, accounts or a password,
in-app reading, OCR for scanned PDFs, saved conversations.

Character profiles and the chat cover the whole book and may contain spoilers. The interface says so.

Terminology: "book", "character", "profile", "analysis", "model" (a configured language model), "library", "examples".

## Brand Commitments

- Name: LivingPaper. Tagline: "Chat with the characters of your books."
- Visual direction, binding and chosen by the owner: the interface evokes **a printed novel**. Black text on white
  paper, refined serif typography for headings and body, generous margins and line spacing, small typographic details
  such as drop caps or ornaments. Sober, centered layout with a maximum width. Works on phone and desktop.
- No decorative animation. Fonts are free (SIL OFL), served by the application itself (no CDN), so it works offline and
  leaks nothing.

## Evidence on Hand

- One public-domain book to try with: *Le Malade imaginaire* (Molière), a PDF in `books/` (local, not committed).
- No analyzed book, no character profile, no portrait, no logo and no testimonial exists yet. Nothing of that kind may
  be invented as if real; demonstration content must be labelled as sample content.

## Product Principles

1. The conversation comes first: everything else exists to get the person talking to a character quickly.
2. The book is the star, not the software: the interface steps back like the margins of a page.
3. Say what is happening: analysis takes minutes, models can fail, keys can be refused. Name each state plainly and
   say what to do.
4. Nothing leaves the person's machine without them knowing: their key, their books, their conversations.
5. Plain by default: everything but the chat works without JavaScript.

## Accessibility & Inclusion

- WCAG AA contrast at minimum, full keyboard navigation, visible labels on every form field, errors shown next to the
  field and in words, `prefers-reduced-motion` respected, text that can be enlarged without breaking the layout.
- Two languages from the start (English and French): no layout may depend on English text lengths.
- Supported browsers: recent Chrome, Edge, Firefox and Safari, on phone and desktop.
