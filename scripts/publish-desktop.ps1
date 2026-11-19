param([string]$Output = (Join-Path $PSScriptRoot '../artifacts/desktop-win-x64'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$Output = [IO.Path]::GetFullPath($Output)
Push-Location $repo
try {
    $project = 'src/BipKb.Desktop/BipKb.Desktop.csproj'
    $lockOption = '-p:NuGetLockFilePath=packages.desktop-win.lock.json'
    & dotnet restore $project -r win-x64 --locked-mode $lockOption
    if ($LASTEXITCODE -ne 0) { throw 'Desktop restore failed' }
    & dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore $lockOption -o $Output
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
    foreach ($name in 'BipKb.Desktop.exe','BipKb.exe','BipKb.runtimeconfig.json','coreclr.dll','PresentationFramework.dll','e_sqlite3.dll') {
        if (!(Test-Path (Join-Path $Output $name))) { throw "Missing desktop distribution file: $name" }
    }
    Write-Host "Windows x64 distribution: $Output"
} finally { Pop-Location }
