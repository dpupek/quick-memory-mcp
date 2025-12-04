# Quick Memory Server installer/upgrader for Windows
# - Publishes worker + MemoryCtl, rewrites config, installs/updates service
# - Prompts for machine/paths/port/account/API keys if not provided
# - Can dry-run validate, backup/rollback, open firewall, add URL ACL, and uninstall
# Run from repo root: powershell -ExecutionPolicy Bypass -File tools/install-service.ps1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$MachineName,
    [string]$InstallDirectory = "C:\\Program Files\\q-memory-mcp",
    [string]$DataDirectory,
    [int]$Port,
    [string]$ServiceAccount,
    [securestring]$ServiceAccountPassword,
    [switch]$GrantLogonRight,
    [switch]$SkipFirewall,
    [switch]$SkipStart,
    [switch]$NoRollback,
    [switch]$ValidateOnly,
    [switch]$Uninstall,
    [switch]$Quiet
)

$transcriptPath = Join-Path $env:TEMP "qms-install-$([DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss')).log"
Start-Transcript -Path $transcriptPath -Append | Out-Null

function Write-Note {
    param([string]$Message)
    Write-Host "[qms] $Message"
}

function Resolve-DotNetPath {
    if ($env:NEXPORT_WINDOTNET) {
        $candidate = Join-Path $env:NEXPORT_WINDOTNET 'dotnet.exe'
        if (Test-Path $candidate) { return $candidate }
    }

    $fromPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }

    $defaultPath = "C:\\Program Files\\dotnet\\dotnet.exe"
    if (Test-Path $defaultPath) { return $defaultPath }

    throw "dotnet.exe not found. Install the Windows .NET SDK or set NEXPORT_WINDOTNET to its folder."
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

function Ensure-ElevationIfNeeded {
    param(
        [string]$Path,
        [bool]$IsRemote
    )

    if ($IsRemote) { return }
    $needsAdmin = $Path -like 'C:\\Program Files*'
    if (-not $needsAdmin) { return }
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Installing to '$Path' requires elevation. Please rerun PowerShell as Administrator or choose a writable directory."
    }
}

function Prompt-CredentialIfNeeded {
    param(
        [string]$Account,
        [securestring]$Password
    )

    if (-not $Account -or $Account -eq 'LocalSystem') { return 'LocalSystem', $null }
    if ($Account -and $Password) { return ,$Account,$Password }
    if ($Quiet) { throw "Custom service account provided without password in quiet mode." }
    $pw = Read-Host "Password for $Account" -AsSecureString
    return ,$Account,$pw
}

