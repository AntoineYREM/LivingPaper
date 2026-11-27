#!/usr/bin/env python3
"""The figures and the chart of the before-and-after measure, from the published runs (evals/published/before-after/).

    python3 evals/before-after/report.py              prints the tables
    python3 evals/before-after/report.py --chart      also writes docs/images/evals/before-after-{light,dark}.svg

See docs/experiments/before-and-after.md. It reads files of the repository only; nothing is sent anywhere.
"""
import json
import math
import statistics
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNS = ROOT / 'evals' / 'published' / 'before-after'
IMAGES = ROOT / 'docs' / 'images' / 'evals'

MODELS = [('deepseek', 'DeepSeek V4.1 Flash'), ('mistral', 'Mistral Small 3.2')]
VERSIONS = [('v0', 'Start'), ('v1', '+ passages'), ('v2', '+ honesty'), ('v3', '+ two searches')]
BOOKS = [('alice', 'Alice'), ('cyrano', 'Cyrano'), ('bovary', 'Madame Bovary'), ('cayley', 'Miss Cayley')]
CATEGORIES = ['direct', 'new-subject', 'short-follow-up', 'precise-follow-up']


def load():
    """The runs by their label: q95-<model>-<version>-<set>; the run without the book of DeepSeek has an earlier label."""
    runs = {}
    for path in sorted(RUNS.glob('*.json')):
        run = json.loads(path.read_text(encoding='utf-8-sig'))
        label = run['label']
        if label == 'q95-closed-cayley-deepseek-v4.1-flash':
            label = 'q95-deepseek-closed-cayley-facts'
        if label in runs:
            raise SystemExit(f'two runs are labelled {label}')
        runs[label] = run
    return runs


RUN = load()


def results(model, version, sets):
    return {r['questionId']: r for name in sets for r in RUN[f'q95-{model}-{version}-{name}']['results']}


def facts(model, version, book=None):
    return results(model, version, [f'{b}-facts' for b, _ in BOOKS if book in (None, b)])


def percent(values, grades):
    graded = [r for r in values if r['score'] is not None]
    return 100 * sum(r['score'] in grades for r in graded) / len(graded)


RIGHT, INVENTED = (4, 5), (1,)


def difference(model, first, second, grades):
    """The change in points between two versions, question by question, with its 95% interval."""
    a, b = facts(model, first), facts(model, second)
    changes = [int(b[q]['score'] in grades) - int(a[q]['score'] in grades) for q in a]
    mean = statistics.mean(changes)
    half = 1.98 * statistics.stdev(changes) / math.sqrt(len(changes))
    return f'{100 * mean:+.0f} ({100 * (mean - half):+.0f} to {100 * (mean + half):+.0f})'


def table(head, rows):
    print('| ' + ' | '.join(head) + ' |')
    print('|' + '---|' * len(head))
    for row in rows:
        print('| ' + ' | '.join(str(cell) for cell in row) + ' |')
    print()


