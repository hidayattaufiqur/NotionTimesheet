#requires -Version 7.0
<#
.SYNOPSIS
    Publishes the extension, packs a real MSIX with MakeAppx, signs it with the
    developer certificate and (optionally) registers it with the current user.

.DESCRIPTION
    The usual "Build > Deploy" flow needs a machine-local Windows SDK
    (Windows Kits\10\...\Platforms\UAP\UAP.props + PRI tooling), which is not installed
    on this box. MakeAppx/SignTool are taken from the Microsoft.Windows.SDK.BuildTools
    NuGet package instead, so the same steps run without the Windows SDK.

    The package identity is CN=Nine Dots Consulting; that certificate already lives in
    Cert:\CurrentUser\My and is trusted in CurrentUser\TrustedPeople + LocalMachine\TrustedPeople.

.EXAMPLE
    pwsh -File build\Pack-Msix.ps1 -Install
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [string]$CertificateThumbprint = 'F70216AD3AB09D39F6B3F719D6DAE92799745BA3',

    # Stamps the Identity/@Version of the packaged manifest only (Package.appxmanifest in git
    # stays at whatever baseline it has). Use a higher value than what is installed.
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$Install
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'NotionTimesheet\NotionTimesheet.csproj'
$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$artifacts = Join-Path $root "artifacts\$Configuration\$rid"
$publish = Join-Path $artifacts 'publish'
$layout = Join-Path $artifacts 'layout'
$msix = Join-Path $artifacts 'NotionTimesheet.msix'

Write-Host "==> Publishing $Configuration|$Platform ($rid)" -ForegroundColor Cyan
Remove-Item $publish, $layout -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $layout | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $rid `
    --self-contained true `
    -p:Platform=$Platform `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host '==> Building MSIX layout' -ForegroundColor Cyan
Copy-Item (Join-Path $publish '*') $layout -Recurse -Force
$manifestPath = Join-Path $layout 'AppxManifest.xml'
Copy-Item (Join-Path $root 'NotionTimesheet\Package.appxmanifest') $manifestPath -Force

$tokenVersion = [regex]::Match((Get-Content $manifestPath -Raw), '<Identity[^>]*\bVersion="([0-9.]+)"').Groups[1].Value
if ($Version) {
    # Only the Identity element carries the package version; leave every other attribute alone.
    $manifest = Get-Content $manifestPath -Raw
    $stamped = [regex]::Replace(
        $manifest,
        '(<Identity\b[^>]*\bVersion=")[0-9.]+(")',
        { param($m) $m.Groups[1].Value + $Version + $m.Groups[2].Value })
    if ($stamped -eq $manifest) { throw 'Could not stamp Version into AppxManifest.xml.' }
    Set-Content -Path $manifestPath -Value $stamped -Encoding UTF8 -NoNewline
    Write-Host "    package version stamped: $tokenVersion -> $Version" -ForegroundColor DarkGray
}
else {
    Write-Host "    package version from manifest: $tokenVersion (pass -Version x.y.z.0 to bump)" -ForegroundColor DarkGray
    $Version = $tokenVersion
}

$globalPackages = ((dotnet nuget locals global-packages --list) -join '') -replace '^global-packages:\s*', ''
$globalPackages = $globalPackages.Trim()
$sdkRoot = Join-Path $globalPackages 'microsoft.windows.sdk.buildtools'
if (-not (Test-Path $sdkRoot)) { throw "Microsoft.Windows.SDK.BuildTools is not restored ($sdkRoot missing). Run 'dotnet restore' first." }

$sdkVersionDir = Get-ChildItem $sdkRoot -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.]', '') } -ErrorAction SilentlyContinue | Select-Object -Last 1
$binRoot = Join-Path $sdkVersionDir.FullName 'bin'
$binVersionDir = Get-ChildItem $binRoot -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.]', '') } | Select-Object -Last 1

$makeappx = Join-Path $binVersionDir.FullName "$Platform\makeappx.exe"
$signtool = Join-Path $binVersionDir.FullName "$Platform\signtool.exe"
foreach ($tool in @($makeappx, $signtool)) {
    if (-not (Test-Path $tool)) { throw "Missing tool: $tool" }
}

Write-Host "==> Packing $msix" -ForegroundColor Cyan
Remove-Item $msix -Force -ErrorAction SilentlyContinue
& $makeappx pack /o /nv /d $layout /p $msix
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed.' }

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $CertificateThumbprint }
if (-not $cert) { throw "Signing certificate $CertificateThumbprint not found in Cert:\CurrentUser\My." }

Write-Host "==> Signing with $($cert.Subject)" -ForegroundColor Cyan
& $signtool sign /fd SHA256 /td SHA256 /sha1 $CertificateThumbprint $msix
if ($LASTEXITCODE -ne 0) { throw 'signtool failed.' }

Write-Host "MSIX: $msix (version $Version)" -ForegroundColor Green

if ($Install) {
    $installed = Get-AppxPackage -Name 'NotionTimesheet' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($installed -and [version]$Version -le [version]$installed.Version) {
        Write-Host "    installed version is $($installed.Version); installing $Version with -ForceUpdateFromAnyVersion (bump -Version next time)." -ForegroundColor DarkYellow
    }

    Write-Host '==> Registering package for the current user' -ForegroundColor Cyan
    Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion -ForceApplicationShutdown
    Get-AppxPackage -Name 'NotionTimesheet' | Format-List Name, Version, InstallLocation, PackageFullName
    Write-Host 'Now run "Reload Command Palette extensions" in Command Palette.' -ForegroundColor Yellow
}