function Grant-ServiceLogonRight {
    param([string]$Account)

    try {
        $sid = (New-Object System.Security.Principal.NTAccount($Account)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    } catch {
        throw "Unable to resolve SID for $Account"
    }

    $inf = Join-Path $env:TEMP 'qms-secpol.inf'
    $db = Join-Path $env:TEMP 'qms-secpol.sdb'
    secedit /export /cfg $inf /areas USER_RIGHTS | Out-Null
    $content = Get-Content $inf
    $lineIndex = -1
    for ($i = 0; $i -lt $content.Length; $i++) { if ($content[$i] -match '^SeServiceLogonRight') { $lineIndex = $i; break } }
    if ($lineIndex -ge 0) {
        if ($content[$lineIndex] -match [regex]::Escape($sid)) { return }
        $content[$lineIndex] = $content[$lineIndex] + ",$sid"
    } else {
        $content += "SeServiceLogonRight = $sid"
    }
    Set-Content -Path $inf -Value $content -Encoding ASCII
    secedit /import /cfg $inf /db $db /quiet | Out-Null
    secedit /configure /db $db /cfg $inf /areas USER_RIGHTS /quiet | Out-Null
}

function New-RandomApiKey {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Update-TomlConfig {
    param(
        [string]$ConfigPath,
        [string]$HttpUrl,
        [string]$DataDir,
        [string]$AdminKey,
        [string]$ReaderKey
    )

    # TOML needs backslashes escaped once; build a clean Windows path with double backslashes
    $sanitizedData = $DataDir.TrimEnd([char]'\', [char]'/')
    $escapedData = $sanitizedData -replace '\\', '\\\\'
    $toml = Get-Content $ConfigPath -Raw
    $toml = $toml -replace 'serviceName = "[^"]*"', 'serviceName = "QuickMemoryServer"'
    $toml = $toml -replace 'httpUrl = "[^"]*"', ('httpUrl = "{0}"' -f $HttpUrl)
    $toml = $toml -replace 'storagePath = "MemoryStores/shared"', ('storagePath = "{0}\\shared"' -f $escapedData)
    $toml = $toml -replace 'storagePath = "MemoryStores/projectA"', ('storagePath = "{0}\\projectA"' -f $escapedData)

    if ($AdminKey) {
        $toml = $toml -replace '(?m)(^\[users\.alice\]\s*\r?\n)apiKey = "[^"]*"', ([string]::Format('$1apiKey = "{0}"', $AdminKey))
    }
    if ($ReaderKey) {
        $toml = $toml -replace '(?m)(^\[users\.bob\]\s*\r?\n)apiKey = "[^"]*"', ([string]::Format('$1apiKey = "{0}"', $ReaderKey))
    }

    Set-Content -LiteralPath $ConfigPath -Value $toml -Encoding UTF8
}

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

function Should-OverwriteConfig {
    param(
        [bool]$IsRemote,
        [string]$InstallDir,
        [System.Management.Automation.Runspaces.PSSession]$Session
    )

    if ($IsRemote) {
        $exists = Invoke-Command -Session $Session -ScriptBlock { param($p) Test-Path (Join-Path $p 'QuickMemoryServer.toml') } -ArgumentList $InstallDir
    }
    else {
        $exists = Test-Path (Join-Path $InstallDir 'QuickMemoryServer.toml')
    }

    if (-not $exists) { return $true }

    if ($Quiet) { return $false }
    $resp = Read-Host "QuickMemoryServer.toml already exists in $InstallDir. Overwrite it with installer defaults? [y/N]"
    return $resp -in @('y','Y','yes','YES')
}

function Backup-ExistingInstall {
    param(
        [string]$InstallDir
    )
    if (-not (Test-Path $InstallDir)) { return $null }
    $zip = Join-Path $env:TEMP "qms-backup-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss')).zip"
    Compress-Archive -Path (Join-Path $InstallDir '*') -DestinationPath $zip -Force
    return $zip
}

function Restore-Backup {
    param(
        [string]$Zip,
        [string]$InstallDir
    )
    if (-not $Zip) { return }
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $InstallDir
    Expand-Archive -Path $Zip -DestinationPath $InstallDir -Force
}

function Install-Or-UpdateServiceLocal {
    param(
        [string]$ServiceName,
        [string]$BinPath,
        [string]$DisplayName,
        [string]$Account,
        [securestring]$Password,
        [switch]$SkipStart
    )

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    $args = @("binPath=`"$BinPath`"", "DisplayName=$DisplayName", "start=auto")
    if ($Account -and $Account -ne 'LocalSystem') {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
        $args += @("obj=$Account", "password=$plain")
    }

    if ($existing) {
        $resp = if ($Quiet) { 'y' } else { Read-Host "Service '$ServiceName' exists. Update it? [y/N]" }
        if ($resp -notin @('y','Y','yes','YES')) { Write-Note "Skipped update"; return }
        if ($existing.Status -ne 'Stopped') { Write-Note "Stopping $ServiceName"; Stop-Service $ServiceName -Force }
        & sc.exe config $ServiceName @args | Out-Null
    }
    else {
        & sc.exe create $ServiceName @args | Out-Null
    }

    if (-not $SkipStart) {
        try {
            Start-Service $ServiceName
            Start-Sleep -Seconds 3
            $svc = Get-Service $ServiceName -ErrorAction Stop
            if ($svc.Status -ne 'Running') { throw "Service $ServiceName failed to start. Current status: $($svc.Status)" }
        }
        catch {
            Dump-LocalServiceLogs -InstallDir (Split-Path $BinPath -Parent) -ServiceName $ServiceName -Tail 80
            throw
        }
    }
}

function Install-Or-UpdateServiceRemote {
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [string]$Target,
        [string]$ServiceName,
        [string]$BinPath,
        [string]$DisplayName,
        [string]$Account,
        [securestring]$Password,
        [switch]$SkipStart
    )

    $updateNeeded = Invoke-Command -Session $Session -ScriptBlock {
        param($svc) return [bool](Get-Service -Name $svc -ErrorAction SilentlyContinue)
    } -ArgumentList $ServiceName

    $pwdPlain = if ($Password) { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)) } else { $null }
    $script = {
        param($svc,$bin,$display,$acct,$pwd,$quiet,$skipStart)
        $args = @("binPath=`"$bin`"", "DisplayName=$display", "start=auto")
        if ($acct -and $acct -ne 'LocalSystem') { $args += @("obj=$acct", "password=$pwd") }
        $present = Get-Service -Name $svc -ErrorAction SilentlyContinue
        if ($present) {
            if (-not $quiet) { Write-Host "Updating $svc on remote" }
            if ($present.Status -ne 'Stopped') { Stop-Service $svc -Force }
            & sc.exe config $svc @args | Out-Null
        }
        else {
            & sc.exe create $svc @args | Out-Null
        }
        if (-not $skipStart) {
            Start-Service $svc
            Start-Sleep -Seconds 3
            $s = Get-Service $svc -ErrorAction Stop
            if ($s.Status -ne 'Running') { throw "Service $svc failed to start. Current status: $($s.Status)" }
        }
    }

    Invoke-Command -Session $Session -ScriptBlock $script -ArgumentList $ServiceName, $BinPath, $DisplayName, $Account, $pwdPlain, $Quiet, $SkipStart
}

function Set-FirewallAndUrlAcl {
    param(
        [int]$Port,
        [string]$Account,
        [bool]$IsRemote,
        [System.Management.Automation.Runspaces.PSSession]$Session
    )

    $url = "http://+:$Port/"
    $fwName = "QuickMemoryServer-$Port"
    $user = if ([string]::IsNullOrWhiteSpace($Account) -or $Account -eq 'LocalSystem') { 'NT AUTHORITY\\SYSTEM' } else { $Account }

    $script = {
        param($p,$url,$fw,$acct)
        try { New-NetFirewallRule -DisplayName $fw -Direction Inbound -Action Allow -Protocol TCP -LocalPort $p -Profile Any -ErrorAction SilentlyContinue | Out-Null } catch {}
        try { netsh http delete urlacl url=$url | Out-Null } catch {}
        netsh http add urlacl url=$url user=$acct | Out-Null
    }

    if ($IsRemote) {
        Invoke-Command -Session $Session -ScriptBlock $script -ArgumentList $Port, $url, $fwName, $user
    }
    else {
        & $script $Port $url $fwName $user
    }
}

function Test-Health {
    param(
        [string]$TargetHost,
        [int]$Port
    )
    $url = "http://${TargetHost}:${Port}/health"
    $timeout = 20
    for ($i=0; $i -lt 5; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec $timeout
            if ($resp.StatusCode -eq 200) { return $true }
        } catch {}
        Start-Sleep -Seconds 3
    }
    return $false
}

function Validate-Layout {
    param(
        [string]$PublishRoot,
        [string]$LayoutPath
    )
    if (-not (Test-Path $LayoutPath)) { return }
    $layout = Get-Content $LayoutPath | ConvertFrom-Json
    $missing = @()
    foreach ($f in $layout.files) {
        $path = Join-Path $PublishRoot $f.path
        if (-not (Test-Path $path)) { $missing += $f.path }
    }
    if ($missing.Count -gt 0) {
        Write-Note "Warning: missing expected files from layout.json: $($missing -join ', ')"
    }
}

function Dump-LocalServiceLogs {
    param(
        [string]$InstallDir,
        [string]$ServiceName,
        [int]$Tail
    )

    if (Test-Path (Join-Path $InstallDir 'logs')) {
        $latest = Get-ChildItem -Path (Join-Path $InstallDir 'logs') -Filter 'quick-memory-server-*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latest) {
            Write-Note "Tail of $($latest.FullName):"
            Get-Content $latest.FullName -Tail $Tail
        }
    }

    Write-Note "Recent Application event log entries for ${ServiceName}:"
    try {
        $events = Get-EventLog -LogName Application -Source $ServiceName -Newest 5 -ErrorAction SilentlyContinue
        if ($events) {
            $events | Format-List -Property TimeGenerated,EntryType,Message
        }
        else {
            $winEvents = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName=$ServiceName} -MaxEvents 5 -ErrorAction SilentlyContinue
            if ($winEvents) { $winEvents | Format-List TimeCreated,LevelDisplayName,Message }
        }
    } catch {}

    Write-Note "sc.exe qc ${ServiceName}:"
    try { sc.exe qc $ServiceName } catch {}
    Write-Note "sc.exe query ${ServiceName}:"
    try { sc.exe query $ServiceName } catch {}
}

