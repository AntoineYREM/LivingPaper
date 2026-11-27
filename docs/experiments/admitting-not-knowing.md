# Experiments: a character that does not know says so

What was measured for PAPER-92, what came out, and what was decided. All of it was done on 27 November 2026.

**Read the figures with care.** Three famous books, one try a question, one free model for the character, one judge. The run
files stay in `evals/runs/`, which is not committed.

## Conclusions

1. **The strict rule is kept** (decision of the owner: better to say "I do not remember" than to give a false fact). Asked for a
   fact of its story that it was not given, the character says so and asks for a more precise question.
2. **Fewer invented answers:** 14 to 6 of 60 with the whole story, 6 to 2 with 3 passages.
3. **With the whole story, right answers are lost:** 35 to 24. In the 12 right answers that were lost, the fact asked was not in
   full in the story (checked by keywords): the earlier answers came from what the model knows of these books by itself.
4. **With passages, right answers hold:** 51 to 49, a difference that can be chance.
5. **The role is kept** (with the story; not measured with passages).
6. **A more precise question rarely finds the passage** with the current search: 1 of 14. The search is the next thing to
   improve (see [the search of a follow-up](#the-search-of-a-follow-up-search-only)), and was changed by PAPER-94.

## Setup

- **Rule:** in `chat.system`, it replaces "You know your whole story, from the beginning to the end of the book."
- **Questions:** the 60 factual questions of `evals/alice/factual.json`, `evals/cyrano/factual.json` and
  `evals/bovary/factual-en.json`; the 40 role questions of `evals/bovary/questions.json`.
- **Character:** ERNIE 5.5 Flash (free preview). **Judges:** `evals/accuracy-judge.json` and `evals/judge.json`, Claude
  Haiku 5.5.
- **Before:** for the facts, the runs of the [second measure of the passages](passages-of-the-book.md#three-books-and-two-models)
  (26 November, same model, judge and books). For the role, a new run with `evals/prompts/published.json`, the example of
  *Madame Bovary* having been analyzed again since the published run.
- **Search:** `bge-m3`, recent messages, 3 passages.

## Facts

| Replies, of 60 | Story, before | Story, after | 3 passages, before | 3 passages, after |
|---|---|---|---|---|
| Right (5 and 4) | 35 | 24 | 51 | 49 |
| Partly right (3) | 9 | 6 | 2 | 3 |
| Without the fact, "I do not remember" among them (2) | 2 | 24 | 1 | 6 |
| False (1) | 14 | 6 | 6 | 2 |
| Average | 3.48 | 3.02 | 4.28 | 4.33 |

The average hides what changed: an invention that becomes an admission raises it, a right answer that becomes an admission
lowers it. The tool now counts the replies of each grade.

Of the replies graded 2, about 19 of 24 (story) and 4 of 6 (passages) ask for a more precise question, counted by keywords and
not read one by one.

## Role

40 questions, 1 try: 4.78 before, 4.88 after.

## A more precise question

14 of the 60 questions had not found their passage. For each, a conversation was written: the question, the reply ERNIE gave,
then a more precise question (`evals/*/follow-up*.json`). The follow-ups were written knowing the expected answers, without
using them, and before any search with them. They are full sentences, which favors a search of the last message alone.

| Of 14 | Recent messages (default) | Last message | Search written by the model |
|---|---|---|---|
| Right passage found | 1 | 4 | 2 |
| Right (5 and 4) | 5 | 8 | 7 |
| Partly right (3) | 2 | 3 | 3 |
| Without the fact (2) | 4 | 2 | 2 |
| False (1) | 3 | 1 | 2 |
| Median time of a reply | 2.6 s | 2.6 s | 7.5 s |

Before the follow-up, 3 of these questions had a right answer. The written search failed twice (the recent messages were then
searched), and sometimes put a wrong fact in the query (a glass of poison for a glass of brandy).

## The search of a follow-up (search only)

No chat model: only whether the passage that holds the answer is among those found. Throwaway scripts, not in the repository.
Their figures for the first two columns of the table above are those of the tool.

Three sets of cases:

- **Long:** the 14 conversations above.
- **Short:** the 7 follow-ups of `evals/bovary/factual.json` ("And on the way home, did you find anything?"), French text.
- **New subject:** the 60 factual questions, each after the previous question of its file and the reply to it. Built by the
  script: one earlier exchange only.

"2 recent + 1 last" means passages taken in turn from the search of the recent messages and from that of the last message,
the first named going first, a passage never twice.

| Right passage found | Passages sent | Long (14) | Short (7) | New subject (60) |
|---|---|---|---|---|
| Recent messages (default) | 3 | 1 | 3 | **11** |
| Last message | 3 | 4 | 0 | 46 |
| 2 recent + 1 last | 3 | 3 | 3 | 35 |
| 1 recent + 2 last | 3 | 2 | 3 | 41 |
| 2 recent + 2 last | 4 at most | 3 | 3 | 42 |
| 3 recent + 3 last | 6 at most | 5 | 3 | 47 |
| Recent messages, 4 | 4 | 3 | 3 | 15 |
| Recent messages, 6 | 6 | 4 | 3 | 22 |

**When the reader changes the subject, the earlier messages drown the question:** 11 of 60 found. The earlier measures did not
show it: they asked questions alone, and follow-ups that need what came before. Searching the last message as well repairs most
of it.

**1 recent + 3 last, what the chat does since PAPER-94**, was run afterwards:

| Right passage found | Before (recent messages, 3) | 1 recent + 3 last, 4 |
|---|---|---|
| New subject (60) | 11 | 46 |
| Short follow-ups (7) | 3 | 3 |
| Long follow-ups (14) | 1 | 4 |
| **Total (81)** | 15 | **53** |
| First message of a conversation (60), where one search is made | 46 | 47 |

Two cases found before are lost: one new subject and one long follow-up. Part of the gain comes from the fourth passage: with
the recent messages only, 4 passages found 15 of the 60 new subjects and 3 of the 14 long follow-ups.

The distance of the closest passage does not tell a search that failed: median 0.378 when the right passage was found, 0.374
when it was not (100 searches of the runs of the day).

## Not measured

- The grades of the replies with any of the combined searches.
- Another model than ERNIE for the character, and a lesser-known book, where the model knows less by itself.
- The role with passages.
