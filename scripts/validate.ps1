param([Parameter(Mandatory)][string]$Workspace, [switch]$Live)
$ErrorActionPreference = 'Stop'
& dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw '.NET unavailable' }
& git --version
if ($LASTEXITCODE -ne 0) { throw 'Git unavailable' }
& bip-kb import --workspace $Workspace
if ($LASTEXITCODE -ne 0) { throw 'Knowledge validation failed' }
& bip-kb doctor --workspace $Workspace
if ($LASTEXITCODE -ne 0) { throw 'CLI capabilities unavailable' }
if ($Live) {
    & bip-kb doctor --live --workspace $Workspace
    if ($LASTEXITCODE -ne 0) { throw 'Real Copilot completion failed' }
}
Write-Host 'Diagnostics completed. Validate ingestion and context with a representative, approved corpus.'
