# Calisto (Windows)

A small floating moon that finds the abbreviations in the window you are reading and explains them.

## For users

Download `Calisto.exe` and run it. The first screen explains the two permissions (reading the active window's text through Windows UI Automation when you press Scan, and internet access for lookups and update checks) and offers **Install** or **Run without installing**. Installing copies Calisto to your user folder, adds it to the Start menu (type "Calisto" to find it) and to Apps and features for removal. No administrator rights needed.

- A small, half-transparent Calisto moon floats at the right edge of the screen, two thirds of the way down. **Drag it** anywhere. **Left-click** it to scan the window you were working in; **right-click** it for the menu.
- The menu is a translucent panel; drag it by its header. The slider at the bottom sets how see-through the icon and menu are.
- **Scan**: green radar rings ripple out from the icon while Calisto reads the window, then the menu opens with the list of abbreviations found. Click one (small triangle: right = closed, down = open) to see what it means.
- **Check for updates**, **Settings** (optional Claude API key, update URL) and **Close Calisto** are in the same menu.

## How meanings are found (no key needed)

1. Candidates are collected from the built-in dictionary (English, chat slang, Russian/CIS official terms), Wiktionary and Wikipedia, queried in the language of the surrounding text (detected from the script and common words) plus English.
2. Each candidate is scored: initials that spell the abbreviation, letters appearing in order (`bs` -> `bullshit`), vocabulary shared between the candidate's description and the sentence around the abbreviation (`PM` near "parliament" -> Prime minister; near "online" -> Private message), the text's language, and a slang boost when the shorthand was typed in lower case.
3. The best candidate is shown with a one-sentence description; up to two alternatives follow after "Also:".

With a Claude API key in Settings, the same candidates plus the sentence are sent to Claude (`claude-opus-5-5`) for a more precise answer.

Diagnostics: `Calisto.exe --lookup ИИН "укажите ИИН заявителя"` writes the result to `%TEMP%\calisto-lookup.txt`.

## Build

Requirements: Windows 10/11, .NET 9 SDK.

```bash
dotnet build -c Release
```

## Publish the downloadable file

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

The result is `publish/Calisto.exe` (about 60 MB, needs nothing installed on the target PC).

## Icon

`calisto.ico` and `calisto.png` are generated from `make_icon.py` (Pillow). The floating icon in the app is the same artwork drawn in XAML (`PlanetIcon.xaml`).

## Releasing updates

The repository is https://github.com/graftimdev/Calisto. "Check for updates" in the app reads its latest release.

To publish a new version:

1. Commit your changes: `git add -A` then `git commit -m "What changed"` then `git push`.
2. Tag it with the new version and push the tag: `git tag v1.1.0` then `git push origin v1.1.0`.
3. GitHub Actions (`.github/workflows/release.yml`) builds `Calisto.exe` with that version number and creates the release with the file attached, in about three minutes.
4. Users press "Check for updates" and get a link to the new `Calisto.exe`. Running it over an existing installation updates it.

Friends can always download the newest version from https://github.com/graftimdev/Calisto/releases/latest.

## Command-line switches

| Switch | Effect |
|---|---|
| `--install` | Install silently (copy, Start menu shortcut, uninstall entry) without launching |
| `--uninstall` | Remove Calisto (add `--silent` to skip the confirmation) |
| `--selftest notepad` | Scan that process's window and write the hits to `%TEMP%\calisto-selftest.txt` |
| `--uitest notepad` | Drive the menu, scan and results on screen, saving screenshots to `%TEMP%\calisto-uitest\` |

## Limits

Text drawn as pixels only (games, some PDF viewers, remote desktop) cannot be read without OCR. Settings, including the API key, live unencrypted in `%APPDATA%\Calisto\settings.json`.
