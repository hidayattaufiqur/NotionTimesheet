param(
    [Parameter(Mandatory)][string]$Title
)

$ErrorActionPreference = 'Stop'
$TasksDataSource = 'fd88b4e2-bb79-4944-91e3-0fd88473bbff'
$Token = $env:NOTION_API_KEY
if (-not $Token) { $Token = (Get-Content (Join-Path $env:LOCALAPPDATA 'CmdPalNotion\token.txt') -Raw).Trim() }
$headers = @{
    Authorization    = "Bearer $Token"
    'Notion-Version' = '2025-09-03'
    'Content-Type'   = 'application/json'
}

$body = @{
    filter = @{ property = 'Name'; title = @{ equals = $Title } }
} | ConvertTo-Json -Depth 10 -Compress

$response = Invoke-RestMethod -Method POST -Uri "https://api.notion.com/v1/data_sources/$TasksDataSource/query" -Headers $headers -Body $body
$rows = @($response.results)
Write-Host "HTTP 200 / matches: $($rows.Count)"

foreach ($row in $rows) {
    $props = $row.properties
    [pscustomobject]@{
        PageId       = $row.id
        Url          = $row.url
        Title        = ($props.Name.title | ForEach-Object { $_.plain_text }) -join ''
        Status       = $props.Status.status.name
        Priority     = $props.Priority.select.name
        Category     = $props.Category.select.name
        StartTime    = $props.'Start Time'.date.start
        EndTime      = $props.'End Time'.date.start
        TimelineDate = $props.'Timeline Date'.date.start
        Month        = ($props.Month.relation | ForEach-Object { $_.id }) -join ','
        Project      = ($props.'FDD/Project'.relation | ForEach-Object { $_.id }) -join ','
        InTrash      = $row.in_trash
    } | Format-List
}
