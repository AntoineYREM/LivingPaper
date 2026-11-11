---
name: LivingPaper
description: Chat with the characters of your books, in the pages of a printed novel.
colors:
  paper: "#ffffff"
  ink: "#141414"
  ink-soft: "#3f3f3f"
  ink-faint: "#666666"
  wash: "#f5f5f5"
  rule: "#d6d6d6"
  rule-strong: "#8c8c8c"
  error: "#a3161b"
  error-wash: "#fdf1f1"
  success: "#1a6a37"
  success-wash: "#eff7f1"
  warning: "#7a4f00"
  warning-wash: "#fbf5e6"
typography:
  display:
    fontFamily: "EB Garamond Variable, Iowan Old Style, Palatino Linotype, Palatino, Book Antiqua, Georgia, serif"
    fontSize: "clamp(2.75rem, 2.1rem + 3vw, 4.5rem)"
    fontWeight: 500
    lineHeight: 1.12
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "EB Garamond Variable, Georgia, serif"
    fontSize: "1.8125rem"
    fontWeight: 600
    lineHeight: 1.12
  title:
    fontFamily: "EB Garamond Variable, Georgia, serif"
    fontSize: "1.4375rem"
    fontWeight: 500
    lineHeight: 1.35
  body:
    fontFamily: "EB Garamond Variable, Georgia, serif"
    fontSize: "clamp(1.1875rem, 1.12rem + 0.3vw, 1.3125rem)"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "EB Garamond Variable, Georgia, serif"
    fontSize: "1.125rem"
    fontWeight: 600
    lineHeight: 1.35
    letterSpacing: "0.09em"
    fontFeature: "small capitals (font-variant-caps: all-small-caps)"
  note:
    fontFamily: "EB Garamond Variable, Georgia, serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.35
rounded:
  edge: "2px"
spacing:
  "1": "0.25rem"
  "2": "0.5rem"
  "3": "0.75rem"
  "4": "1rem"
  "5": "1.5rem"
  "6": "2rem"
  "7": "3rem"
  "8": "4.5rem"
  "9": "6.5rem"
  gutter: "clamp(1.25rem, 5vw, 2.5rem)"
components:
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    typography: "{typography.label}"
    rounded: "{rounded.edge}"
    height: "2.875rem"
    padding: "0 1.5rem"
  button-primary-hover:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
  button-quiet:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    height: "2.875rem"
  button-quiet-hover:
    backgroundColor: "{colors.wash}"
  button-danger:
    backgroundColor: "transparent"
    textColor: "{colors.error}"
    rounded: "{rounded.edge}"
  button-danger-hover:
    backgroundColor: "{colors.error}"
    textColor: "{colors.paper}"
  button-disabled:
    backgroundColor: "{colors.wash}"
    textColor: "{colors.ink-faint}"
  input:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    height: "2.875rem"
    padding: "0.5rem 0.75rem"
  alert-error:
    backgroundColor: "{colors.error-wash}"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    padding: "1rem 1.5rem"
  alert-success:
    backgroundColor: "{colors.success-wash}"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    padding: "1rem 1.5rem"
  alert-warning:
    backgroundColor: "{colors.warning-wash}"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    padding: "1rem 1.5rem"
  message-character:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    padding: "0.75rem 1rem"
  message-reader:
    backgroundColor: "{colors.wash}"
    textColor: "{colors.ink}"
    rounded: "{rounded.edge}"
    padding: "0.75rem 1rem"
---

# Design System: LivingPaper

## Overview

**Creative North Star: "The Printed Novel"**

The interface is the page of a printed novel, and the software steps back like the margins of a book. Black ink on white paper, one serif family, one centered column, hairline rules, square corners, no shadow. The owner imposed this look; it refuses the category default of cards, rounded tiles, an accent color and an icon on everything. The reader should forget the software and remember the book.

The system borrows the apparatus of print. A list of books is a table of contents with dot leaders; a list of characters is the cast of a play; a text that begins something opens with a drop cap and the printer's ornament; a page ends on a folio. Nothing moves for decoration. Hierarchy comes from size, weight, italic and true small capitals, never from color or depth.

The build chose white paper by decision: there is no dark mode. Words stand in for icons. Everything is served by the application itself (font, ornament), with no CDN.

