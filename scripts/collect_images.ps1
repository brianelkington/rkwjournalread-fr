<#
.SYNOPSIS
    Collects all .jpg files in a folder (non-recursive) and writes their Windows paths to a JSON file.

.PARAMETER Folder
    Directory to scan (default: current directory)

.PARAMETER OutputFile
    File to write JSON to (default: images.json)
#>

param(
    [string]$Folder = ".",
    [string]$OutputFile = "images.json"
)

$jpgFiles = Get-ChildItem -Path $Folder -Filter *.jpg -File

if (-not $jpgFiles) {
    Write-Warning "No .jpg files found in '$Folder'."
}

$jsonArray = @()

foreach ($file in $jpgFiles) {
    if (Test-Path $file.FullName) {
        $jsonArray += @{
            path  = $file.FullName
            split = $true
        }
    } else {
        Write-Warning "File not found: $($file.FullName)"
    }
}

$json = $jsonArray | ConvertTo-Json -Depth 3
Set-Content -Path $OutputFile -Value $json

$entryCount = $jsonArray.Count
Write-Host "Generated JSON with $entryCount entries."