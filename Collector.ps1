$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$issues = [System.Collections.Generic.List[object]]::new()
function Read-Section([string]$Name, [scriptblock]$Body) {
    try { & $Body } catch { $issues.Add([ordered]@{ area = $Name; message = $_.Exception.Message }) }
}
function Read-Events($Filter, [int]$Maximum) {
    try {
        @(Get-WinEvent -FilterHashtable $Filter -MaxEvents $Maximum -ErrorAction Stop | ForEach-Object {
            [ordered]@{ timeUtc = $_.TimeCreated.ToUniversalTime().ToString('o'); id = $_.Id; provider = $_.ProviderName; message = $_.Message }
        })
    } catch { if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { throw } }
}
$serviceName=$env:WD_SERVICE
$service=Read-Section 'service' { Get-CimInstance Win32_Service | Where-Object { $_.Name -eq $serviceName } | Select-Object -First 1 }
$details=$null; $dependencies=@(); $events=@()
if ($null -ne $service) {
    $command=$service.PathName; $executable=''; $spaced=$false
    if ($command -match '^\s*"([^"]+)"') { $executable=$Matches[1] }
    elseif ($command -match '^\s*(.+?\.exe)(?:\s|$)') { $executable=$Matches[1]; $spaced=$executable.Contains(' ') }
    else { $executable=$command.Trim() }
    $executable=[Environment]::ExpandEnvironmentVariables($executable)
    if ($executable.StartsWith('\SystemRoot\',[StringComparison]::OrdinalIgnoreCase)) { $executable=Join-Path $env:SystemRoot $executable.Substring(12) }
    $fileStatus='unknown'
    try { Get-Item -LiteralPath $executable -ErrorAction Stop | Out-Null; $fileStatus='present' } catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { $fileStatus='missing' } else { $issues.Add(@{area='executable';message=$_.Exception.Message}) } }
    $details=@{name=$service.Name;displayName=$service.DisplayName;state=$service.State;startMode=$service.StartMode;account=$service.StartName;pid=$service.ProcessId;exitCode=$service.ExitCode;serviceSpecificExitCode=$service.ServiceSpecificExitCode;executablePath=$executable;executableStatus=$fileStatus;unquotedSpacedPath=$spaced}
    $dependencies=@(Read-Section 'dependencies' { (Get-Service | Where-Object { $_.Name -eq $serviceName }).ServicesDependedOn | ForEach-Object { @{name=$_.Name;state=$_.Status.ToString()} } })
    $events=@(Read-Section 'events' { Read-Events @{LogName='System';ProviderName='Service Control Manager';StartTime=(Get-Date).AddDays(-[int]$env:WD_DAYS)} 200 | Where-Object { $_.message -and ($_.message.IndexOf($service.Name,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or $_.message.IndexOf($service.DisplayName,[StringComparison]::OrdinalIgnoreCase) -ge 0) } })
}
@{evidence=@{requestedService=$serviceName;service=$details;dependencies=$dependencies;events=$events};issues=@($issues.ToArray())}|ConvertTo-Json -Depth 10 -Compress
