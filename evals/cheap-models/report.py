"""Figures of the comparison of cheap models: prints the table of evals/README.md and draws its chart.

Run from anywhere: python evals/cheap-models/report.py
Reads the runs of evals/published/cheap-models/ and the prices next to this file; writes the two SVG files of
docs/images/evals/. Nothing is sent anywhere. Python standard library only.
"""
import glob
import json
import random
import statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNS = ROOT / "evals" / "published" / "cheap-models"
PRICES = Path(__file__).with_name("prices.json")
IMAGES = ROOT / "docs" / "images" / "evals"
JUDGE = "anthropic/claude-haiku-5.5"
ROLE, FACTS, TRAPS = "character-fidelity", "factual-accuracy", "honesty"
QUESTIONS = 135

# Where the name of each point goes, so that two names never cover each other: (right, down, anchor), per panel.
LABELS = {
    "deepseek/deepseek-v4.1-flash": ((11, 4, "start"), (11, 4, "start")),
    "z-ai/glm-5.3-flash": ((-11, 4, "end"), (0, 21, "middle")),
    "xiaomi/mimo-v2.6-flash": ((11, -3, "start"), (11, 5, "start")),
    "google/gemini-2.5-flash-lite": ((-11, 2, "end"), (-11, 4, "end")),
    "openai/gpt-6-luna": ((11, 12, "start"), (0, -12, "middle")),
    "anthropic/claude-haiku-5.5": ((0, 21, "middle"), (0, -12, "middle")),
    "mistralai/mistral-small-2603": ((11, 4, "start"), (11, 4, "start")),
    "openai/gpt-oss-120b": ((11, 4, "start"), (0, 21, "middle")),
    "minimax/minimax-m3.1-flash": ((11, 4, "start"), (11, 4, "start")),
}

THEMES = {
    "light": {"surface": "#fcfcfb", "text": "#0b0b0b", "muted": "#52514e", "grid": "#e3e2de", "model": "#2a78d6", "judge": "#eb6834"},
    "dark": {"surface": "#1a1a19", "text": "#f0efec", "muted": "#c3c2b7", "grid": "#343432", "model": "#3987e5", "judge": "#d95926"},
}


def load(path):
    with open(path, encoding="utf-8") as file:
        return json.load(file)


def mean(values):
    return sum(values) / len(values)


def replies_by_model():
    """Per model: one reply per question, and the tokens of every call. A question asked again replaces its failure."""
    runs = defaultdict(list)
    for path in sorted(glob.glob(str(RUNS / "*.json"))):
        run = load(path)
        runs[run["chat"]["model"]].append(run)
    models = {}
    for model, of_model in runs.items():
        replies, tokens = {}, defaultdict(int)
        # The runs of questions asked again come last, so that their replies win.
        for run in sorted(of_model, key=lambda r: r["label"] == "bench-cheap-rerun"):
            for result in run["results"]:
                key = (run["dataset"], result["questionId"])
                replies[key] = dict(result, kind=run["judge"]["scoreName"])
            for side, count in run["tokens"]["chat"].items():
                tokens[side] += count
        graded = [r for r in replies.values() if r["score"] is not None]
        if len(graded) != QUESTIONS:
            raise SystemExit(f"{model}: {len(graded)} graded replies, {QUESTIONS} expected")
        models[model] = (replies, tokens)
    return models


def figures():
    prices = load(PRICES)["models"]
    rows = []
    for model, (replies, tokens) in replies_by_model().items():
        scores = {kind: [r["score"] for r in replies.values() if r["kind"] == kind] for kind in (ROLE, FACTS, TRAPS)}
        price = prices[model]
        cost = ((tokens["input"] - tokens["cachedInput"]) * price["input"] + tokens["cachedInput"] * price["cachedInput"]
                + tokens["output"] * price["output"]) / 1e6
        rows.append({
            "model": model,
            "name": price["name"],
            "role": mean(scores[ROLE]),
            "right": 100 * mean([s >= 4 for s in scores[FACTS]]),
            "false": 100 * mean([s == 1 for s in scores[FACTS]]),
            "honest": sum(s >= 4 for s in scores[TRAPS]),
            "traps": len(scores[TRAPS]),
            "seconds": statistics.median(r["trace"]["seconds"] for r in replies.values()),
            "cost": 1000 * cost / len(replies),
            "replies": replies,
        })
    return sorted(rows, key=lambda r: -r["role"])


