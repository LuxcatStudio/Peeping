$ServiceName = "ActiveWindowMonitorService"
$EventLogPath = "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$ServiceName"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "Requesting administrator privileges..."
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-File", "`"$($MyInvocation.MyCommand.Path)`""
    exit
}

Write-Host "Stopping and uninstalling service..."


$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    Write-Host "Service '$ServiceName' does not exist."
} else {
    Write-Host "Service '$ServiceName' found. Status: $($service.Status)"

    if ($service.Status -eq "Running") {
        Write-Host "Stopping service..."
        try {
            Stop-Service -Name $ServiceName -Force -ErrorAction Stop
            Write-Host "Service stop command sent."
            $timeout = 10
            $counter = 0
            while ($service.Status -ne "Stopped" -and $counter -lt $timeout) {
                Start-Sleep -Seconds 1
                $service.Refresh()
                $counter++
                Write-Host "Waiting for service to stop... ($counter/$timeout seconds)"
            }
            
            if ($service.Status -eq "Stopped") {
                Write-Host "Service stopped successfully."
            } else {
                Write-Host "Service stop timeout, trying to kill process..."
            }
        } catch {
            Write-Host "Failed to stop service: $($_.Exception.Message)"
        }
    }
}

Write-Host "Attempting to force kill service process..."
try {
    $process = Get-WmiObject Win32_Service -Filter "Name='$ServiceName'" | Select-Object ProcessId
    if ($process -and $process.ProcessId -gt 0) {
        Write-Host "Found service process PID: $($process.ProcessId)"
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "Service process killed."
        Start-Sleep -Seconds 2
    } else {
        Write-Host "No service process found."
    }
} catch {
    Write-Host "Failed to kill service process: $($_.Exception.Message)"
}

Write-Host "Deleting service..."
sc.exe delete $ServiceName 2>&1 | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service deleted successfully."
} else {
    Write-Host "Service deletion completed (service may not exist)."
}

Write-Host "Deleting event log source..."
if (Test-Path $EventLogPath) {
    try {
        Remove-Item $EventLogPath -Recurse -Force -ErrorAction Stop
        Write-Host "Event log source deleted successfully."
    } catch {
        Write-Host "Failed to delete event log source: $($_.Exception.Message)"
    }
} else {
    Write-Host "Event log source does not exist."
}

Write-Host "Uninstall completed!"