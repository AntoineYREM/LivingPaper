#!/usr/bin/env python3
"""Fails when a NuGet dependency (direct or transitive) has a license that is not on the allowlist.

Run after `dotnet restore`. It reads obj/project.assets.json of every project, finds the .nuspec of each package in the
NuGet cache and checks its license. With --write NOTICES.md it also writes the list of the packages and their licenses.
The allowlist holds permissive licenses that can be combined with the AGPL-3.0.
"""
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ALLOWED = {
    "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "0BSD", "Unlicense", "CC0-1.0", "Zlib",
    "MS-PL", "Unicode-3.0", "Unicode-DFS-2016", "PostgreSQL", "BlueOak-1.0.0",
}
# Packages published before SPDX expressions, with only a license URL. Each entry was checked by hand.
URL_LICENSES = {
    "https://aka.ms/deprovlicense": "MIT",
    "https://go.microsoft.com/fwlink/?linkid=2028464": "MIT",
    "https://licenses.nuget.org/mit": "MIT",
    "https://licenses.nuget.org/apache-2.0": "Apache-2.0",
}


def read_license(nuspec: pathlib.Path) -> str:
    root = ET.parse(nuspec).getroot()
    ns = {"n": root.tag.split("}")[0].strip("{")}
    meta = root.find("n:metadata", ns)
    lic = meta.find("n:license", ns)
    if lic is not None and (lic.text or "").strip():
        return lic.text.strip()
    url = meta.find("n:licenseUrl", ns)
    if url is not None and (url.text or "").strip():
        return URL_LICENSES.get(url.text.strip(), "UNKNOWN (" + url.text.strip() + ")")
    return "UNKNOWN (no license declared)"


def ok(expression: str) -> bool:
    parts = [p for p in re.split(r"[()\s]+|\bOR\b|\bAND\b|\bWITH\b", expression) if p]
    return bool(parts) and all(p in ALLOWED for p in parts)


def main() -> int:
    write = sys.argv[sys.argv.index("--write") + 1] if "--write" in sys.argv else None
    found = {}
    for assets in pathlib.Path(".").glob("**/obj/project.assets.json"):
        data = json.loads(assets.read_text(encoding="utf-8"))
        folders = [pathlib.Path(p) for p in data.get("packageFolders", {})]
        for key, lib in data.get("libraries", {}).items():
            if lib.get("type") != "package":
                continue
            name, version = key.split("/")
            for folder in folders:
                nuspec = folder / lib["path"] / (name.lower() + ".nuspec")
                if nuspec.exists():
                    found[(name, version)] = read_license(nuspec)
                    break
            else:
                found[(name, version)] = "UNKNOWN (package not found in the cache)"
    if not found:
        print("No project.assets.json found: run `dotnet restore` first.")
        return 2
    bad = {k: v for k, v in found.items() if not ok(v)}
    for (name, version), lic in sorted(found.items()):
        print(("FAIL " if (name, version) in bad else "ok   ") + f"{name} {version}: {lic}")
    if write:
        lines = ["| Package | Version | License |", "|---|---|---|"]
        lines += [f"| {n} | {v} | {l} |" for (n, v), l in sorted(found.items())]
        pathlib.Path(write).write_text("\n".join(lines) + "\n", encoding="utf-8")
    if bad:
        print(f"\n{len(bad)} package(s) with a license that is not on the allowlist.")
        return 1
    print(f"\n{len(found)} packages, all with an allowed license.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
