"""Builds an offline dictionary pack (pack-<lang>.json) of abbreviations from Wiktionary.

Usage:  python tools/build_pack.py en pack-en.json

It lists the members of Wiktionary's abbreviation categories, fetches their plain-text definitions in batches
and keeps the "Initialism of X" / "Abbreviation of X" lines. One request per second, as Wikimedia asks.
"""
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import date

LANG = sys.argv[1] if len(sys.argv) > 1 else "en"
OUT = sys.argv[2] if len(sys.argv) > 2 else f"pack-{LANG}.json"
UA = "CalistoPackBuilder/1.0 (https://github.com/graftimdev/Calisto) python-urllib"
API = f"https://{LANG}.wiktionary.org/w/api.php"

CATEGORIES = {
    "en": ["Category:English initialisms", "Category:English acronyms", "Category:English abbreviations", "Category:English clippings"],
    "ru": ["Категория:Аббревиатуры/ru", "Категория:Сокращения/ru"],
}[LANG]

DEF_RE = re.compile(
    r"(?:initialism|abbreviation|acronym|abbr\.?|short|clipping|contraction|сокращение|сокр\.?|аббревиатура)\s*(?:of|for|from|от)?\s*[:\-–—]?\s*(?P<x>[^.;\n(◆]{2,90})",
    re.I)
SKIP_SECTIONS = {"etymology", "pronunciation", "anagrams", "see also", "references", "synonyms", "antonyms", "translations",
                 "derived terms", "related terms", "usage notes", "further reading", "hyponyms", "hypernyms", "quotations",
                 "alternative forms", "coordinate terms", "произношение", "этимология", "перевод", "синонимы", "антонимы"}


def get(params):
    params = dict(params, format="json")
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                data = json.load(r)
            time.sleep(1.0)
            return data
        except Exception as e:  # noqa: BLE001
            print("retry", attempt, e, file=sys.stderr)
            time.sleep(5 * (attempt + 1))
    raise SystemExit("giving up")


def members(category):
    cont = {}
    while True:
        data = get({"action": "query", "list": "categorymembers", "cmtitle": category, "cmlimit": 500, "cmnamespace": 0, **cont})
        for m in data["query"]["categorymembers"]:
            yield m["title"]
        if "continue" in data:
            cont = data["continue"]
        else:
            break


def definitions(extract):
    section = ""
    for raw in extract.split("\n"):
        line = raw.strip()
        if not line:
            continue
        if line.startswith("="):
            name = line.strip("= ").lower()
            if raw.count("=") > 4:  # level 3+
                section = name
            else:
                section = ""
            continue
        if section in SKIP_SECTIONS or len(line) < 4 or len(line) > 240:
            continue
        if not (line[0].isalpha() or line[0].isdigit()):
            continue
        line = re.sub(r"^\(?\d+[.)]\s*", "", line)
        line = re.split(r"\s*[◆◇□]", line)[0].strip()
        m = DEF_RE.search(line)
        if m:
            yield m.group("x").strip().rstrip(".,")


def main():
    titles = []
    seen = set()
    for cat in CATEGORIES:
        print("listing", cat, file=sys.stderr)
        for t in members(cat):
            if t not in seen and 2 <= len(t) <= 12 and " " not in t:
                seen.add(t)
                titles.append(t)
    print(len(titles), "titles", file=sys.stderr)

    entries = {}
    for i in range(0, len(titles), 20):
        batch = titles[i:i + 20]
        data = get({"action": "query", "prop": "extracts", "explaintext": 1, "titles": "|".join(batch)})
        for page in data["query"]["pages"].values():
            title, extract = page.get("title", ""), page.get("extract", "")
            defs = []
            for d in definitions(extract):
                d = d[0].upper() + d[1:]
                if d.lower() != title.lower() and d not in defs:
                    defs.append(d)
            if defs:
                entries[title] = defs[:6]
        if (i // 20) % 25 == 0:
            print(f"{i}/{len(titles)} -> {len(entries)} entries", file=sys.stderr)

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({"lang": LANG, "built": date.today().isoformat(), "entries": entries}, f, ensure_ascii=False, separators=(",", ":"))
    print("wrote", OUT, len(entries), "entries", file=sys.stderr)


if __name__ == "__main__":
    main()
