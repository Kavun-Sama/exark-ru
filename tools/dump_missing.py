from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "localization" / "source.en.json"
TRANSLATIONS = ROOT / "localization" / "translations.ru.json"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("tables", nargs="*")
    parser.add_argument("--start", type=int, default=0)
    parser.add_argument("--count", type=int)
    parser.add_argument("--summary", action="store_true")
    args = parser.parse_args()

    source = json.loads(SOURCE.read_text(encoding="utf-8"))
    translations = json.loads(TRANSLATIONS.read_text(encoding="utf-8"))

    table_names = args.tables or list(source)
    missing_by_table: dict[str, list[dict[str, object]]] = {}
    for table_name in table_names:
        translated = translations.get(table_name, {})
        missing_by_table[table_name] = [
            row for row in source[table_name] if str(row["id"]) not in translated
        ]

    if args.summary:
        for table_name in table_names:
            total = len(source[table_name])
            missing = len(missing_by_table[table_name])
            print(f"{table_name}: {total - missing}/{total} translated, {missing} missing")
        return

    for table_name in table_names:
        rows = missing_by_table[table_name]
        end = None if args.count is None else args.start + args.count
        print(f"### {table_name} missing [{args.start}:{end or len(rows)}] / {len(rows)}")
        for row in rows[args.start:end]:
            text = row["text"].replace("\r", "\\r").replace("\n", "\\n")
            print(f"{row['id']}\t{row['key']}\t{text}")


if __name__ == "__main__":
    main()
