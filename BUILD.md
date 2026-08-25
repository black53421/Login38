# Building Login38 from source

Everything below runs on Windows. The launcher injects into a 32-bit game process
through Win32 APIs, so there is no cross-platform build.

## 1. Prerequisites

| What | Why | Where |
| --- | --- | --- |
| **.NET 10 SDK** | compiles the solution | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **.NET 10 Desktop Runtime — x86** | runs the launcher and the test host | same page, pick **x86 (32-bit)** |
| **Git** | clone | <https://git-scm.com/download/win> |

The SDK's own bitness does not matter — an x64 SDK compiles an x86 target fine. The
**runtime** does: every executable and every test project in this solution is
`PlatformTarget=x86`, and a 32-bit process cannot load a 64-bit runtime. Installing
only the x64 desktop runtime produces `You must install .NET Desktop Runtime` when you
run `launcher.exe`, and a test host that fails to start when you run `dotnet test`.

Verify:

```powershell
dotnet --version                # 10.0.x
dotnet --list-runtimes | Select-String 'WindowsDesktop'
```

You want a `Microsoft.WindowsDesktop.App 10.0.x` line whose path starts with
`C:\Program Files (x86)\dotnet` — that is the x86 one.

Visual Studio is optional. If you want it, 2022 17.12+ or 2026 with the **.NET desktop
development** workload opens `Login38.slnx` directly.

## 2. Clone and build

```powershell
git clone https://github.com/<owner>/Login38.git
cd Login38
dotnet build Login38.slnx -c Debug
```

First build restores ~10 NuGet packages and takes a couple of minutes. After that it is
seconds.

`TreatWarningsAsErrors` is on for the whole solution, so a build that prints nothing is
a build that is clean. That is deliberate: a warning here is usually a marshalling or
nullability mistake that would fail at a P/Invoke boundary rather than at compile time.

## 3. Run the tests

```powershell
dotnet test Login38.slnx -c Debug
```

Or run the project sensor, which does both and exits non-zero on any failure:

```bash
.claude/check.sh          # git-bash / WSL
```

## 4. Produce the shipping executables

```powershell
pwsh -File build/publish.ps1 -Zip
```

Windows PowerShell 5.1 works too:

```powershell
powershell -ExecutionPolicy Bypass -File build/publish.ps1 -Zip
```

Output lands in `artifacts/release/`:

```
launcher.exe    ~9 MB    the player's launcher
encoder.exe     ~8 MB    the operator's tool
使用說明.txt              end-user instructions, copied into the zip
```

and `artifacts/Login38-v<version>-win-x86.zip` is exactly what a GitHub release ships.

To do it by hand instead:

```powershell
dotnet publish src/Login38.App/Login38.App.csproj -c Release -o artifacts/release
dotnet publish src/Login38.Encoder/Login38.Encoder.csproj -c Release -o artifacts/release
```

The publish settings live in the two `.csproj` files, not on the command line, so both
forms give the same binary.

### Why the executables are framework-dependent

`SelfContained=false` + `PublishSingleFile=true`. One file, but the machine supplies the
runtime, so an operator hands out a 9 MB executable instead of a 61 MB one and a launcher
update costs 9 MB again rather than shipping the whole runtime a second time. The price is
that a player installs the x86 desktop runtime once.

Trimming is off, and not by preference — the SDK refuses. WPF resolves types by name out
of XAML, the trimmer cannot see those references, and `NETSDK1168` stops the build rather
than shipping something that crashes when a window opens. Setting `PublishTrimmed=true`
will not work; do not try.

Switching to `SelfContained=true` does work if you would rather ship one 61 MB file that
needs no runtime install. Change it in both `.csproj` files, not just one.

## 5. The native DirectDraw hook (optional)

`native/ddraw_inproc/` is a small C++ DLL that takes over the game's DirectDraw present
call so the helper features can draw. The compiled `build/l38ddraw.dll` is **committed to
this repository**, and both `Login38.Aux` and `Login38.Patching` embed it as a resource.

That means a normal build needs no C++ toolchain at all. You only need one if you change
`native/ddraw_inproc/src/*.cpp`:

```cmd
native\ddraw_inproc\build.bat
```

It calls `vcvars32.bat` from a Visual Studio install — edit the `VCVARS` path at the top
of the script if yours is elsewhere. It must be **vcvars32**, not vcvars64: the DLL is
loaded into the 32-bit game process.

## 6. Troubleshooting

**`NETSDK1083: The specified RuntimeIdentifier 'win-x86' is not recognized`**
The .NET 10 SDK is not installed, or an old `global.json` somewhere above the clone is
pinning an earlier SDK. Check with `dotnet --version`.

**`error MSB4126: The specified solution configuration "Debug|Any CPU" is invalid`**
You passed `-p:Platform=AnyCPU`, or opened the solution in a tool that defaults to it.
This solution only defines `x86`. Drop the override.

**Build succeeds, `launcher.exe` does nothing when double-clicked**
Missing x86 desktop runtime — see §1. The single-file executable has a runtime check but
no console, so the failure is silent. Run it from a terminal to see the message.

**`dotnet test` reports the test host crashed**
Same cause: the test projects are x86 and need the x86 runtime.

**Chinese text in the UI or in generated files comes out as `???`**
Something has forced invariant globalization. `InvariantGlobalization=false` in
`Directory.Build.props` is required — every string that crosses into game memory is CP950.

## 7. Repository layout

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
