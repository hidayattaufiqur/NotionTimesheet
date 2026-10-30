# Notion Timesheet — PowerToys Command Palette extension

Start and finish Notion **Tasks Kanban** timers without opening Notion.

| Command | What it does |
|---|---|
| **Start task** | Adaptive Card form (title, description, priority, category, FDD/Project, status). On submit it creates a Tasks Kanban row with `Start Time` = submit moment (`+07:00`), `Timeline Date` = today, `Month` resolved by name (`October 2026`), plus the picked fields. |
| **Finish task** | Live list of `In-Progress` and `Do Today` rows (with an explicit **Refresh list** item). Invoking a row PATCHes `End Time` = now, `Status` = `Completed`, and backfills `Start Time` if it was empty. |

---

## 1. Token (never in source control)

Resolved on every call, first match wins:

1. `NOTION_API_KEY` environment variable
2. `%LOCALAPPDATA%\CmdPalNotion\token.txt` — one line, starting `ntn_` or `secret_`

```powershell
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\CmdPalNotion" | Out-Null
Set-Content -NoNewline "$env:LOCALAPPDATA\CmdPalNotion\token.txt" 'ntn_your_token_here'
```

With no token, **Start task** shows a setup page with the exact path instead of a 401.

> The data folder keeps its original name, `%LOCALAPPDATA%\CmdPalNotion`, even though the
> project is now NotionTimesheet — that is where the existing `token.txt` already lives, and
> keeping the path means the working token keeps working.

---

## 2. Build

```powershell
dotnet build .\NotionTimesheet\NotionTimesheet.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

* `net9.0-windows10.0.26100.0`, `TargetPlatformMinVersion 10.0.19041.0`
* `Microsoft.CommandPalette.Extensions` **0.9.260303001** — newest package that targets .NET 9.
  **0.11/0.12 reference `System.Runtime 10.0.0.0` and need the .NET 10 SDK**, which this box
  does not have. 0.9 is the same generation that the other third-party extensions on this
  machine (xRate) run against, on the 0.13 Command Palette host.
* `CsWinRTEnabled=false`: the package already ships the `ABI.*` projections inside
  `Microsoft.CommandPalette.Extensions.Toolkit.dll`; regenerating them requires a machine-local
  Windows SDK (see §3).

## 3. Package + deploy (build ≠ deploy)

This machine has **no Windows SDK** (`C:\Program Files (x86)\Windows Kits\10` is absent), so the
MSIX build targets (`/t:Deploy`, `GenerateAppxPackageOnBuild`) cannot run: they need
`Platforms\UAP\UAP.props` and `MakePri`. `build\Pack-Msix.ps1` does the same work with
`MakeAppx`/`SignTool` taken from the `Microsoft.Windows.SDK.BuildTools` NuGet package:

```powershell
pwsh -File .\build\Pack-Msix.ps1 -Install
pwsh -File .\build\Pack-Msix.ps1 -Version 1.0.2.0 -Install   # bump for the next redeploy
```

It publishes self-contained, builds the MSIX layout from `Package.appxmanifest`, packs, signs
with the code-signing cert and registers the package for the current user.

`-Version` stamps `Identity/@Version` **in the packaged `AppxManifest.xml` only** — the
`Package.appxmanifest` in git stays at its baseline, so every install is a clean upgrade instead
of relying on `-ForceUpdateFromAnyVersion`. Without `-Version` the manifest value is used and the
script warns when it is not newer than what is installed. Installation always uses
`-ForceApplicationShutdown` because Command Palette keeps the extension process alive.

Signing identity: `CN=Nine Dots Consulting`, thumbprint
`F70216AD3AB09D39F6B3F719D6DAE92799745BA3`, created with
`New-SelfSignedCertificate -Type CodeSigningCert`. The certificate must be in
`LocalMachine\TrustedPeople` (one-time, elevated):

```powershell
certutil -addstore TrustedPeople .\notion-code-signing.cer
```

To make a new cert, recreate it and update both `Package.appxmanifest` (`Publisher=`) and the
`-CertificateThumbprint` default in `build\Pack-Msix.ps1`.

## 4. Reload after every redeploy

In Command Palette type `reload` → **Reload Command Palette extensions**.
Command Palette 0.13 also picks up a newly registered extension on its own (it launched this
extension right after `Add-AppxPackage`), but the reload command is the reliable path after a
rebuild.

---

## 5. Verify without the UI

```powershell
pwsh -File .\build\SmokeTest.ps1            # query + create + archive one scratch row
pwsh -File .\build\Verify-Row.ps1 -Title '...'   # read back one row (status/times/month)
```

UI checks are done by hand in the palette (see `TEST-SCENARIO.md`). Earlier versions of this
repo contained UI-Automation scripts that drove the palette; they were removed — Windows
Defender flagged the generated `pwsh -Command` payload as `Trojan:Win32/ClickFix`, and nothing
in this project needs to script the UI.

## 6. Layout

```
NotionTimesheet.sln
Directory.Build.props / Directory.Packages.props / nuget.config
build/
  Pack-Msix.ps1        # publish -> layout -> MakeAppx -> SignTool -> Add-AppxPackage
  SmokeTest.ps1        # live Notion query/create/archive
  Verify-Row.ps1       # read one row back by title
TEST-SCENARIO.md       # manual acceptance steps
NotionTimesheet/
  Package.appxmanifest, app.manifest
  Program.cs                        # COM server host (-RegisterProcessAsComServer)
  NotionTimesheetExtension.cs       # IExtension, GUID 7C1F5D2A-...
  NotionTimesheetCommandsProvider.cs
  NotionIds.cs                      # all data source / database ids and picker values
  Services/NotionClient.cs          # REST calls, 429 retry-after, Month resolution + day cache
  Services/TokenStore.cs            # env var then token.txt
  Services/Clock.cs                 # +07:00 timestamps
  Forms/StartTaskForm.cs            # Adaptive Card + submit
  Pages/StartTaskPage.cs, FinishTaskPage.cs, SetupPage.cs
  Commands/FinishTaskCommand.cs
```

## 7. Known divergences from the docs

* The docs' `Create a new extension` scaffold emits `net10.0-windows10.0.26100.0` and
  `Microsoft.CommandPalette.Extensions 0.11`. Neither builds here (no .NET 10 SDK, no Windows SDK).
  This project keeps the same structure but on .NET 9 + SDK 0.9.
* `msbuild /t:Deploy` — the documented deploy — fails on this box for the same missing-SDK reason;
  `build\Pack-Msix.ps1` replaces it (real signed MSIX, registered per user).
* `Microsoft.CommandPalette.Extensions.Toolkit` is not a separate NuGet package; it ships inside
  `Microsoft.CommandPalette.Extensions`.

