from __future__ import annotations

import json
import re
import sys
import argparse
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = ROOT / "localization" / "source.en.json"
TRANSLATION_PATH = ROOT / "localization" / "translations.ru.json"
TAG_RE = re.compile(r"<[^>]+>")
PLACEHOLDER_RE = re.compile(r"\{[^{}]+\}")


def tokens(text: str) -> tuple[list[str], list[str]]:
    return sorted(TAG_RE.findall(text)), sorted(PLACEHOLDER_RE.findall(text))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--partial", action="store_true")
    args = parser.parse_args()

    source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
    translations = json.loads(TRANSLATION_PATH.read_text(encoding="utf-8"))
    errors: list[str] = []
    checked = 0

    for table_name, rows in source.items():
        translated_table = translations.get(table_name, {})
        for row in rows:
            key_id = str(row["id"])
            original = row["text"]
            translated = translated_table.get(key_id)
            checked += 1

            if translated is None:
                if not args.partial:
                    errors.append(f"missing: {table_name}/{key_id} {row['key']}")
                continue
            if original and not translated:
                errors.append(f"empty: {table_name}/{key_id} {row['key']}")
            if tokens(original) != tokens(translated):
                errors.append(f"tokens: {table_name}/{key_id} {row['key']}")

    source_tables = set(source)
    extra_tables = set(translations) - source_tables
    if extra_tables:
        errors.append(f"extra tables: {sorted(extra_tables)}")

    translated_count = sum(len(table) for table in translations.values())
    print(f"Checked {checked} source entries; translation contains {translated_count} entries.")

    if errors:
        print(f"Validation failed with {len(errors)} issue(s):")
        for error in errors[:200]:
            print(f"  {error}")
        if len(errors) > 200:
            print(f"  ... and {len(errors) - 200} more")
        return 1

    if args.partial:
        print("Translated entries are structurally valid; tags and placeholders match.")
    else:
        print("Translation is complete; tags and placeholders match.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
