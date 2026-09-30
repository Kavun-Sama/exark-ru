from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = ROOT / "localization" / "source.en.json"
RU_DIR = ROOT / "localization" / "ru"
OUTPUT_PATH = ROOT / "localization" / "translations.ru.json"


def main() -> None:
    source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
    merged: dict[str, dict[str, str]] = {table_name: {} for table_name in source}
    source_by_id: dict[tuple[str, str], str] = {}

    for table_name, rows in source.items():
        for row in rows:
            source_by_id[(table_name, str(row["id"]))] = row["text"]

    for table_name in source:
        path = RU_DIR / f"{table_name}.jsonl"
        if not path.exists():
            continue

        table = merged[table_name]
        for line_number, raw_line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            row = json.loads(line)
            key = str(row["id"])
            if key in table:
                raise ValueError(f"Duplicate id {key} in {path}:{line_number}")
            table[key] = row["text"]

    translations_by_source: dict[str, set[str]] = {}
    for table_name, table in merged.items():
        for key_id, translated in table.items():
            original = source_by_id.get((table_name, key_id))
            if original:
                translations_by_source.setdefault(original, set()).add(translated)

    reusable = {
        original: next(iter(translations))
        for original, translations in translations_by_source.items()
        if len(translations) == 1
    }

    propagated = 0
    for table_name, rows in source.items():
        table = merged[table_name]
        for row in rows:
            key_id = str(row["id"])
            if key_id in table:
                continue
            translated = reusable.get(row["text"])
            if translated is not None:
                table[key_id] = translated
                propagated += 1

    OUTPUT_PATH.write_text(
        json.dumps(merged, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    count = sum(len(table) for table in merged.values())
    nonempty_tables = sum(bool(table) for table in merged.values())
    print(
        f"Merged {count} entries from {nonempty_tables} tables into {OUTPUT_PATH} "
        f"({propagated} exact duplicates propagated)"
    )


if __name__ == "__main__":
    main()
