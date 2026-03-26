param(
    [string]$Configuration = "Debug"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\DEADSKY.App\DEADSKY.App.csproj"
$outputDir = Join-Path $repoRoot "artifacts\current-app"
$targetExe = Join-Path $outputDir "DEADSKY.App.exe"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$resolvedRepoRoot = (Resolve-Path $repoRoot).Path
$resolvedOutputDir = (Resolve-Path $outputDir).Path
if (-not $resolvedOutputDir.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean output outside repo root: $resolvedOutputDir"
}

Get-CimInstance Win32_Process -Filter "Name='DEADSKY.App.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.Equals($targetExe, [System.StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

try {
    Get-ChildItem -LiteralPath $resolvedOutputDir -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction Stop
}
catch {
    Get-ChildItem -LiteralPath $resolvedOutputDir -Force -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $resolvedOutputDir -Force -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Recurse -Force -File -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
}

dotnet build $projectPath -c $Configuration "-p:OutDir=$resolvedOutputDir\"
