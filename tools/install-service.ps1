# Quick Memory Server installer/upgrader for Windows
# - Builds + publishes the worker and MemoryCtl
# - Prompts for machine/paths/port, installs or updates the Windows service
# Run from the repo root with: powershell -ExecutionPolicy Bypass -File tools/install-service.ps1

[CmdletBinding()]
param(
    [string]$MachineName,
    [string]$InstallDirectory = "C:\\Program Files\\q-memory-mcp",
    [string]$DataDirectory,
    [int]$Port,
    [switch]$Quiet
)

function Write-Note {
    param([string]$Message)
    Write-Host "[qms] $Message"
}

function Resolve-DotNetPath {
    if ($env:NEXPORT_WINDOTNET) {
        $candidate = Join-Path $env:NEXPORT_WINDOTNET 'dotnet.exe'
        if (Test-Path $candidate) { return $candidate }
    }

    $defaultPath = "C:\\Program Files\\dotnet\\dotnet.exe"
    if (Test-Path $defaultPath) { return $defaultPath }

    throw "dotnet.exe not found. Set NEXPORT_WINDOTNET or install the Windows .NET SDK."
}

function Prompt-IfMissing {
    param(
        [string]$Value,
        [string]$Prompt,
        [string]$Default
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) { return $Value }
    if ($Quiet) { return $Default }

    $response = Read-Host "${Prompt} [${Default}]"
    if ([string]::IsNullOrWhiteSpace($response)) { return $Default }
    return $response
}

$dotnet = Resolve-DotNetPath()
Write-Note "Using dotnet at $dotnet"

$targetName = Prompt-IfMissing $MachineName "Install to machine (enter 'local' for this computer)" "local"
$targetIsRemote = $targetName -ne "local" -and $targetName -ne '.' -and $targetName -ne $env:COMPUTERNAME

$InstallDirectory = Prompt-IfMissing $InstallDirectory "Install directory" "C:\\Program Files\\q-memory-mcp"
$DataDirectory = Prompt-IfMissing $DataDirectory "Data directory" "C:\\ProgramData\\q-memory-mcp"
$Port = [int](Prompt-IfMissing ($Port -as [string]) "HTTP port" "5080")

$escapedData = $DataDirectory -replace '\\', '\\\\'
$httpUrl = "http://0.0.0.0:$Port"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$publishRoot = Join-Path $env:TEMP "qms-publish-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

Write-Note "Publishing artifacts to $publishRoot"
& $dotnet publish "$repoRoot/src/QuickMemoryServer.Worker/QuickMemoryServer.Worker.csproj" `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:AssemblyName=QuickMemoryServer `
    -o $publishRoot | Write-Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$toolsOutput = Join-Path $publishRoot 'tools'
& $dotnet publish "$repoRoot/tools/MemoryCtl/MemoryCtl.csproj" `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:AssemblyName=memoryctl `
    -o $toolsOutput | Write-Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (MemoryCtl) failed" }

Copy-Item "$repoRoot/layout.json" -Destination (Join-Path $publishRoot 'layout.json') -Force

$configTemplate = if (Test-Path "$repoRoot/QuickMemoryServer.toml") { "$repoRoot/QuickMemoryServer.toml" } else { "$repoRoot/QuickMemoryServer.sample.toml" }
$configTarget = Join-Path $publishRoot 'QuickMemoryServer.toml'
Copy-Item $configTemplate -Destination $configTarget -Force

$toml = Get-Content $configTarget -Raw
$toml = $toml -replace 'serviceName = "[^"]*"', 'serviceName = "QuickMemoryServer"'
$toml = $toml -replace 'httpUrl = "[^"]*"', "httpUrl = \"$httpUrl\""
$toml = $toml -replace 'storagePath = "MemoryStores/shared"', "storagePath = \"$escapedData\\\\shared\""
$toml = $toml -replace 'storagePath = "MemoryStores/projectA"', "storagePath = \"$escapedData\\\\projectA\""
Set-Content -LiteralPath $configTarget -Value $toml -Encoding UTF8

New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot 'logs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot 'Backups') | Out-Null

function Ensure-TargetStructure {
    param(
        [string]$BaseDir,
        [string]$DataDir,
        [bool]$IsRemote,
        [System.Management.Automation.Runspaces.PSSession]$Session
    )

    $shared = Join-Path $DataDir 'shared'
    $project = Join-Path $DataDir 'projectA'

    if ($IsRemote) {
        Invoke-Command -Session $Session -ScriptBlock {
            param($base, $data, $sharedPath, $projectPath)
            New-Item -ItemType Directory -Force -Path $base, $data, $sharedPath, $projectPath, (Join-Path $base 'logs'), (Join-Path $base 'Backups') | Out-Null
        } -ArgumentList $BaseDir, $DataDir, $shared, $project
    }
    else {
        New-Item -ItemType Directory -Force -Path $BaseDir, $DataDir, $shared, $project, (Join-Path $BaseDir 'logs'), (Join-Path $BaseDir 'Backups') | Out-Null
    }
}

