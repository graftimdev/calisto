<p align="center">
  <img src="calisto.png" width="96" alt="Calisto">
</p>

<h1 align="center">Calisto</h1>

<p align="center">A tiny moon that floats on your screen and explains the abbreviations you are reading.</p>

## Download

**[Download Calisto.exe](https://github.com/graftimdev/Calisto/releases/latest)** (Windows 10 or 11, 64-bit, nothing else to install)

Run the file. Because it is a small independent app without a paid signing certificate, Windows may show "Windows protected your PC": click **More info**, then **Run anyway**.

The first screen explains what Calisto needs permission for and offers **Install** (adds Calisto to the Start menu, no administrator rights needed) or **Run without installing**.

## How to use it

- A small, half-transparent Calisto moon appears on the right edge of your screen. Drag it anywhere you like.
- **Left-click the moon** to scan the window you were just working in. Green rings ripple across the screen while Calisto reads it, then a list of every abbreviation it found opens next to the moon.
- **Click an abbreviation** in the list to see what it means in that context. The small triangle points right when closed and down when open.
- **Right-click the moon** for the menu: scan, check for updates, settings, close. The slider at the bottom sets how transparent the moon and the menu are. The menu can be dragged by its header.

Calisto works in any language. It understands common abbreviations, chat shorthand like *idk* or *bs*, and official terms such as *ИИН* or *НДС*, and it uses the words around an abbreviation to pick the right meaning: *PM* next to "parliament" is the Prime minister, *PM* next to "send me" is a private message.

## Where the meanings come from

No account or API key is needed. Calisto combines:

1. A built-in dictionary of common abbreviations, chat slang and official terms.
2. [Wiktionary](https://www.wiktionary.org) and [Wikipedia](https://www.wikipedia.org), searched in the language of the text you are reading.

Candidates are ranked by how well their initials match the abbreviation and how many words they share with the surrounding sentence. If you add a Claude API key in Settings, the sentence is also sent to Claude for an even more precise answer. This is optional.

## Privacy

- Calisto reads the text of a window only when you press Scan, using the same Windows interface that screen readers use. It never takes screenshots, never logs keystrokes and never scans in the background.
- When you click an abbreviation, only that abbreviation is sent to Wiktionary and Wikipedia. The surrounding sentence leaves your computer only if you have added a Claude API key.
- "Check for updates" asks GitHub for the newest version only when you choose it.
- Nothing is collected, stored or shared by the author. Settings live in `%APPDATA%\Calisto\settings.json` on your own PC.

## Updating and removing

- **Update:** right-click the moon, choose *Check for updates*, download the new file and run it. It replaces the installed copy.
- **Remove:** Windows Settings, Apps, *Calisto*, Uninstall. Or run `Calisto.exe --uninstall`.

## Limits

Calisto can read apps that expose their text to Windows: browsers, Office, PDF readers, Notepad, most modern apps. Text drawn only as pixels (games, some video or remote desktop windows) cannot be read.

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
