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
