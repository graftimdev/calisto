"""Builds an offline dictionary pack (pack-<lang>.json) of abbreviations from Wiktionary.

Usage:  python tools/build_pack.py en pack-en.json

It lists the members of Wiktionary's abbreviation categories, fetches their wikitext 50 pages per request and keeps
the definition lines ("# {{initialism of|en|...}}", "# {{abbreviation of|en|...}}", plain glosses). One request per
second, as Wikimedia asks. Set CALISTO_PACK_LIMIT=200 to build a small sample for testing.
"""
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import date

LANG = sys.argv[1] if len(sys.argv) > 1 else "en"
OUT = sys.argv[2] if len(sys.argv) > 2 else f"pack-{LANG}.json"
LIMIT = int(os.environ.get("CALISTO_PACK_LIMIT", "0") or 0)
UA = "CalistoPackBuilder/1.1 (https://github.com/graftimdev/Calisto) python-urllib"
API = f"https://{LANG}.wiktionary.org/w/api.php"

CATEGORIES = {
    "en": ["Category:English initialisms", "Category:English acronyms", "Category:English abbreviations", "Category:English clippings"],
    "ru": ["Категория:Аббревиатуры/ru", "Категория:Сокращения/ru"],
}[LANG]
SECTION_NAME = {"en": "English", "ru": "Русский"}[LANG]

# "{{initialism of|en|X}}" style templates: the target is the first positional parameter after the language code.
OF_TEMPLATES = ("initialism of", "init of", "abbreviation of", "abbr of", "abbrev of", "acronym of", "clipping of", "clip of",
                "short for", "contraction of", "ellipsis of", "alternative form of", "alt form", "synonym of", "syn of", "alternative spelling of")
DROP_TEMPLATES = ("lb", "lbl", "label", "cln", "c", "C", "topics", "top", "catlangname", "rfdef", "rfex", "senseid", "defdate", "anchor", "attn", "n-g", "non-gloss definition", "ngd")


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


def language_section(wikitext):
    """The part of the page under the "== <language> ==" heading (whole page when there is no such heading)."""
    m = re.search(r"^==\s*" + re.escape(SECTION_NAME) + r"\s*==\s*$", wikitext, re.M)
    if not m:
        return wikitext if LANG != "en" else ""
    rest = wikitext[m.end():]
    n = re.search(r"^==[^=].*?==\s*$", rest, re.M)
    return rest[:n.start()] if n else rest


def template_text(body):
    """Renders one {{...}} template body (without the braces) as plain text, or "" to drop it."""
    parts = [p for p in body.split("|")]
    name = parts[0].strip()
    if name in DROP_TEMPLATES:
        return ""
    positional = [p for p in parts[1:] if "=" not in p]
    named = dict(p.split("=", 1) for p in parts[1:] if "=" in p)
    if name in OF_TEMPLATES:
        target = positional[1] if len(positional) > 1 else (positional[0] if positional else "")
        gloss = named.get("t") or named.get("gloss") or (positional[3] if len(positional) > 3 else "")
        return target + (f" ({gloss})" if gloss else "")
    if name in ("l", "m", "link", "mention", "ll", "w", "W", "pedia", "wikipedia", "taxlink", "vern"):
        if name in ("w", "W", "pedia", "wikipedia"):
            return positional[0] if positional else ""
        text = positional[2] if len(positional) > 2 and positional[2] else (positional[1] if len(positional) > 1 else "")
        gloss = named.get("t") or named.get("gloss")
        return text + (f" ({gloss})" if gloss else "")
    if name in ("gloss", "gl", "qualifier", "q", "qual", "i", "sense"):
        return "(" + ", ".join(positional) + ")" if positional else ""
    if name in ("place",):
        return positional[1].replace("/", " ") if len(positional) > 1 else ""
    return positional[-1].strip() if positional else ""


def plain(line):
    """Turns one definition line of wikitext into plain text."""
    # Links first: they may sit inside template parameters and contain "|" themselves.
    line = re.sub(r"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", r"\1", line)
    # Templates, innermost first.
    for _ in range(6):
        new = re.sub(r"\{\{([^{}]*)\}\}", lambda m: template_text(m.group(1)), line)
        if new == line:
            break
        line = new
    line = re.sub(r"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", r"\1", line)
    line = re.sub(r"\b[wW]:", "", line)
    line = re.sub(r"'{2,}", "", line)
    line = re.sub(r"<ref[^>]*/>|<ref[^>]*>.*?</ref>|<[^>]+>", "", line)
    line = re.sub(r"\s+", " ", line).strip(" :;,")
    line = re.sub(r"\s+([,.;:)])", r"\1", line)
    return line


def definitions(wikitext):
    section = language_section(wikitext)
    for raw in section.split("\n"):
        if not raw.startswith("# ") and not (raw.startswith("#") and len(raw) > 1 and raw[1] not in "#*:"):
            continue
        text = plain(raw.lstrip("#").strip())
        if 2 <= len(text) <= 240 and re.search(r"\w", text) and not text.lower().startswith(("see ", "used ")):
            yield text[0].upper() + text[1:]


def main():
    titles = []
    seen = set()
    for cat in CATEGORIES:
        print("listing", cat, file=sys.stderr)
        for t in members(cat):
            if t not in seen and 2 <= len(t) <= 12 and " " not in t:
                seen.add(t)
                titles.append(t)
            if LIMIT and len(titles) >= LIMIT:
                break
        if LIMIT and len(titles) >= LIMIT:
            break
    print(len(titles), "titles", file=sys.stderr)

    entries = {}
    batch_size = 50
    for i in range(0, len(titles), batch_size):
        batch = titles[i:i + batch_size]
        data = get({"action": "query", "prop": "revisions", "rvprop": "content", "rvslots": "main", "titles": "|".join(batch)})
        for page in data["query"]["pages"].values():
            title = page.get("title", "")
            revs = page.get("revisions") or []
            wikitext = (revs[0].get("slots", {}).get("main", {}).get("*", "") if revs else "")
            defs = []
            for d in definitions(wikitext):
                key = d.lower().rstrip(".")
                if key != title.lower() and key not in {x.lower().rstrip(".") for x in defs}:
                    defs.append(d)
            if defs:
                entries[title] = defs[:6]
        if (i // batch_size) % 20 == 0:
            print(f"{i}/{len(titles)} -> {len(entries)} entries", file=sys.stderr)

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({"lang": LANG, "built": date.today().isoformat(), "entries": entries}, f, ensure_ascii=False, separators=(",", ":"))
    print("wrote", OUT, len(entries), "entries", file=sys.stderr)


if __name__ == "__main__":
    main()