def print_report(rows):
    print("| Model | Staying in role (of 5) | Right facts | False facts | Traps, nothing made up | Median time | USD for 1,000 replies |")
    print("|---|---|---|---|---|---|---|")
    for r in rows:
        print(f"| {r['name']} | {r['role']:.2f} | {r['right']:.0f} % | {r['false']:.0f} % | {r['honest']}/{r['traps']} "
              f"| {r['seconds']:.1f} s | {r['cost']:.2f} |")
    # Difference with the best model, question by question, with a 95 % bootstrap interval over the questions.
    generator = random.Random(1)
    for kind, label in ((ROLE, "staying in role"), (FACTS, "facts (grade of 5)")):
        def average(row):
            return mean([r["score"] for r in row["replies"].values() if r["kind"] == kind])

        best = max(rows, key=average)
        print(f"\n{label}: best is {best['name']} ({average(best):.2f})")
        for row in rows:
            if row is best:
                continue
            gaps = [row["replies"][key]["score"] - reply["score"] for key, reply in best["replies"].items() if reply["kind"] == kind]
            means = sorted(mean(generator.choices(gaps, k=len(gaps))) for _ in range(10000))
            print(f"  {row['name']}: {mean(gaps):+.2f} [{means[250]:+.2f}, {means[9749]:+.2f}]")


def chart(rows, theme):
    colors = THEMES[theme]
    parts = []

    def text(x, y, content, size=12, color="muted", anchor="start", weight=None):
        bold = f' font-weight="{weight}"' if weight else ""
        parts.append(f'<text x="{x:g}" y="{y:g}" font-size="{size}" fill="{colors[color]}" text-anchor="{anchor}"{bold}>{content}</text>')

    def point(x, y, judge, title):
        """A circle for a model, a diamond for the one that is also the judge: the shape tells it without the color."""
        paint = f'fill="{colors["judge" if judge else "model"]}" stroke="{colors["surface"]}" stroke-width="2"'
        if judge:
            shape = f'<path d="M{x:.1f} {y - 8:.1f}l8 8l-8 8l-8 -8z" {paint}'
            parts.append(f"{shape}><title>{title}</title></path>")
        else:
            parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="6" {paint}><title>{title}</title></circle>')

    description = "; ".join(
        f"{r['name']}: role {r['role']:.2f} of 5, right facts {r['right']:.0f} %, {r['cost']:.2f} USD for 1,000 replies" for r in rows)
    text(24, 32, "Quality and price of nine inexpensive models", 17, "text", weight=600)
    text(24, 54, "Higher is better, further left is cheaper. Price of the replies in USD for 1,000. The vertical axes do not start at 0.")
    point(30, 78, False, "model")
    text(42, 82, "model")
    point(120, 78, True, "also the judge")
    text(134, 82, "Claude Haiku 5.5 is also the judge that gave every grade")

    left, right, widest = 56, 690, 0.8
    panels = (("role", "Staying in role (grade of 5)", 130, 3.8, 5.0, [4.0, 4.5, 5.0], "{:.1f}"),
              ("right", "Right answers to factual questions (%)", 440, 70, 90, [70, 75, 80, 85, 90], "{:g}"))
    height = 240
    for number, (key, title, top, low, high, ticks, shown) in enumerate(panels):
        def across(cost):
            return left + cost / widest * (right - left)

        def up(value):
            return top + height - (value - low) / (high - low) * height

        text(24, top - 14, title, 13, "text", weight=600)
        for tick in ticks:
            parts.append(f'<line x1="{left}" y1="{up(tick):.1f}" x2="{right}" y2="{up(tick):.1f}" stroke="{colors["grid"]}" stroke-width="1"/>')
            text(left - 8, up(tick) + 4, shown.format(tick), 11, anchor="end")
        for tick in (0, 0.2, 0.4, 0.6, 0.8):
            text(across(tick), top + height + 18, f"{tick:g}", 11, anchor="middle")
        text(right, top + height + 34, "USD for 1,000 replies", 11, anchor="end")
        for r in rows:
            x, y = across(r["cost"]), up(r[key])
            judge = r["model"] == JUDGE
            value = f"{r['role']:.2f} of 5" if key == "role" else f"{r['right']:.0f} %"
            point(x, y, judge, f"{r['name']}: {value}, {r['cost']:.2f} USD for 1,000 replies")
            dx, dy, anchor = LABELS[r["model"]][number]
            text(x + dx, y + dy, r["name"], 11, "text", anchor)

    text(24, 735, "135 questions each, one try, 27 November 2026. Chat only: the cost of the judge is not counted.", 11)
    return "\n".join([
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 720 750" width="720" height="750" role="img" '
        'aria-labelledby="title desc" font-family="system-ui, -apple-system, \'Segoe UI\', Roboto, Helvetica, Arial, sans-serif">',
        '  <title id="title">Quality and price of nine inexpensive models</title>',
        f'  <desc id="desc">{description}. Claude Haiku 5.5 is also the judge.</desc>',
        f'  <rect width="720" height="750" rx="10" fill="{colors["surface"]}"/>',
        *("  " + part for part in parts),
        "</svg>",
        "",
    ])


def main():
    rows = figures()
    print_report(rows)
    for theme in THEMES:
        path = IMAGES / f"cheap-models-{theme}.svg"
        path.write_text(chart(rows, theme), encoding="utf-8", newline="\n")
        print(f"wrote {path.relative_to(ROOT).as_posix()}")


if __name__ == "__main__":
    main()