**Key Characteristics:**
- One family, EB Garamond (variable, SIL OFL), in roman, italic and true small capitals.
- Color is ink and grey; red, green and ochre exist only as states and always come with a word.
- Single column, 46rem wide, margins that grow with the screen.
- Flat: hairlines and ink slabs instead of shadows; 2px edge on controls only.
- Signature move: the drop cap with the printer's ornament, and the folio at the foot of a page.

## Colors

Ink on paper with a grey that recedes; three state colors that never appear alone.

### Primary
- **Printer's Ink** (`colors.ink`): body text, titles, the primary button slab, focus outline, selection, the heavy running-head rule. The only dark fill of any screen (the Send button is the only ink slab in the chat).

### Neutral
- **Paper White** (`colors.paper`): the page and every field. White by decision.
- **Soft Ink** (`colors.ink-soft`): running text that must recede (lead, quotes, notes, pending replies).
- **Faint Ink** (`colors.ink-faint`): meta, hints, placeholders, folio, speaker names. Lowest allowed text grey (5.7:1).
- **Quiet Wash** (`colors.wash`): a quiet field: the reader's chat turns, quiet-button hover, disabled grounds.
- **Hairline** (`colors.rule`): rules between things; decoration only, never the sole mark of a control.
- **Control Edge** (`colors.rule-strong`): the border of any control, the dot leaders, the plain note.

### Tertiary (states only)
- **Proofreader's Red** (`colors.error`, wash `colors.error-wash`): errors and what destroys (delete).
- **Proof Green** (`colors.success`, wash `colors.success-wash`): success, "ready" status.
- **Marginal Ochre** (`colors.warning`, wash `colors.warning-wash`): warnings.

### Named Rules
**The Ink And Grey Rule.** Outside the three states, nothing is colored. A new accent color is a new world, not an extension.

**The Word Beside The Color Rule.** A state color is always paired with a word (title, status word, error sentence), never color alone. The red field border also thickens.

**The Tokens Only Rule.** Colors are written only in `tokens.css` as `#rrggbb`; every other style sheet uses `var(--color-*)`. Every text/background pair must reach WCAG AA (4.5:1); a test computes it from the tokens.

## Typography

**Display Font:** EB Garamond Variable (with Iowan Old Style, Palatino, Georgia, serif)
**Body Font:** the same
**Label Font:** the same, in true small capitals

**Character:** a refined old-style serif set like a book: oldstyle figures in running text, common ligatures, kerning, auto hyphenation by page language. Hierarchy is by size and weight, with italic for voice.

### Hierarchy
- **Display** (500, clamp 2.75rem to 4.5rem, 1.12, -0.015em): the h1 of a page, balanced wrapping.
- **Headline** (600, 1.8125rem, 1.12): h2; 4.5rem above, 1rem below (more above than below).
- **Title** (500 to 700, 1.4375rem): h3 in bold small capitals, book titles in the contents, brand name, the lead (italic, soft ink).
- **Body** (400, clamp 1.1875rem to 1.3125rem, 1.6): running text, max 66ch (`--measure`, tested at 60 to 75). In `.prose`, paragraphs after the first are indented 1.5em, not spaced.
- **Label** (600, 1.125rem, +0.09em, all-small-caps): field labels, buttons, nav links, status words, speaker names, cast names, fact terms.
- **Note** (400, 1rem to 0.875rem): hints, errors (600), meta, swatch captions.

### Named Rules
**The True Small Caps Rule.** Labels use `font-variant-caps: all-small-caps` with letter-spacing 0.09em, never uppercase text at reduced size.

**The Lining Figures Rule.** Figures are oldstyle in running text; counts, versions, statuses and the folio use lining (and tabular where they align).

**The One Family Rule.** No second typeface, no system display face. Weight, italic and caps carry every role.

## Layout

A single centered column: `--page-width` 46rem plus a gutter of clamp(1.25rem, 5vw, 2.5rem) each side, white all around. The body is a flex column so the folio sits at the foot. Spacing is a scale of 8 with a quarter step: 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4.5, 6.5rem. Vertical rhythm is generous: 4.5rem above h1 and h2, 3rem around ornaments and rules.

The page opens with a running head: brand left (600, 1.4375rem), small-capital links right, one 3px ink rule below. The active link is underlined; there is no hover fill.

