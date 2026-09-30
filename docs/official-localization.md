# Russian localization handoff

This package contains 2,800 Russian translations across 17 string tables for Exark.
It contains localization data only; no game binaries, fonts, or extracted art are included.

## Files

- `localization.by-key.json`: tables containing `key`, `id`, `en`, and `ru` for every entry.
- `csv/*.csv`: one UTF-8 CSV with a BOM per table, with the same four columns.

IDs are decimal strings to avoid precision loss in tools that parse JSON numbers
as floating-point values. Match entries by table and key, then confirm their IDs
against the current project's shared table data. The English text provides context
and a baseline for detecting changed or newly added source strings.

These are interchange files, not native Unity `.asset` files or a guaranteed
drop-in import schema. The CSV columns can be mapped to the project's localization
import workflow. The repository also includes the original per-table JSONL files
and the fan patcher's ID-indexed catalog under `localization/`.

## Integration

1. Add a Russian locale (`ru`) and populate its string tables using the supplied keys.
2. Preserve numeric placeholders, rich-text tags, sprite references, and Smart String
   settings from the original entries.
3. Use fonts with Cyrillic coverage, including bold text and inline font overrides.
   The fan patch uses a Windows font fallback; that font is not included in this package.
4. Check text wrapping, button widths, tooltips, and text animation in the native build.

Two Russian entries intentionally differ from the original sprite tags:
`TextUI/UI_TOOLTIP_TRIGGERED` and `TextUI/UI_TOOLTIP_PASSIVE` use the Russian text
from `UI_EFFECT_TYPE_TRIGGERED` and `UI_EFFECT_TYPE_PASSIVE`, respectively. Their
English versions are labels baked into `triggered0/1/2` and `passive0/1/2` sprites.
Use these text labels or provide localized sprite assets. The fan patch makes the
same substitution at runtime; the handoff contains the final text directly.

Other sprite graphics, including tag badges, may contain English lettering and
require an asset localization pass. Translating every string-table entry does not
translate lettering baked into images.

## Validation and review

The fan patch's catalog has passed checks for completeness, tags, and placeholders.
Runtime regression checks cover tooltip/button templates, effect labels, patch
idempotence, and uninstall restoration. The current fan patch was tested in-game
by its author; official integration still needs review in the developer's build.

The source text and keys reflect the installed game snapshot used for this translation.
Reconcile them with the latest project before importing. Translation style,
terminology, attribution, and permission to include the community translation
can be agreed with the contributor before shipping.

Project: https://github.com/Kavun-Sama/exark-ru
