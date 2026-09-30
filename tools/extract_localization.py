from __future__ import annotations

import json
from pathlib import Path

import UnityPy


GAME_ROOT = Path(r"D:\Tools\Steam\steamapps\common\Exark")
AA_ROOT = GAME_ROOT / "Exark_Data" / "StreamingAssets" / "aa" / "StandaloneWindows64"
ENGLISH_BUNDLE = AA_ROOT / "localization-string-tables-english(en)_assets_all.bundle"
SHARED_BUNDLE = AA_ROOT / "localization-assets-shared_assets_all.bundle"
OUTPUT = Path(__file__).resolve().parents[1] / "localization" / "source.en.json"


def load_shared_keys() -> dict[int, tuple[str, str]]:
    result: dict[int, tuple[str, str]] = {}
    environment = UnityPy.load(str(SHARED_BUNDLE))

    for obj in environment.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        tree = obj.read_typetree()
        table_name = tree.get("m_TableCollectionName")
        entries = tree.get("m_Entries")
        if not table_name or not isinstance(entries, list):
            continue
        if not table_name.startswith("Text"):
            continue

        for entry in entries:
            result[int(entry["m_Id"])] = (table_name, entry["m_Key"])

    return result


def main() -> None:
    shared_keys = load_shared_keys()
    environment = UnityPy.load(str(ENGLISH_BUNDLE))
    tables: dict[str, list[dict[str, object]]] = {}

    for obj in environment.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        tree = obj.read_typetree()
        name = tree.get("m_Name", "")
        if not name.endswith("_en"):
            continue

        table_name = name[:-3]
        rows: list[dict[str, object]] = []
        for entry in tree.get("m_TableData", []):
            key_id = int(entry["m_Id"])
            shared_table, key = shared_keys.get(key_id, (table_name, f"ID_{key_id}"))
            if shared_table != table_name:
                key = f"{shared_table}:{key}"
            rows.append(
                {
                    "id": key_id,
                    "key": key,
                    "text": entry.get("m_Localized", ""),
                }
            )
        tables[table_name] = rows

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(tables, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote {sum(len(rows) for rows in tables.values())} entries to {OUTPUT}")


if __name__ == "__main__":
    main()

