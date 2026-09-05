[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$documents = @(& git -C $root ls-files --cached --others --exclude-standard -- '*.md' | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate Markdown.' }
$failures = [Collections.Generic.List[string]]::new()
foreach ($document in $documents) {
    if ($document.StartsWith('thirdparty/')) { continue }
    $path = Join-Path $root $document
    $content = Get-Content -LiteralPath $path -Raw
    $content = [regex]::Replace($content, '(?ms)^```.*?^```\s*$', '')
    $links = [regex]::Matches($content, '\[[^\]\r\n]*\]\(([^)\r\n]+)\)|(?:src|href)="([^"]+)"')
    foreach ($link in $links) {
        $target = ($link.Groups[1].Value + $link.Groups[2].Value).Trim().Trim('<', '>')
        if ($target -match '^(https?:|data:|mailto:|#)' -or $target -match '\s+"') { continue }
        $target = [uri]::UnescapeDataString(($target -split '#', 2)[0])
        if (-not $target) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $target))
        if (-not (Test-Path -LiteralPath $resolved)) { $failures.Add("${document}: broken local link '$target'") }
    }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Host "Verified local Markdown and HTML links in $($documents.Count) documents."

& (Join-Path $root "thirdparty/FishUI/scripts/Test-Documentation.ps1")

$fishUiCommit = (& git -C (Join-Path $root 'thirdparty/FishUI') rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the FishUI dependency revision.' }
foreach ($document in @('README.md', 'INFO.md')) {
    if (-not (Get-Content -LiteralPath (Join-Path $root $document) -Raw).Contains($fishUiCommit)) {
        throw "${document}: FishUI dependency revision does not match the checkout."
    }
}
