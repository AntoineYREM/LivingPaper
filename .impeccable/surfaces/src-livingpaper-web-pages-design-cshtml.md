---
version: 1
slug: "src-livingpaper-web-pages-design-cshtml"
primary_target: "src/LivingPaper.Web/Pages/Design.cshtml"
related_targets: ["src/LivingPaper.Web/Pages/Shared/_Layout.cshtml"]
---

# Surface brief: the design reference page and the shared identity

Scope: `/Design` (development only) and the shared layout and style sheets it demonstrates. Mode: Operate for the screens
people use (settings, forms, chat) and Read for the pages people read (a book, a character's profile). The identity is
global; this page is its reference, not a product screen.

Audience and job: a reader who hosts the application, and a visitor who tries the examples. They add a book, wait, and talk
to a character. Success is forgetting the software.

Constraints (PRODUCT.md): black text on white paper, serif everywhere, generous margins and leading, one centered column,
no decorative animation, free fonts served by the application, WCAG AA, keyboard, `prefers-reduced-motion`, EN and FR.

Unresolved: no dark mode (the paper is white by decision); no icons (words instead); the demo page texts are sample content
until the translations (PAPER-13); the real pages are built later from these components.

## Direction contract

THESIS: the interface is the page of a printed novel, and the software steps back like the margins of a book. It refuses
the category default of cards, rounded tiles, an accent color and an icon on everything.

OWN-WORLD: white paper, near-black ink (`#141414`), one family (EB Garamond, variable) in roman, italic and true small
capitals, hairline rules, square corners, no shadow. Grey recedes. Red, green and ochre exist only as states and are always
accompanied by a word. A list of books is a table of contents with dot leaders; a list of characters is the cast of a play;
a conversation is a column of boxed turns (the familiar messaging pattern: the character in a hairline box on the left, the reader on a grey ground on the right, the speaker's name in small capitals above each), which the owner may later replace by a play script; a profile opens with an ornament, a drop cap and indented paragraphs; parts are separated by the printer's ornament (a diamond between
two beads and two fine rules); the page ends on a folio.

STORY: the visitor sees at once that this is about books and about reading, trusts the quiet interface, finds the character
they want, and starts talking.

FIRST VIEWPORT: a running head (the name at the left, three small-capital links at the right, one heavy 3 px rule below), then
a very large title in roman, an italic lead, and one note in a single hairline, all in a column about 740 pixels wide centered in white,
with wide margins. The primary action is an ink button; there is no hero image.

FORM: a printed novel's page with the apparatus of a play (the cast) and of a book (the contents, the folio). One signature
move, the drop cap with its ornament, used where a text begins something. Position 1 of the owner's pinned direction;
seed: none (the roll was skipped: the direction was pinned by the owner).

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
