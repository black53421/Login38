# Login38 — Lineage 3.8 Launcher

*English · [繁體中文](README.zh-TW.md)*

A launcher for Lineage 3.8 private servers: it picks a server, patches the 32-bit client
in memory, injects the helper DLL, and starts the game. Written in C# on .NET 10 with a
WPF Fluent shell.

Ships as two executables:

- **`launcher.exe`** — what a player runs. Server list, window mode, patching, injection.
- **`encoder.exe`** — what a server operator runs. Produces the encrypted `list.txt` and
  `config.ini` that the launcher reads, and packs custom morph tables into `.pak`.

## Two ways to get it

**I just want to run it.** Download the zip from
[Releases](../../releases/latest), unpack it into the client directory, and read
`使用說明.txt`. You need the **x86** [.NET 10 Desktop
Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — x86, not x64; the launcher
is a 32-bit process.

**I want to build it myself.** [**BUILD.md**](BUILD.md) walks through it end to end —
prerequisites, clone, build, test, and producing the same executables the release ships.
Short version:

```powershell
git clone https://github.com/r0ptik/Login38.git
cd Login38
dotnet build Login38.slnx -c Debug
pwsh -File build/publish.ps1 -Zip
```

## What it does

### The launcher

- Reads the operator's encrypted `list.txt`, offers only the slots actually in use, and
  probes each one so a dead server reads as offline before the player presses play.
- Keeps the player's own display choices in `launcher.ini`, separate from the operator's
  `config.ini`, so handing out a new config never overwrites somebody's settings.
- Announcement page and operator links; optional server-list refresh and launcher
  self-update from URLs the operator sets.
- Runs more than one client at once, up to a ceiling the operator picks (0 = unlimited).
- Optional packet encryption: the server's RSA challenge folds down to the byte the client
  XORs everything it sends against, with movement packets exempt if the operator wants
  them readable.
- Captures the typed account and password at the login screen and sends the login packet
  in the shape server emulators expect.
- Redirects every outbound connection the client makes to the chosen server, so a stock
  client reaches a private one.

### Patches applied to the client in memory

Each one is a separate `IGamePatch` under `src/Login38.Patching/Patches/`, applied by
phase as the client unpacks and starts.

| Patch | What it does |
| --- | --- |
| `HitPointExpansionPatch` | Widens hit points and mana from 16-bit fields to 32-bit ones, everywhere the client touches them |
| `ArmourResistanceExpansionPatch` | Widens armour class and magic resistance from a byte to a full integer |
| `InventoryLimitPatch` | Corrects the item count the inventory window prints against |
| `EquipmentSlotsPatch` | Extends the equipment window from 19 slots to 25 |
| `ImageLimitPatch` | Raises the ceiling on how many `img` sprite resources the client will load |
| `PngLimitPatch` | Widens the client's fixed pool of PNG surfaces |
| `ItemDescriptionLengthPatch` | Stops long item descriptions from crashing the client |
| `ItemDescriptionColourPatch` | Makes colour codes work in item descriptions |
| `LongItemStatusPatch` | Teaches the client a packet that can carry an item description longer than 255 bytes |
| `ChatWidthPatch` | Lets chat lines use the full width of the chat box |
| `InputBoxBackgroundPatch` | Makes the chat input box capture its background from the screen, so it stops drawing black in windowed mode |
| `PresentHookPatch` | Takes over how the client puts frames on screen, so the helper's own text stays visible |
| `SurfacePixelFormatPatch` | Pins the surface colour layout instead of following the blitter's run-time choice |
| `SmoothRunPatch` | Makes hasted characters run instead of shuffling |
| `SimplifiedChineseTextPatch` | Makes the client render simplified Chinese, for operators who publish in it |
| `DynamicDialogPatch` | Lets the server send an NPC dialog instead of naming one |
| `DynamicIconPatch` | Animates chosen item icons from a custom PNG pak |
| `MorphTablePatch` | Feeds the client a morph table from memory instead of from disk |
| `ConnectRedirectPatch` | Redirects every outbound TCP connection to the chosen server |
| `LoginHookPatch` | Captures the account and password and sends the emulator-shaped login packet |
| `MovePacketEncryptionPatch` | Stops the client obfuscating and encrypting its movement packets |
| `AntiCheatBypassPatch` | Stops the client killing itself over its own memory-integrity checks |
| `TimeProtectionBypassPatch` | Disarms the client's self-protection check once its packer has finished unpacking |
| `CrtWatsonPatch` | Stops the Visual C++ 2008 runtime terminating the client over an invalid argument |
| `WindowTitlePatch` | Gives the game window a different title on every launch |

### The in-game helper

Off until the player presses **HOME** — launching a client is not asking for a helper, and
one that starts pouring potions the moment the world loads has made that choice for them.
**INSERT** toggles it after that. One helper per client, so a launcher driving two clients
has two.

- **Potions** — drinks when the player is running low, from an item list the operator ships
  in `linhelperZ.ini`.
- **Buffs** — keeps the player's buffs up.
- **Hotkeys** — runs a command on F1 to F4.
- **Timers** — runs a command every so often.
- **Shout** — says the player's messages on a loop.
- **Disposal** — destroys or drops items the player has marked.
- **Profiles** — loads a character's settings when they enter the world and saves them when
  they leave.
- **Notifications** — pickup toasts in the lower-left corner, and floating experience and
  coin text on each kill.
- **Inventory watch** — keeps the helper window's inventory current while it is open.
- **Packet spy** — writes every packet the client sends to the log.

Switches the player flips in the helper window:

| Toggle | What it does |
| --- | --- |
| All day | Keeps the world lit as if it were noon |
| Damage | Shows what each hit was worth, over the target or under it |
| Monster colour | Colours a monster's name by how far above the player it is |
| Clock | Keeps the in-game clock on screen |
| Low CPU | Lets the client idle instead of spinning a core |
| Underwater | Takes the water off the screen while the player is under it |

### The encoder

- Edits the server list — name, address, port, and the per-server switches — and writes it
  back as the encrypted `list.txt` the stock client also reads.
- Writes the `[aux]` and `[launcher]` switches to `config.ini`, and the RSA key pair to
  `pack.properties` for the server side.
- Packs a morph table from `.txt` into `.pak`, tagged so the launcher rejects a `.pak` from
  a third-party toolchain.

### Not implemented

Two switches are parsed and written back so an operator's config survives a round-trip, but
nothing reads them: `AntiCheatAdvanced` and `InternalBotEnabled`.

## x86 is a correctness requirement, not a preference

The injector resolves `LoadLibraryW` inside this process and hands that address to
`CreateRemoteThread` in the 32-bit game process. Under WOW64 the two `kernel32` base
addresses differ, so a 64-bit build would inject a bad pointer. Every patch address is
likewise a 32-bit VA. `Platforms=x86` is set solution-wide in `Directory.Build.props` and
should stay that way.

## Layout

```
Login38.slnx                 solution (.slnx, the .NET 10 format)
Directory.Build.props        shared TFM / x86 / nullable / warnings-as-errors
Directory.Packages.props     central package versions

src/
  Login38.Core/              pure logic, zero Win32, fully unit-testable
  Login38.Interop/           Win32 surface: SafeHandles, remote process/memory, injector
  Login38.Patching/          remote memory patches
  Login38.Aux/               in-game helper features
  Login38.Ui/                shared WPF controls and theming
  Login38.App/               launcher.exe — WPF Fluent shell (WPF-UI + MVVM)
  Login38.Encoder/           encoder.exe — server-operator tool
tests/                       xUnit test projects, one per source project
build/                       app manifests, publish script, end-user readme
assets/                      icons and logos, compiled into the executables
native/ddraw_inproc/         C++ DirectDraw present hook, embedded as a resource
```

## Configuration files

Nothing in this repository is a working server configuration, on purpose.

| File | Produced by | Goes to | In git? |
| --- | --- | --- | --- |
| `list.txt` | `encoder.exe` | players, beside `launcher.exe` | no |
| `config.ini` | `encoder.exe` | players, beside `launcher.exe` | no |
| `pack.properties` | `encoder.exe` | the server's `./config/` — **holds the RSA private exponent** | no |
| `launcher.ini` | `launcher.exe` | written on first run, per player | no |
| `*.pak` | `encoder.exe` | players, if the server uses custom morph tables | no |

An operator generates their own set with `encoder.exe`. `pack.properties` is a private
key: it goes to the server and nowhere else.

## Verify

```bash
.claude/check.sh
```

Builds with warnings as errors and runs the test suite. Exit 0 means shippable.
