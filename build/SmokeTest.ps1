#requires -Version 7.0
<#
.SYNOPSIS
    Read-only + create/archive smoke test against the live Notion workspace, independent of
    the C# extension. Proves the token, the data source ids and the property payloads work.

.DESCRIPTION
    1. POST /v1/data_sources/<TasksKanban>/query with the In-Progress / Do Today filter and
       print the live result set.
    2. POST /v1/pages to create "<timestamp> SCRATCH TEST — safe to delete" with every field
       the Start task form writes (incl. a resolved Month relation when one exists).
    3. PATCH that page with {"archived": true} and print the response.

    Every scratch page id is printed so it can be verified as archived.

.EXAMPLE
    pwsh -File build\SmokeTest.ps1
#>
[CmdletBinding()]
param(
    [string]$Token,
    [switch]$KeepScratch
)

$ErrorActionPreference = 'Stop'

$TasksDataSource = 'fd88b4e2-bb79-4944-91e3-0fd88473bbff'
$TasksDatabase = '89bb7257-5959-4095-9816-2cbc207c8d26'
$MonthDataSource = '2334188b-6f6a-8179-8f33-000b25758536'
$NotionVersion = '2025-09-03'
$WibOffset = [TimeSpan]::FromHours(7)

if (-not $Token) {
    $Token = $env:NOTION_API_KEY
}
if (-not $Token) {
    $tokenFile = Join-Path $env:LOCALAPPDATA 'CmdPalNotion\token.txt'
    if (Test-Path $tokenFile) { $Token = (Get-Content $tokenFile -Raw).Trim() }
}
if (-not $Token) { throw 'No Notion token (NOTION_API_KEY or %LOCALAPPDATA%\CmdPalNotion\token.txt).' }

$headers = @{
    Authorization    = "Bearer $Token"
    'Notion-Version' = $NotionVersion
    'Content-Type'   = 'application/json'
}

function Invoke-Notion {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        $Body
    )

    $params = @{
        Method      = $Method
        Uri         = "https://api.notion.com/v1$Path"
        Headers     = $headers
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $params.Body = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    }

    try {
        $response = Invoke-WebRequest @params
        [pscustomobject]@{
            Status = [int]$response.StatusCode
            Json   = $response.Content | ConvertFrom-Json
            Raw    = $response.Content
        }
    }
    catch {
        $status = $null
        $raw = $null
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $raw = $reader.ReadToEnd()
        }
        [pscustomobject]@{ Status = $status; Json = $null; Raw = $raw; Error = $_.Exception.Message }
    }
}

$now = [DateTimeOffset]::Now.ToOffset($WibOffset)
$stamp = $now.ToString("yyyy-MM-dd'T'HH:mm:sszzz")
$day = $now.ToString('yyyy-MM-dd')
$monthName = $now.ToString('MMMM yyyy', [System.Globalization.CultureInfo]::InvariantCulture)

Write-Host "==> 1. Query In-Progress / Do Today (as of $stamp)" -ForegroundColor Cyan
$queryBody = @{
    filter = @{
        or = @(
            @{ property = 'Status'; status = @{ equals = 'In-Progress' } }
            @{ property = 'Status'; status = @{ equals = 'Do Today' } }
        )
    }
    sorts  = @(@{ property = 'Start Time'; direction = 'ascending' })
}
$query = Invoke-Notion -Method POST -Path "/data_sources/$TasksDataSource/query" -Body $queryBody
Write-Host "HTTP $($query.Status)"
if ($query.Raw) {
    $parsed = $query.Raw | ConvertFrom-Json
    $rows = if ($parsed.results) {
        $parsed.results | ForEach-Object {
            $title = ($_.properties.Name.title | ForEach-Object { $_.plain_text }) -join ''
            [pscustomobject]@{
                id       = $_.id
                title    = $title
                status   = $_.properties.Status.status.name
                start    = $_.properties.'Start Time'.date.start
                category = $_.properties.Category.select.name
            }
        }
    }
    Write-Host "rows: $(@($rows).Count)"
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}

if ($query.Status -ne 200) {
    Write-Host $query.Raw
    throw 'Query failed; aborting before creating anything.'
}

Write-Host "==> 2. Resolve Month row '$monthName'" -ForegroundColor Cyan
$dsInfo = Invoke-Notion -Method GET -Path "/data_sources/$MonthDataSource"
$titleProperty = 'Name'
if ($dsInfo.Json.properties) {
    $titleProperty = ($dsInfo.Json.properties.PSObject.Properties | Where-Object { $_.Value.type -eq 'title' } | Select-Object -First 1).Name
}
$monthQuery = Invoke-Notion -Method POST -Path "/data_sources/$MonthDataSource/query" -Body @{
    filter    = @{ property = $titleProperty; title = @{ equals = $monthName } }
    page_size = 5
}
$monthMatch = @($monthQuery.Json.results)
Write-Host "HTTP $($monthQuery.Status) matches=$($monthMatch.Count)"

$title = "$($now.ToString('yyyy-MM-dd HH:mm')) SCRATCH TEST — safe to delete"
Write-Host "==> 3. Create '$title'" -ForegroundColor Cyan

$properties = @{
    Name          = @{ title = @(@{ type = 'text'; text = @{ content = $title } }) }
    Description   = @{ rich_text = @(@{ type = 'text'; text = @{ content = 'Created by NotionTimesheet smoke test; archived immediately.' } }) }
    Priority      = @{ select = @{ name = 'Medium' } }
    Status        = @{ status = @{ name = 'Do Later' } }
    'Start Time'  = @{ date = @{ start = $stamp } }
    'Timeline Date' = @{ date = @{ start = $day } }
    Category      = @{ select = @{ name = 'Nine Dots' } }
}
if ($monthMatch.Count -eq 1) {
    $properties['Month'] = @{ relation = @(@{ id = $monthMatch[0].id }) }
}

$created = Invoke-Notion -Method POST -Path '/pages' -Body @{
    parent     = @{ database_id = $TasksDatabase }
    properties = $properties
}
Write-Host "HTTP $($created.Status)"
Write-Host $created.Raw

if (-not $created.Json.id) { throw 'Create failed; nothing to archive.' }

$pageId = $created.Json.id
$pageUrl = $created.Json.url

Write-Host "==> 4. Verify created values by re-reading the page" -ForegroundColor Cyan
$readBack = Invoke-Notion -Method GET -Path "/pages/$pageId"
$readBack.Json.properties | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'smoke-created-properties.json')
Write-Host "saved properties to build\smoke-created-properties.json"

if (-not $KeepScratch) {
    Write-Host "==> 5. Archive $pageId" -ForegroundColor Cyan
    $archived = Invoke-Notion -Method PATCH -Path "/pages/$pageId" -Body @{ archived = $true }
    Write-Host "HTTP $($archived.Status)"
    Write-Host ($archived.Raw.Substring(0, [Math]::Min(600, $archived.Raw.Length)))
}

[pscustomobject]@{
    QueryStatus  = $query.Status
    OpenRows     = @($query.Json.results).Count
    MonthMatches = $monthMatch.Count
    PageId       = $pageId
    PageUrl      = $pageUrl
    Archived     = -not $KeepScratch
} | Format-List

