param([string]$ToolPath = (Join-Path $PSScriptRoot '../artifacts/tools'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & dotnet restore --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    & dotnet test --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & dotnet pack src/BipKb -c Release --no-restore -o artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Pack failed' }
    $ToolPath = [IO.Path]::GetFullPath($ToolPath)
    if ((Test-Path (Join-Path $ToolPath 'bip-kb.exe')) -or (Test-Path (Join-Path $ToolPath 'bip-kb'))) {
        & dotnet tool update BipKb --version 0.3.4 --add-source (Join-Path $repo 'artifacts') --tool-path $ToolPath
    } else {
        & dotnet tool install BipKb --version 0.3.4 --add-source (Join-Path $repo 'artifacts') --tool-path $ToolPath
    }
    if ($LASTEXITCODE -ne 0) { throw 'Tool installation failed' }
    Write-Host "Installed in $ToolPath. Add this directory to PATH."
} finally { Pop-Location }