Responsive: the column never changes. Under 40rem, the contents list drops its dot leaders and the status falls on its own line; the cast stacks to one column with the button below; settings facts stack term over value; chat turns widen from 88% to 94%. The chat composer keeps the Send button beside the field at every width (stacking would eat a quarter of a phone screen), with a tighter gap and sticky at the foot above the safe-area inset.

## Elevation & Depth

Flat. No shadow, no blur, no layering. Depth is conveyed by hairlines (1px `rule`, `rule-strong`), the 3px running-head rule, a 1px ink rule opening each list, and a tonal wash. The only `box-shadow` in the build is a 1px inset in red that thickens an invalid field's border; it is a border, not elevation.

### Named Rules
**The Flat Page Rule.** A printed page has no shadow. A surface is distinguished by a rule or a wash, never by lifting.

## Shapes

Square corners. The single radius is 2px, which only softens the edge of a control (buttons, fields, tags, alerts, chat boxes, focus outline). Borders are 1px hairlines; a plain note is one hairline with no ground. The printer's ornament (a diamond between two beads and two fine rules, `ornament.svg`, painted in faint ink via a mask, 7.5rem wide) is the one drawn shape. Progress is a 4px bar, square, ink on hairline.

## Components

### Language switcher

In the running head, at the right of the brand: one small-capital button per language (`EN`, `FR`, then any language added), each carrying its own name as its accessible label ("Français", never translated) and its own `lang`. The current one is underlined and has `aria-current="true"`; the others underline on hover. It is a form that posts to `/Language` (antiforgery token, works without JavaScript) and returns the visitor to the page they were on. Targets are at least 2.75rem. Added with PAPER-13; a row of page links (`.nav`, the current page marked `aria-current="page"`) sits between the brand and the switcher; Settings is there (PAPER-5), the library and examples will join it when those pages exist.

### Buttons
- **Shape:** 2px edge, minimum height 2.875rem (a finger can hit it), padding 0 1.5rem, label in small capitals.
- **Primary:** ink slab with paper text. Hover prints in reverse (paper ground, ink text).
- **Quiet:** transparent, control-edge border, hover wash with ink border.
- **Danger:** red outline and text, hover fills red. Always says what it destroys in words.
- **Small:** 2.5rem high, 1rem horizontal padding. **Disabled:** wash ground, hairline border, faint text, not-allowed cursor.
- **Focus:** the global 2px ink outline, 3px offset. Transitions 120ms, ease-out, color only.

### Inputs / Fields
- Label above in small capitals, optional hint in faint ink below, error in red 600 below that. Control: paper ground, 1px control-edge border, 2px edge, min 2.875rem, hover darkens border to ink.
- **Invalid** (`aria-invalid="true"`): red border plus 1px inset red, with the error sentence; never color alone. **Disabled:** wash ground, faint text.
- Fieldset: 1px hairline frame, small-capital legend; choice rows at least 2.5rem high. Forms are capped at the measure.

### Alerts
- Plain note: one control-edge hairline, transparent. Error, success, warning: same shape with a tinted ground, state-colored border and a small-capital title in the state color that names the state.

### Contents (list of books)
- A table of contents: 1px ink rule above the first row, hairlines between rows; title (1.4375rem), dotted 2px leader in control-edge grey, status word in small capitals (ready in green, failed in red) with tabular lining figures; italic soft meta line; actions below; thin ink progress bar. Under 40rem, leaders disappear.

### Cast (list of characters)
- Set like a play: three columns (name in bold small capitals up to 12rem, italic soft note, action button), 1px ink rule on top, hairlines between. Stacks under 40rem.

### Entries (settings items)
- A title, a definition grid of facts (term in faint small capitals, value breaking anywhere), and a bordered small-caps tag. Same ink rule opening the list.

### A book in the list (`.entry__problem`, `.entry__warning`, `.entry__actions`)
- Under the title: the status tag and, beside it, the progress in words ("3 of 12 parts read"). What went wrong is a paragraph with a left rule in the proofreader's red (`.entry__problem`), a warning (parts left out) one with a neutral rule (`.entry__warning`); neither relies on color, the sentence says it. The actions (Retry, Delete) sit in a row (`.entry__actions`): Retry is a small ink button, Delete the confirming disclosure below. While a book waits or runs, the list refreshes every 5 seconds (`wwwroot/js/books.js`, small vanilla script); it never replaces the list under an open confirmation or a focus, and without the script a reload shows the same thing. Added with PAPER-8.