def tables():
    names = [name for _, name in VERSIONS]
    print('## Right / false, %, 140 factual questions\n')
    table(['Model'] + names, [[label] + [f'{percent(facts(m, v).values(), RIGHT):.0f} / {percent(facts(m, v).values(), INVENTED):.0f}' for v, _ in VERSIONS]
                              for m, label in MODELS])
    print('## Replies by grade (right / partly / without the fact / false)\n')
    table(['Model'] + names, [[label] + [' / '.join(str(sum(r['score'] in g for r in facts(m, v).values())) for g in (RIGHT, (3,), (2,), INVENTED))
                                         for v, _ in VERSIONS] for m, label in MODELS])
    print('## Change between versions, points (95% interval)\n')
    table(['Model', 'Step', 'Right', 'False'], [[label, f'{a} to {b}', difference(m, a, b, RIGHT), difference(m, a, b, INVENTED)]
                                                 for m, label in MODELS for a, b in (('v0', 'v1'), ('v1', 'v2'), ('v2', 'v3'), ('v0', 'v3'))])
    for title, keys, of in (('category', CATEGORIES, lambda m, v, c: [r for r in facts(m, v).values() if r['category'] == c]),
                            ('book', [b for b, _ in BOOKS], lambda m, v, b: facts(m, v, b).values())):
        print(f'## Right / false, %, by {title}\n')
        table(['Model', title.capitalize()] + names,
              [[label, key] + [f'{percent(of(m, v, key), RIGHT):.0f} / {percent(of(m, v, key), INVENTED):.0f}' for v, _ in VERSIONS]
               for m, label in MODELS for key in keys])
    traps = [f'{b}-traps' for b, _ in BOOKS]
    print('## Traps, of 20: grades 5 / 4 / 3 / 2 / 1\n')
    table(['Model'] + names, [[label] + [' / '.join(str(sum(r['score'] == g for r in results(m, v, traps).values())) for g in (5, 4, 3, 2, 1)) for v, _ in VERSIONS]
                              for m, label in MODELS])
    print("## The 12 questions of a reader: right / false\n")
    table(['Model'] + names, [[label] + [' / '.join(str(sum(r['score'] in g for r in results(m, v, ['bovary-reader']).values())) for g in (RIGHT, INVENTED))
                                         for v, _ in VERSIONS] for m, label in MODELS])
    print('## Role, 40 questions to Emma: average (replies graded 2 or less)\n')
    rows = []
    for m, label in MODELS:
        row = [label]
        for v, _ in VERSIONS:
            scores = [r['score'] for r in results(m, v, ['emma-role']).values() if r['score'] is not None]
            row.append(f'{statistics.mean(scores):.2f} ({sum(s <= 2 for s in scores)})')
        rows.append(row)
    table(['Model'] + names, rows)
    print('## Expected passage found, %, factual questions\n')
    found = lambda m, v, c: 100 * statistics.mean(bool(r['trace']['expectedFound']) for r in facts(m, v).values() if c in (None, r['category']))
    table(['Category'] + names[1:], [[c or 'all'] + [f'{found("deepseek", v, c):.0f}' for v, _ in VERSIONS[1:]] for c in [None] + CATEGORIES])
    print('## Per reply, all but the role: prompt tokens, median seconds, price of 1,000 replies (USD)\n')
    prices = json.loads((ROOT / 'evals' / 'prices.json').read_text(encoding='utf-8-sig'))
    prices = prices.get('models', prices)
    rows = []
    for m, label in MODELS:
        for v, name in VERSIONS:
            chosen = [run for key, run in RUN.items() if key.startswith(f'q95-{m}-{v}-') and not key.endswith('emma-role')]
            replies = [r for run in chosen for r in run['results']]
            spent = {k: sum(run['tokens']['chat'].get(k, 0) for run in chosen) for k in ('input', 'cachedInput', 'output')}
            price = prices[chosen[0]['chat']['model']]
            cost = ((spent['input'] - spent['cachedInput']) * price['input'] + spent['cachedInput'] * price['cachedInput'] + spent['output'] * price['output']) / 1e6
            rows.append([label, name, f"{statistics.mean(r['trace']['replyTokens']['input'] for r in replies):.0f}",
                         f"{statistics.median(r['trace']['seconds'] for r in replies):.1f}", f'{1000 * cost / len(replies):.3f}'])
    table(['Model', 'Version', 'Prompt', 'Seconds', 'USD'], rows)
    print('## Without the book (Miss Cayley, 35 questions): right / false, %\n')
    table(['Model', 'Right', 'False'], [[label, f"{percent(RUN[f'q95-{m}-closed-cayley-facts']['results'], RIGHT):.0f}",
                                         f"{percent(RUN[f'q95-{m}-closed-cayley-facts']['results'], INVENTED):.0f}"] for m, label in MODELS])
    print(f'{len(RUN)} runs, {sum(len(run["results"]) for run in RUN.values())} replies')


