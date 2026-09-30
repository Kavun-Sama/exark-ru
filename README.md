<p align="center">
  <img src="exark-ru.png" alt="Exark RU" width="512" height="512">
</p>

<p align="center">
  <strong>Неофициальный русский перевод Exark</strong><br>
  <sub>Unofficial Russian translation for Exark</sub>
</p>

<p align="center">
  <a href="#русский">Русский</a> · <a href="#english">English</a>
</p>

---

## Русский

Полный неофициальный перевод Exark на русский язык. Патчер добавляет **Русский (RU)** в штатное меню выбора языка, не заменяя и не удаляя оригинальные локализации.

- 2 800 переведённых строк в 17 таблицах локализации;
- поддержка кириллицы и локализованных TMP-ассетов;
- автоматический поиск установленной Exark в библиотеках Steam;
- резервные копии изменяемых игровых DLL;
- удаление русификатора через тот же патчер.

### Установка

1. Скачайте <code>Exark-RU-v1.0.3.zip</code> из [Releases](https://github.com/Kavun-Sama/exark-ru/releases/latest).
2. Закройте игру и распакуйте архив в любую папку.
3. Запустите <code>ExarkRu.Patcher.exe</code>.
4. В игре откройте **Settings → Language → Русский (RU)**.

Если игра установлена нестандартно:

    .\ExarkRu.Patcher.exe --game "D:\Path\To\Exark"

Удаление:

    .\ExarkRu.Patcher.exe --uninstall

## English

Complete unofficial Russian translation for Exark. The patcher adds **Русский (RU)** to the built-in language selector without replacing or removing any original localization.

- 2,800 translated strings across 17 localization tables;
- Cyrillic and localized TMP asset support;
- automatic Steam library detection;
- automatic backups of modified game DLLs;
- clean uninstall through the same patcher.

### Installation

1. Download <code>Exark-RU-v1.0.3.zip</code> from [Releases](https://github.com/Kavun-Sama/exark-ru/releases/latest).
2. Close the game and extract the archive anywhere.
3. Run <code>ExarkRu.Patcher.exe</code>.
4. Open **Settings → Language → Русский (RU)** in-game.

For a custom install path:

    .\ExarkRu.Patcher.exe --game "D:\Path\To\Exark"

To uninstall:

    .\ExarkRu.Patcher.exe --uninstall

## Build

Requires an installed copy of Exark and .NET SDK.

    python .\tools\merge_translation.py
    python .\tools\validate_translation.py
    dotnet build .\ExarkRu.slnx -c Release
    dotnet run --project .\tests\ExarkRu.Regression -c Release -- . "D:\Tools\Steam\steamapps\common\Exark\Exark_Data\Managed"
    .\tools\build_release.ps1 -Version 1.0.3

Release artifacts are written to <code>artifacts/</code>.

## Official localization handoff

To export the translation as JSON keyed by table/entry and 17 CSV files:

    python .\tools\export_handoff.py

The resulting source archive contains localization data and [integration notes](docs/official-localization.md),
without game binaries or fonts. Two sprite-based effect labels are exported as Russian text.

The regression runner checks tooltip and button templates against the complete catalog,
then checks hook ordering and repeated patching on temporary copies of the game DLLs.
It does not modify the installed game. Visual acceptance requires restarting Exark:
check Cyrillic item descriptions, tooltip stats, the numeric Skip reward, repeated
opening of the Escape menu, and switching from Russian to a built-in language and back.

---

This is an unofficial fan-made localization project. Exark and its assets belong to their respective rights holders. The game itself is not distributed with this project.