### Pages of the library, of a book and of a character (PAPER-9)
- The library is the contents list (`.contents`): title as a link, dotted leader, status in small capitals (green when ready, proofreader's red when failed or interrupted), then the progress sentence, the 4px `.progress` bar while it runs, the file and counts, and the actions (Open, Retry, Delete). An empty library is the `.empty` block with the Add button. A page that belongs to another opens with a `.crumb` line (small, above the title) that goes back. The page of a book is the cast of a play (`.cast`: name in small capitals, italic note, "Read the sheet"); the sheet of a character is a title, the short description as lead, the profile as `.prose` with a drop cap on the first paragraph (one paragraph per line of the profile), and the edit form below a rule. A missing book, character or address shows the `NotFound` page (the `.empty` block) with the 404 status kept.

### Confirming a deletion (`.confirm`)
- A native `<details>`: its `<summary>` is a small danger button ("Delete"), which unfolds a warning and the confirming button ("Confirm the deletion"), set off by a red rule on the left. No script and no browser dialog; keyboard and screen readers get it for free. The summary carries an accessible label that names what is deleted. Added with PAPER-5 (`/Settings`).

### Chat page (PAPER-10)
- A page of its own (`/Book/{id}/Character/{n}/Chat`) in the validated design: boxed turns, the composer sticky at the foot. A message that scrolls into view stops above the composer (`scroll-margin-block-end` on `.chat .message`); the short hint sits above the composer, not inside it, to keep the stuck part low. A reply being written is the pending box, then fills with the text as it arrives. An error of the model is the red alert inside the window, and the unsent message returns to the field. `.js-only` hides what needs JavaScript (the chat buttons and the window) until `js-flag.js` marks the page; a `<noscript>` note says why the chat is missing.

### Chat
- A column of boxed turns, max 66ch. The character's turn: hairline box, left, speaker name in faint small capitals above. The reader's turn: right, wash ground and lighter hairline. A pending turn: same box, dashed, italic soft ink, in words, no animation. The composer is sticky at the foot: field plus Send, top hairline, paper ground; Send is the only ink slab on the screen. Decided by the owner on 2026-11-11 after reviewing /Design on phone and computer: boxed turns, the familiar messaging pattern, are kept (a play script was considered).

### Empty state
- Centered, italic 1.8125rem title and soft ink text. No illustration.

### Signature: Drop cap and ornament
- `.dropcap::first-letter`: 3.7em, weight 500, lining figures, floated left. Used only where a text begins something (a character profile, a book summary). Paired with the ornament above and indented paragraphs after the first. The folio (page number in lining faint ink, centered) closes each page.

## Do's and Don'ts

### Do:
- **Do** write every color in `tokens.css` only and reference `var(--color-*)` elsewhere; keep every text pair at WCAG AA or better.
- **Do** keep the column at 46rem and reading text at 66ch (60 to 75).
- **Do** use true small capitals (0.09em) for labels, buttons, nav, status words and speaker names.
- **Do** pair every state color with a word, and say what a destructive action destroys.
- **Do** keep a visible `:focus-visible` outline (2px ink, 3px offset) on everything focusable; support `prefers-reduced-motion`.
- **Do** use the drop cap and ornament where a text begins something, and end pages with a folio.
- **Do** use words instead of icons, and give every field a visible label.
- **Do** serve everything (fonts, ornament) from the application; set text in `lang`-aware hyphenation, and keep short interface notes unhyphenated.

### Don't:
- **Don't** add dark mode, an accent color, shadows, rounded corners above 2px, gradients, or cards.
- **Don't** add icons, icon fonts or a second typeface.
- **Don't** animate for decoration: no `@keyframes`; transitions are 120ms feedback on color only.
- **Don't** remove an outline, rely on color alone for state, or use `rule` as the only mark of a control (use `rule-strong`).
- **Don't** load anything from a CDN or external host, or introduce Bootstrap.
- **Don't** use more than one ink slab per screen.
- **Don't** treat the demo page texts as final: they are sample content until PAPER-13 (translations).
