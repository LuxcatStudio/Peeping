$ServiceName = "ActiveWindowMonitorService"
$DisplayName = "Active Window Monitor Service"
$ServicePath = "C:\MonitorService\ActiveWindowMonitor.exe"
$ServiceArgs = "http://localhost:3000/api/status"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "Requesting administrator privileges..."
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-File", "`"$($MyInvocation.MyCommand.Path)`""
    exit
}

Write-Host "Installing and starting service..."

if (-not (Test-Path $ServicePath)) {
    Write-Host "ERROR: Executable file not found - $ServicePath"
    Write-Host "Please ensure the program is compiled and placed in the correct location."
    exit 1
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -ne $service) {
    Write-Host "Service already exists. Status: $($service.Status)"
} else {
    Write-Host "Creating new service..."
    
    $command = "`"$ServicePath`" `"$ServiceArgs`""
    Write-Host "Service command: $command"
    
    sc.exe create $ServiceName binPath= "$command" start= auto DisplayName= "$DisplayName"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Service created successfully"
        
        try {
            New-Item "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$ServiceName" -Force | Out-Null
            Write-Host "Event log source registered successfully"
        } catch {
            Write-Host "Failed to register event log source: $($_.Exception.Message)"
        }
    } else {
        Write-Host "Service creation failed, error code: $LASTEXITCODE"
        exit 1
    }
}

Write-Host "Starting service..."
try {
    Start-Service -Name $ServiceName -ErrorAction Stop
    Start-Sleep -Seconds 2
    
    $service = Get-Service -Name $ServiceName
    if ($service.Status -eq "Running") {
        Write-Host "Service started successfully!"
        Write-Host "Service Name: $DisplayName"
        Write-Host "Service Status: $($service.Status)"
    } else {
        Write-Host "Service started but status is: $($service.Status)"
    }
} catch {
    Write-Host "Failed to start service: $($_.Exception.Message)"
    Write-Host "Please check Event Viewer for detailed error information."
}

Write-Host "Installation completed!"