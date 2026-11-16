param([string]$ToolPath = (Join-Path $PSScriptRoot '../artifacts/tools'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    Write-Host '[1/5] Restoring locked dependencies'
    & dotnet restore --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    Write-Host '[2/5] Building application and tests'
    & dotnet build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed: see the first compiler error above. No tool was installed.' }
    Write-Host '[3/5] Running tests'
    & dotnet test --no-build --no-restore --logger 'trx;LogFileName=tests.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    $versionOutput = & dotnet msbuild src/BipKb/BipKb.csproj -nologo -getProperty:PackageVersion
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the package version from the project' }
    $version = ($versionOutput -join "`n").Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') { throw 'Unexpected package version returned by MSBuild' }
    Write-Host "[4/5] Packing BipKb $version"
    & dotnet pack src/BipKb -c Release --no-restore -o artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Pack failed' }
    if (-not (Test-Path (Join-Path $repo "artifacts/BipKb.$version.nupkg"))) { throw 'Expected package was not produced' }
    $ToolPath = [IO.Path]::GetFullPath($ToolPath)
    Write-Host "[5/5] Installing BipKb $version in $ToolPath"
    if ((Test-Path (Join-Path $ToolPath 'bip-kb.exe')) -or (Test-Path (Join-Path $ToolPath 'bip-kb'))) {
        & dotnet tool update BipKb --version $version --add-source (Join-Path $repo 'artifacts') --tool-path $ToolPath
    } else {
        & dotnet tool install BipKb --version $version --add-source (Join-Path $repo 'artifacts') --tool-path $ToolPath
    }
    if ($LASTEXITCODE -ne 0) { throw 'Tool installation failed' }
    Write-Host "Installed in $ToolPath. Add this directory to PATH."
} finally { Pop-Location }
