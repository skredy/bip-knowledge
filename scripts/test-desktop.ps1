param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$directoryPath = (Resolve-Path $Directory).Path
$workspacePath = Join-Path ([IO.Path]::GetTempPath()) ('bip desktop essai ' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $directoryPath 'BipKb.exe') init --non-interactive --workspace $workspacePath
if ($LASTEXITCODE -ne 0) { throw 'Workspace initialization failed' }
$info = [Diagnostics.ProcessStartInfo]::new((Join-Path $directoryPath 'BipKb.Desktop.exe'))
$info.UseShellExecute = $false
$info.ArgumentList.Add('--workspace')
$info.ArgumentList.Add($workspacePath)
$app = [Diagnostics.Process]::Start($info)
try {
    $end = [DateTime]::UtcNow.AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
        if ($app.HasExited) { throw "WPF exited with code $($app.ExitCode)" }
    } until ($app.MainWindowHandle -ne 0 -or [DateTime]::UtcNow -gt $end)
    $window = [Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    function Find-Control($property, $value) {
        $condition = [Windows.Automation.PropertyCondition]::new($property, $value)
        $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    }
    $name = [Windows.Automation.AutomationElement]::NameProperty
    $id = [Windows.Automation.AutomationElement]::AutomationIdProperty
    do {
        $label = Find-Control $id 'WorkspaceLabel'
        $nav = Find-Control $id 'Nav'
        if ($label -and $label.Current.Name -eq $workspacePath -and $nav -and $nav.Current.IsEnabled) { break }
        if ([DateTime]::UtcNow -gt $end) { throw 'Workspace did not open in WPF' }
        Start-Sleep -Milliseconds 200
    } while ($true)
    foreach ($page in 'Sources','Connaissances','Dossiers','Arbitrages','Activité et outils') {
        $tab = Find-Control $name $page
        $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $heading = Find-Control $id 'Heading'
        if ($heading.Current.Name -ne $page) { throw "Navigation failed: $page" }
    }
    $storage = Find-Control $name 'Stockage'
    $storage.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $end = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 150
        $result = Find-Control $id 'Answer'
        $text = $result.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    } until ($text.Contains('materialized.json') -or [DateTime]::UtcNow -gt $end)
    if (!$text.Contains('materialized.json')) { throw 'Storage query result missing' }
    Write-Host 'WPF startup, workspace selection, navigation and storage diagnostics passed.'
} finally {
    if (!$app.HasExited) { [void]$app.CloseMainWindow(); if (!$app.WaitForExit(5000)) { $app.Kill($true) } }
    $app.Dispose()
}