function Install-Or-UpdateServiceLocal {
    param(
        [string]$ServiceName,
        [string]$BinPath,
        [string]$DisplayName
    )

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        $resp = if ($Quiet) { 'y' } else { Read-Host "Service '$ServiceName' exists. Update it? [y/N]" }
        if ($resp -notin @('y','Y','yes','YES')) { Write-Note "Skipped update"; return }
        if ($existing.Status -ne 'Stopped') { Write-Note "Stopping $ServiceName"; Stop-Service $ServiceName -Force }
        sc.exe config $ServiceName binPath= "\"$BinPath\"" DisplayName= "$DisplayName" start= auto | Out-Null
    }
    else {
        sc.exe create $ServiceName binPath= "\"$BinPath\"" DisplayName= "$DisplayName" start= auto | Out-Null
    }

    Start-Service $ServiceName
    Start-Sleep -Seconds 2
    $svc = Get-Service $ServiceName -ErrorAction Stop
    if ($svc.Status -ne 'Running') { throw "Service $ServiceName failed to start. Current status: $($svc.Status)" }
}

function Install-Or-UpdateServiceRemote {
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [string]$Target,
        [string]$ServiceName,
        [string]$BinPath,
        [string]$DisplayName
    )

    $updateNeeded = Invoke-Command -Session $Session -ScriptBlock {
        param($svc) return [bool](Get-Service -Name $svc -ErrorAction SilentlyContinue)
    } -ArgumentList $ServiceName

    if ($updateNeeded) {
        $resp = if ($Quiet) { 'y' } else { Read-Host "Service '$ServiceName' exists on $Target. Update it? [y/N]" }
        if ($resp -notin @('y','Y','yes','YES')) { Write-Note "Skipped update"; return }
        Invoke-Command -Session $Session -ScriptBlock {
            param($svc,$bin,$display)
            $s = Get-Service -Name $svc -ErrorAction Stop
            if ($s.Status -ne 'Stopped') { Stop-Service $svc -Force }
            sc.exe config $svc binPath= "\"$bin\"" DisplayName= "$display" start= auto | Out-Null
        } -ArgumentList $ServiceName, $BinPath, $DisplayName
    }
    else {
        Invoke-Command -Session $Session -ScriptBlock {
            param($svc,$bin,$display)
            sc.exe create $svc binPath= "\"$bin\"" DisplayName= "$display" start= auto | Out-Null
        } -ArgumentList $ServiceName, $BinPath, $DisplayName
    }

    Invoke-Command -Session $Session -ScriptBlock {
        param($svc)
        Start-Service $svc
        Start-Sleep -Seconds 2
        $s = Get-Service $svc -ErrorAction Stop
        if ($s.Status -ne 'Running') { throw "Service $svc failed to start. Current status: $($s.Status)" }
    } -ArgumentList $ServiceName
}

$serviceName = 'QuickMemoryServer'
$displayName = 'Quick Memory MCP'
$serviceExe = Join-Path $InstallDirectory 'QuickMemoryServer.exe'

if ($targetIsRemote) {
    Write-Note "Opening remote session to $targetName"
    $session = New-PSSession -ComputerName $targetName
    Ensure-TargetStructure -BaseDir $InstallDirectory -DataDir $DataDirectory -IsRemote $true -Session $session
    Write-Note "Copying payload to $targetName:$InstallDirectory"
    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $InstallDirectory -Recurse -Force -ToSession $session
    Install-Or-UpdateServiceRemote -Session $session -Target $targetName -ServiceName $serviceName -BinPath $serviceExe -DisplayName $displayName
    Remove-PSSession $session
}
else {
    Ensure-TargetStructure -BaseDir $InstallDirectory -DataDir $DataDirectory -IsRemote $false
    Write-Note "Copying payload to $InstallDirectory"
    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $InstallDirectory -Recurse -Force
    Install-Or-UpdateServiceLocal -ServiceName $serviceName -BinPath $serviceExe -DisplayName $displayName
}

Write-Note "Install/upgrade complete. Service '$serviceName' is running on port $Port using data at $DataDirectory"

Remove-Item -Recurse -Force $publishRoot
