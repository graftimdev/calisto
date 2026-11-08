<p align="center">
  <img src="calisto.png" width="96" alt="Calisto">
</p>

<h1 align="center">Calisto</h1>

<p align="center">Select any word, press a key, get its meaning. A tiny moon that floats on your screen and explains what you are reading.</p>

## Download

**[Download Calisto.exe](https://github.com/graftimdev/Calisto/releases/latest)** (Windows 10 or 11, 64-bit, nothing else to install)

Run the file. Because it is a small independent app without a paid signing certificate, Windows may show "Windows protected your PC": click **More info**, then **Run anyway**.

The first screen explains what Calisto needs permission for and offers **Install** (adds Calisto to the Start menu, no administrator rights needed) or **Run without installing**.

## How to use it

- **Select a word** in any app and press **Ctrl+Alt+C**. A small card appears next to the word with its meaning in context. Works for ordinary words, names, abbreviations and chat shorthand, in any language. Click anywhere or press Esc to dismiss the card.
- **Passive scan** (on by default) quietly marks every abbreviation in the window you are working in with a tiny green **def** tag, placed above the end of the word so it never covers the text. Click a tag to see the meaning.
- The **Calisto moon** floats at the right edge of your screen. Drag it anywhere. Click it for the menu: Add API key, Settings, Check for updates, Close Calisto. The menu can be dragged by its header.
- **Settings**: opacity of the moon and menu, passive scan on or off, the language definitions should be in (Auto picks it from the text), the lookup hotkey (click the box and press the combination you want; unusable combinations are refused with an explanation), the offline dictionary download, and **Contribute a definition**.

## Where the meanings come from

No account or API key is needed. Calisto combines:

1. A built-in dictionary of common abbreviations, chat slang and official terms.
2. [Wiktionary](https://www.wiktionary.org) and [Wikipedia](https://www.wikipedia.org), searched in the language of the text you are reading (or the language you picked).
3. Definitions contributed by other Calisto users, and an optional offline dictionary pack.

Candidates are ranked by how well they fit the sentence around the word: for abbreviations, whether the initials match and which words are shared with the surrounding text; for words, the dictionary senses in order, with the context deciding between them. If you add a Claude API key in the menu, the sentence is also sent to Claude for an even more precise answer. This is optional.

## Working offline

Every meaning you look up is saved on your PC, so it is available again without internet. In Settings you can download the English offline dictionary pack for thousands more abbreviations. Community contributions are downloaded once a day.

## Contributing a definition

Settings, **Contribute a definition**: enter the word and its meaning. Calisto checks the wording and opens a pre-filled GitHub issue; press "Submit new issue" there (a free GitHub account is needed) and the entry is added to the shared dictionary that every Calisto user receives.

## Privacy

- Calisto reads the text of a window only for the passive scan of the window you are working in (which you can turn off) and when you press the hotkey, using the same Windows interface that screen readers use. It never takes screenshots, never logs keystrokes and never reads windows in the background while you are away.
- When you look a word up, only that word is sent to Wiktionary and Wikipedia. The surrounding sentence leaves your computer only if you have added a Claude API key.
- "Check for updates" asks GitHub for the newest version only when you choose it. Community definitions are fetched from GitHub once a day.
- Nothing is collected, stored or shared by the author. Settings and the offline cache live in `%APPDATA%\Calisto` on your own PC.

## Updating and removing

- **Update:** click the moon, choose *Check for updates*, download the new file and run it. It replaces the installed copy.
- **Remove:** Windows Settings, Apps, *Calisto*, Uninstall. Or run `Calisto.exe --uninstall`.

## Limits

Calisto can read apps that expose their text to Windows: browsers, Office, PDF readers, Notepad, most modern apps. Text drawn only as pixels (games, some video or remote desktop windows) cannot be read; for those, select nothing and the hotkey falls back to copying the selection.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download). Clone the repository and run:

```
dotnet build -c Release
```

To produce the single downloadable file:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Issues and ideas are welcome on the [issue tracker](https://github.com/graftimdev/Calisto/issues).
