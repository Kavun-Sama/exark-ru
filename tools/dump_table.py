from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "localization" / "source.en.json"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("tables", nargs="+")
    parser.add_argument("--start", type=int, default=0)
    parser.add_argument("--count", type=int)
    args = parser.parse_args()

    data = json.loads(SOURCE.read_text(encoding="utf-8"))
    for table_name in args.tables:
        rows = data[table_name]
        end = None if args.count is None else args.start + args.count
        print(f"### {table_name} [{args.start}:{end or len(rows)}]")
        for row in rows[args.start:end]:
            text = row["text"].replace("\r", "\\r").replace("\n", "\\n")
            print(f"{row['id']}\t{row['key']}\t{text}")


if __name__ == "__main__":
    main()