# The two series colors are the first two of the palette of the dataviz skill, as in the other charts of the project.
THEMES = {
    'light': dict(surface='#fcfcfb', ink='#0b0b0b', secondary='#52514e', grid='#e3e2de', series=('#2a78d6', '#eb6834')),
    'dark': dict(surface='#1a1a19', ink='#f0efec', secondary='#c3c2b7', grid='#343432', series=('#3987e5', '#d95926')),
}


def chart(theme):
    c = THEMES[theme]
    width, height, top, bottom = 720, 360, 108, 306
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" role="img" '
           f'aria-label="Right and false replies, from the start to the final version, for two models" font-family="system-ui, sans-serif">',
           f'<rect width="{width}" height="{height}" fill="{c["surface"]}"/>']

    def text(x, y, value, size=12, color=None, anchor='start', weight='400'):
        out.append(f'<text x="{x}" y="{y}" font-size="{size}" fill="{color or c["secondary"]}" text-anchor="{anchor}" font-weight="{weight}">{value}</text>')

    def dot(x, y, color):
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="4.5" fill="{color}" stroke="{c["surface"]}" stroke-width="2"/>')

    text(24, 30, 'Answers about the books, from the start to the final version', 16, c['ink'], weight='600')
    text(24, 50, '140 factual questions on four books, % of the replies, graded by a language model')
    for index, (_, label) in enumerate(MODELS):
        x = 28 + index * 180
        dot(x, 68, c['series'][index])
        text(x + 12, 72, label)
    for panel, (title, grades) in enumerate((('Right answers (higher is better)', RIGHT), ('False statements (lower is better)', INVENTED))):
        left = 56 + panel * 354
        right = left + 250
        text(left - 32, top - 14, title, 13, c['ink'], weight='600')
        y_of = lambda value: bottom - (bottom - top) * value / 100
        for value in (0, 25, 50, 75, 100):
            out.append(f'<line x1="{left}" y1="{y_of(value):.1f}" x2="{right}" y2="{y_of(value):.1f}" stroke="{c["grid"]}" stroke-width="1"/>')
            text(left - 8, y_of(value) + 4, f'{value}', 11, anchor='end')
        xs = [left + 14 + i * (right - left - 28) / 3 for i in range(4)]
        for i, x in enumerate(xs):
            text(x, bottom + 18, f'V{i}', 12, anchor='middle')
        series = [[percent(facts(m, v).values(), grades) for v, _ in VERSIONS] for m, _ in MODELS]
        for index, values in enumerate(series):
            points = ' '.join(f'{x:.1f},{y_of(v):.1f}' for x, v in zip(xs, values))
            out.append(f'<polyline points="{points}" fill="none" stroke="{c["series"][index]}" stroke-width="2" stroke-linejoin="round"/>')
            for x, v in zip(xs, values):
                dot(x, y_of(v), c['series'][index])
        # The first and the last value of each line, the upper one above its point and the lower one below.
        for end, x, anchor, shift in ((0, xs[0], 'middle', 0), (3, xs[3] + 10, 'start', 4)):
            upper = max(range(2), key=lambda i: series[i][end])
            for index, values in enumerate(series):
                if end == 0:
                    offset = -10 if index == upper else 19
                else:
                    close = abs(series[0][end] - series[1][end]) < 7
                    offset = shift + ((-6 if index == upper else 8) if close else 0)
                text(x, f'{y_of(values[end]) + offset:.1f}', f'{values[end]:.0f}%', 12, c['ink'], anchor, '600')
    text(24, height - 14, 'V0: whole story · V1: passages of the book · V2: V1 and says when it does not know · V3: V2 and two searches', 11)
    out.append('</svg>')
    return '\n'.join(out) + '\n'


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    tables()
    if '--chart' in sys.argv:
        for name in THEMES:
            (IMAGES / f'before-after-{name}.svg').write_text(chart(name), encoding='utf-8', newline='\n')
            print('written', f'before-after-{name}.svg')