function Uninstall-Service {
    param(
        [string]$ServiceName,
        [string]$InstallDir,
        [bool]$IsRemote,
        [System.Management.Automation.Runspaces.PSSession]$Session
    )

    $script = {
        param($svc,$dir)
        $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
        if ($s) {
            if ($s.Status -ne 'Stopped') { Stop-Service $svc -Force }
            sc.exe delete $svc | Out-Null
        }
        if (Test-Path $dir) { Remove-Item -Recurse -Force -Path $dir }
    }

    if ($IsRemote) { Invoke-Command -Session $Session -ScriptBlock $script -ArgumentList $ServiceName, $InstallDir }
    else { & $script $ServiceName $InstallDir }
}

try {
    $dotnet = Resolve-DotNetPath
    Write-Note "Using dotnet at $dotnet"

    $targetName = Prompt-IfMissing $MachineName "Install to machine (enter 'local' for this computer)" "local"
    $targetIsRemote = $targetName -ne "local" -and $targetName -ne '.' -and $targetName -ne $env:COMPUTERNAME

    $InstallDirectory = Prompt-IfMissing $InstallDirectory "Install directory" "C:\\Program Files\\q-memory-mcp"
    $DataDirectory = Prompt-IfMissing $DataDirectory "Data directory" "C:\\ProgramData\\q-memory-mcp"
    $portInput = Prompt-IfMissing ($Port -gt 0 ? $Port.ToString() : $null) "HTTP port" "5080"
    $Port = [int]$portInput
    if ($Port -le 0) { $Port = 5080 }

    Ensure-ElevationIfNeeded -Path $InstallDirectory -IsRemote $targetIsRemote

    $ServiceAccount = Prompt-IfMissing $ServiceAccount "Service account (LocalSystem or DOMAIN\\user)" "LocalSystem"
    $acct,$acctPassword = Prompt-CredentialIfNeeded -Account $ServiceAccount -Password $ServiceAccountPassword

    $adminKeyInput = if ($Quiet) { '' } else { Read-Host "Admin API key (blank to auto-generate)" }
    $readerKeyInput = if ($Quiet) { '' } else { Read-Host "Reader API key (blank to auto-generate)" }
    $adminKey = if ([string]::IsNullOrWhiteSpace($adminKeyInput)) { New-RandomApiKey } else { $adminKeyInput }
    $readerKey = if ([string]::IsNullOrWhiteSpace($readerKeyInput)) { New-RandomApiKey } else { $readerKeyInput }

    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
    $publishRoot = Join-Path $env:TEMP "qms-publish-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

    if ($Uninstall) {
        if ($PSCmdlet.ShouldProcess($InstallDirectory, "Uninstall service")) {
            if ($targetIsRemote) {
                Write-Note "Opening remote session to $targetName"
                $session = New-PSSession -ComputerName $targetName
                Uninstall-Service -ServiceName 'QuickMemoryServer' -InstallDir $InstallDirectory -IsRemote $true -Session $session
                Remove-PSSession $session
            }
            else {
                Uninstall-Service -ServiceName 'QuickMemoryServer' -InstallDir $InstallDirectory -IsRemote $false -Session $null
            }
            Write-Note "Uninstall complete"
        }
        return
    }

    $httpUrl = "http://0.0.0.0:$Port"
    Write-Note "HTTP URL: $httpUrl"

    if ($ValidateOnly) { Write-Note "Running in validate-only mode" }

    if ($PSCmdlet.ShouldProcess($publishRoot, "Publish worker and tools")) {
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
        Update-TomlConfig -ConfigPath $configTarget -HttpUrl $httpUrl -DataDir $DataDirectory -AdminKey $adminKey -ReaderKey $readerKey

        New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot 'logs') | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot 'Backups') | Out-Null

        # Include docs for admin/agent help rendering
        if (Test-Path (Join-Path $repoRoot 'docs')) {
            Copy-Item -Path (Join-Path $repoRoot 'docs') -Destination (Join-Path $publishRoot 'docs') -Recurse -Force
        }

        Validate-Layout -PublishRoot $publishRoot -LayoutPath (Join-Path $repoRoot 'layout.json')
    }

    if ($ValidateOnly) { Write-Note "Validate-only complete"; return }

    $serviceName = 'QuickMemoryServer'
    $displayName = 'Quick Memory MCP'
    $serviceExe = Join-Path $InstallDirectory 'QuickMemoryServer.exe'

    $backupZip = $null
    $existingPreBackup = $null
    if (-not $targetIsRemote) {
        $existingPreBackup = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($existingPreBackup -and $existingPreBackup.Status -ne 'Stopped') {
            Write-Note "Stopping $serviceName before backup"
            Stop-Service $serviceName -Force
        }
    }

    try {
        if (-not $targetIsRemote) {
            $backupZip = Backup-ExistingInstall -InstallDir $InstallDirectory
            if ($backupZip) { Write-Note "Backed up existing install to $backupZip" }
        }

        if ($targetIsRemote) {
            Write-Note "Opening remote session to $targetName"
            $session = New-PSSession -ComputerName $targetName
            $overwriteConfig = Should-OverwriteConfig -IsRemote $true -InstallDir $InstallDirectory -Session $session
            # Stop service before copying to avoid file-locks
            Invoke-Command -Session $session -ScriptBlock { param($svc) $s = Get-Service -Name $svc -ErrorAction SilentlyContinue; if ($s -and $s.Status -ne 'Stopped') { Stop-Service $svc -Force } } -ArgumentList $serviceName
            Ensure-TargetStructure -BaseDir $InstallDirectory -DataDir $DataDirectory -IsRemote $true -Session $session
        Write-Note "Copying payload to ${targetName}:${InstallDirectory}"
        $copyParams = @{ Path = Join-Path $publishRoot '*'; Destination = $InstallDirectory; Recurse = $true; Force = $true; ToSession = $session; Exclude = @('logs','logs/*','logs/**') }
        if (-not $overwriteConfig) { $copyParams.Exclude = 'QuickMemoryServer.toml' }
        Copy-Item @copyParams
            Install-Or-UpdateServiceRemote -Session $session -Target $targetName -ServiceName $serviceName -BinPath $serviceExe -DisplayName $displayName -Account $acct -Password $acctPassword -SkipStart:$SkipStart
            if (-not $SkipFirewall) { Set-FirewallAndUrlAcl -Port $Port -Account $acct -IsRemote $true -Session $session }
            if ($GrantLogonRight -and $acct -and $acct -ne 'LocalSystem') { Invoke-Command -Session $session -ScriptBlock ${function:Grant-ServiceLogonRight} -ArgumentList $acct }
            Remove-PSSession $session
        }
        else {
            $overwriteConfig = Should-OverwriteConfig -IsRemote $false -InstallDir $InstallDirectory -Session $null
            $existingLocal = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($existingLocal -and $existingLocal.Status -ne 'Stopped') { Write-Note "Stopping $serviceName"; Stop-Service $serviceName -Force }
            Ensure-TargetStructure -BaseDir $InstallDirectory -DataDir $DataDirectory -IsRemote $false
        Write-Note "Copying payload to $InstallDirectory"
        $copyParams = @{ Path = Join-Path $publishRoot '*'; Destination = $InstallDirectory; Recurse = $true; Force = $true; Exclude = @('logs','logs/*','logs/**') }
        if (-not $overwriteConfig) { $copyParams.Exclude = 'QuickMemoryServer.toml' }
        Copy-Item @copyParams
            Install-Or-UpdateServiceLocal -ServiceName $serviceName -BinPath $serviceExe -DisplayName $displayName -Account $acct -Password $acctPassword -SkipStart:$SkipStart
            if (-not $SkipFirewall) { Set-FirewallAndUrlAcl -Port $Port -Account $acct -IsRemote $false -Session $null }
            if ($GrantLogonRight -and $acct -and $acct -ne 'LocalSystem') { Grant-ServiceLogonRight -Account $acct }
        }

        if (-not $SkipStart) {
            $healthHost = if ($targetIsRemote) { $targetName } else { 'localhost' }
            $healthy = Test-Health -TargetHost $healthHost -Port $Port
            if ($healthy) { Write-Note "Health check passed at http://${healthHost}:${Port}/health" }
            else {
                Write-Note "Warning: health endpoint not responding yet"
                if (-not $targetIsRemote) { Dump-LocalServiceLogs -InstallDir $InstallDirectory -ServiceName $serviceName -Tail 50 }
            }
        }
    }
    catch {
        Write-Note "Error: $_"
        if (-not $targetIsRemote -and $backupZip -and -not $NoRollback) {
            $doRollback = $Quiet ? $true : ((Read-Host "Start failed. Roll back to previous install? [Y/n]") -notmatch '^[nN]')
            if ($doRollback) {
                Write-Note "Attempting rollback from backup"
                Restore-Backup -Zip $backupZip -InstallDir $InstallDirectory
            }
            else {
                Write-Note "Rollback skipped; leaving files in place for investigation. Backup: $backupZip"
            }
        }
        throw
    }
    finally {
        if ($publishRoot -and (Test-Path $publishRoot)) { Remove-Item -Recurse -Force $publishRoot }
    }

    Write-Note "Install/upgrade complete. Service '$serviceName' is configured on port $Port using data at $DataDirectory"
    Write-Note "Admin API key: $adminKey"
    Write-Note "Reader API key: $readerKey"
}
finally {
    Stop-Transcript | Out-Null
    Write-Host "Transcript saved to $transcriptPath"
}
