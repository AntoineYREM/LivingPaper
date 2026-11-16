#!/usr/bin/env python3
"""Prints the notes of one version from CHANGELOG.md: what is under "## [X.Y.Z] - date", up to the next "## [".

Usage: changelog-section.py 0.2.0
The release workflow uses it for the notes of the GitHub Release. It fails (exit 1) when the version has no section or an
empty one, so a release is never published without notes.
"""
import re
import sys
from pathlib import Path


def section(changelog: str, version: str) -> str:
    lines = changelog.splitlines()
    heading = re.compile(r"^## \[" + re.escape(version) + r"\]")
    start = next((i for i, line in enumerate(lines) if heading.match(line)), None)
    if start is None:
        raise SystemExit(f"CHANGELOG.md has no section [{version}]")
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## [")), len(lines))
    notes = "\n".join(lines[start + 1 : end]).strip()
    if not notes:
        raise SystemExit(f"The section [{version}] of CHANGELOG.md is empty")
    return notes


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: changelog-section.py X.Y.Z")
    print(section(Path(__file__).resolve().parents[2].joinpath("CHANGELOG.md").read_text(encoding="utf-8"), sys.argv[1]))
