from __future__ import annotations

import argparse
import csv
import io
import json
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EFFECT_LABEL_KEYS = {
    "UI_TOOLTIP_TRIGGERED": "UI_EFFECT_TYPE_TRIGGERED",
    "UI_TOOLTIP_PASSIVE": "UI_EFFECT_TYPE_PASSIVE",
}


def main() -> None:
    parser = argparse.ArgumentParser(description="Export Russian localization for developer integration.")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts" / "Exark-Russian-Localization-Source-v1.0.3.zip")
    args = parser.parse_args()

    source = json.loads((ROOT / "localization" / "source.en.json").read_text(encoding="utf-8"))
    translations = json.loads((ROOT / "localization" / "translations.ru.json").read_text(encoding="utf-8"))
    ui_by_key = {row["key"]: row for row in source["TextUI"]}
    tables: dict[str, list[dict[str, str]]] = {}
    for table_name, rows in source.items():
        table = []
        for row in rows:
            entry_id = str(row["id"])
            russian = translations[table_name][entry_id]
            label_key = EFFECT_LABEL_KEYS.get(row["key"]) if table_name == "TextUI" else None
            if label_key:
                label_id = str(ui_by_key[label_key]["id"])
                russian = translations["TextUI"][label_id]
            table.append({"key": row["key"], "id": entry_id, "en": row["text"], "ru": russian})
        tables[table_name] = table

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("README.md", (ROOT / "docs" / "official-localization.md").read_bytes())
        archive.writestr("localization.by-key.json", json.dumps(tables, ensure_ascii=False, indent=2) + "\n")
        for table_name, rows in tables.items():
            buffer = io.StringIO(newline="")
            writer = csv.DictWriter(buffer, fieldnames=["key", "id", "en", "ru"])
            writer.writeheader()
            writer.writerows(rows)
            archive.writestr(f"csv/{table_name}.csv", buffer.getvalue().encode("utf-8-sig"))

    print(f"Exported {sum(len(rows) for rows in tables.values())} entries in {len(tables)} tables: {args.output}")


if __name__ == "__main__":
    main()
