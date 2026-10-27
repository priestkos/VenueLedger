# Venue Ledger

A Dalamud plugin for keeping FFXIV venues in one place and reading their hours against your own clock.

Each venue carries the details you need at the door, and every one of them converts itself:

| field | what it does |
|---|---|
| **Name** | the venue's label in the list and the export |
| **Location** | free text shown to readers, e.g. `Ward 12, Plot 30, Lavender Beds, Mateus` |
| **Address** | optional world / district / ward / plot, used to build a **Lifestream** command |
| **Website** | with Open (game browser) and Copy |
| **Discord** | with Open and Copy |
| **Opens** | wall clock + **its own timezone selector** |
| **Closes** | wall clock + **its own timezone selector** |
| **Notes** | anything else (dress code, cover charge, age limit) |

## What it does

- **A live clock** in the header reads your **system** time and system timezone
  (`system clock: IST · Asia/Kolkata UTC+05:30`), ticking every frame.
- **Every venue's hours are projected onto one board clock** (your system timezone by default), so
  `20:00–02:00 JST` and `21:00–01:00 IST` sit next to each other as `16:30–22:30` and `21:00–01:00 +1d`.
  The venue's own clock is never thrown away — both are shown.
- **Opening and closing each carry their own timezone**, so a venue that opens in JST and closes in
  another zone still resolves to a real instant (a closing clock that lands before the opening instant is
  read as the next day — that is how `20:00 – 02:00` works).
- **Open / closed right now**, with `closes in 4h 12m` or `opens tomorrow at 04:30`.
- **A day strip per venue** on the Board tab: a 24-hour bar with each venue's session drawn in, and a
  crimson line at *now*. Overlaps between venues are visible without arithmetic.
- **Lifestream travel**: given district, ward and plot the plugin builds the command Lifestream's own
  help documents — `/li lavender 1 30`, or `/li mateus lavender 12 30` across worlds — and copies it in
  one click.
- **Output**: the Export tab previews the exact text, then copies it to the clipboard or writes
  `venues-YYYYMMDD-HHmm.txt` next to the plugin config.

## Commands

| command | effect |
|---|---|
| `/venue` | open the ledger |
| `/venue copy` | copy the whole list to the clipboard |
| `/venue export` | write the list to a `.txt` and report the path in chat |
| `/venue list` | print each venue's hours in your timezone to chat |
| `/venue help` | the command list |

## Layout

```
Plugin.cs          entry point, commands, clipboard queue, export file
MainWindow.cs      the window: Ledger / Board / Export
Theme.cs           the K'os Mos palette, pushed around this window only
Configuration.cs   persisted venues + preferences
Venue.cs           the venue record, districts, Lifestream command building   (no Dalamud)
Schedule.cs        timezones, parsing, sessions, status, board, exporter      (no Dalamud)
tools/LogicTests/  offline harness that compiles the two files above
```

`Venue.cs` and `Schedule.cs` reference no Dalamud type, so the harness runs the real logic with no game
and no dev distrib:

```
dotnet run --project tools/LogicTests -c Release          # 173 checks
dotnet run --project tools/LogicTests -c Release -- --show # a real export, printed
```

The sweep at the end of the harness covers 1,764 zone/time combinations (6 zones × 6 zones × 7 opening
times × 7 closing times) and asserts each one produces a session that closes after it opens, stays under
26 hours and converts to the same instant in every zone.

## Theming

The palette is the K'os Mos sheet: smoke `#0a0507`, wine `#3d1a1f`, ember `#d05a26`, cream `#f6e2c6`,
gold `#d9a94e`, with dotted rules instead of solid separators and square corners. Colours are pushed with
`ImRaii` around this window's widgets only — **your global Dalamud style is untouched**, and nothing is
hooked. `preview.html` is a static mock of the same design for reading outside the game.

## Install (dev)

```
dotnet build -c Release
```

Then copy `bin/Release/VenueLedger.dll` and `bin/Release/VenueLedger.json` into
`%APPDATA%\XIVLauncher\devPlugins\VenueLedger\` and enable it under `/xlplugins` → Dev Tools.
Requires .NET 10 SDK and the Dalamud dev distrib; it builds nothing without them.

## Notes / limits

- Lifestream's plot form covers houses. Apartments and FC rooms have no documented `/li` ward-plot form,
  so for those leave the structured address empty and the button copies the location text instead.
- District keywords follow Lifestream's documented examples (`lavender`, `goblet`, `mist`) plus
  `shirogane` and `empyreum`.
- The plugin reads no game memory, hooks nothing and executes no teleport. It writes one config file and,
  when you ask, one text file.
