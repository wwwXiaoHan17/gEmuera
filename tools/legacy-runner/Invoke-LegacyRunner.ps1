[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ConfigPath,
    [string]$GameRoot,
    [string]$Profile,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$RepeatCount = 3,
    [ValidateRange(5, 900)][int]$TimeoutSeconds = 360,
    [switch]$SkipBuild,
    [switch]$UseDisplayServer,
    [string]$ExistingIdentityDirectory,
    [switch]$AllowGameDirectoryWrites,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$schemaVersion = '1.0.0'
$requiredSemanticFiles = @('state.json', 'display.json', 'screenshots.json', 'hit-test.json', 'effects.json', 'errors.json', 'timeline.json', 'semantic-trace.json')
$requiredReportFiles = $requiredSemanticFiles + @('display.raw.json', 'trace.json', 'trace.raw.json', 'metrics.json', 'diagnostics.json', 'artifacts.json')
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $PSScriptRoot 'fixtures\first-wait.json'
}

function Get-NormalizedFullPath {
    param([string]$Path)
    return [IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-Sha256File {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Sha256Text {
    param([string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $utf8NoBom.GetBytes($Value)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Write-Json {
    param($Value, [string]$Path)
    $json = $Value | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($Path, $json + "`n", $utf8NoBom)
}

function Resolve-GodotExecutable {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Path).Path
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Godot path does not exist: $Path"
    }
    $candidate = Get-ChildItem -LiteralPath $Path -File -Filter '*_console.exe' |
        Where-Object { $_.Name -match '4\.7' -and $_.Name -match 'mono' } |
        Sort-Object Name | Select-Object -First 1
    if (-not $candidate) {
        $candidate = Get-ChildItem -LiteralPath $Path -File -Filter '*_console.exe' | Sort-Object Name | Select-Object -First 1
    }
    if (-not $candidate) {
        throw "Godot console executable was not found under: $Path"
    }
    return $candidate.FullName
}

function Quote-ProcessArgument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-CapturedProcess {
    param(
        [string]$Executable,
        [string]$Arguments,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds
    )
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Executable
    $startInfo.Arguments = $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutMilliseconds)
        if ($timedOut) {
            try { $process.Kill() } catch { }
        }
        $process.WaitForExit()
        return [ordered]@{
            exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
            timedOut = $timedOut
            stdout = $stdoutTask.GetAwaiter().GetResult()
            stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function New-ArtifactManifest {
    param([string]$RunDirectory)
    $entries = @()
    foreach ($file in (Get-ChildItem -LiteralPath $RunDirectory -File | Where-Object { $_.Name -ne 'artifacts.json' } | Sort-Object Name)) {
        $entries += [ordered]@{
            path = $file.Name
            bytes = [int64]$file.Length
            sha256 = Get-Sha256File -Path $file.FullName
        }
    }
    return [ordered]@{
        schemaVersion = $schemaVersion
        status = 'Captured'
        fileCount = $entries.Count
        entries = $entries
    }
}

function Copy-IsolatedGameFixture {
    param([string]$Source, [string]$Destination, [string]$RunDirectory)
    $destinationFull = [IO.Path]::GetFullPath($Destination)
    $runFull = [IO.Path]::GetFullPath($RunDirectory).TrimEnd('\') + '\'
    if (-not (($destinationFull.TrimEnd('\') + '\').StartsWith($runFull, [StringComparison]::OrdinalIgnoreCase))) {
        throw "Isolated fixture target escaped the run directory: $destinationFull"
    }
    if (Test-Path -LiteralPath $destinationFull) {
        throw "Isolated fixture target must not exist: $destinationFull"
    }
    [IO.Directory]::CreateDirectory($destinationFull) | Out-Null
    $copy = Invoke-CapturedProcess -Executable 'robocopy' `
        -Arguments ((Quote-ProcessArgument $Source) + ' ' + (Quote-ProcessArgument $destinationFull) + ' /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP') `
        -WorkingDirectory $RunDirectory -TimeoutMilliseconds 600000
    [IO.File]::WriteAllText((Join-Path $RunDirectory 'fixture-copy.stdout.log'), $copy.stdout, $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $RunDirectory 'fixture-copy.stderr.log'), $copy.stderr, $utf8NoBom)
    if ($copy.timedOut -or $copy.exitCode -gt 7) {
        throw "Isolated fixture copy failed with robocopy exit code $($copy.exitCode)."
    }
    return $destinationFull
}

function New-FixtureMutationReport {
    param([string]$Source, [string]$RuntimeCopy)
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
    $runtimeRoot = [IO.Path]::GetFullPath($RuntimeCopy).TrimEnd('\') + '\'
    $sourceFiles = @{}
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        $sourceFiles[$relative] = $file
    }
    $changes = @()
    foreach ($file in Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse -Force) {
        $relative = $file.FullName.Substring($runtimeRoot.Length).Replace('\', '/')
        $sourceFile = $sourceFiles[$relative]
        if (-not $sourceFile) {
            $changes += [ordered]@{ path = $relative; change = 'Added'; bytes = [int64]$file.Length; sha256 = Get-Sha256File -Path $file.FullName }
            continue
        }
        [void]$sourceFiles.Remove($relative)
        if ($file.Length -ne $sourceFile.Length -or $file.LastWriteTimeUtc -ne $sourceFile.LastWriteTimeUtc) {
            $runtimeHash = Get-Sha256File -Path $file.FullName
            $sourceHash = Get-Sha256File -Path $sourceFile.FullName
            if ($runtimeHash -ne $sourceHash) {
                $changes += [ordered]@{ path = $relative; change = 'Modified'; bytes = [int64]$file.Length; beforeSha256 = $sourceHash; sha256 = $runtimeHash }
            }
        }
    }
    foreach ($remaining in ($sourceFiles.Keys | Sort-Object)) {
        $changes += [ordered]@{ path = $remaining; change = 'Deleted' }
    }
    return [ordered]@{
        schemaVersion = $schemaVersion
        sourceGameRoot = Get-NormalizedFullPath -Path $Source
        runtimeCopyRemovedAfterCapture = $true
        changeCount = $changes.Count
        changes = $changes
    }
}

function Remove-IsolatedGameFixture {
    param(
        [string]$Fixture,
        [string]$RunDirectory,
        [ValidateSet('runtime-game', 'runtime-alternate-game')][string]$ExpectedDirectoryName = 'runtime-game'
    )
    $fixtureFull = [IO.Path]::GetFullPath($Fixture).TrimEnd('\') + '\'
    $runFull = [IO.Path]::GetFullPath($RunDirectory).TrimEnd('\') + '\'
    $fixtureDirectoryName = [IO.Path]::GetFileName($fixtureFull.TrimEnd('\'))
    if (-not $fixtureFull.StartsWith($runFull, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($fixtureDirectoryName, $ExpectedDirectoryName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unverified fixture path: $fixtureFull"
    }
    if (Test-Path -LiteralPath $Fixture) {
        Remove-Item -LiteralPath $Fixture -Recurse -Force
    }
}

function Invoke-RunnerBaselineIdentity {
    param(
        [string]$ProjectRoot,
        [string]$OutputDirectory,
        [string]$GameRoot,
        [string]$GodotExecutable,
        [string]$LogPrefix
    )

    $identityScript = Join-Path $ProjectRoot 'tools\baseline-identity\Invoke-BaselineIdentity.ps1'
    $identityArguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Quote-ProcessArgument $identityScript),
        '-ProjectRoot', (Quote-ProcessArgument $ProjectRoot),
        '-OutputDirectory', (Quote-ProcessArgument $OutputDirectory),
        '-GameRoot', (Quote-ProcessArgument $GameRoot),
        '-GodotExecutable', (Quote-ProcessArgument $GodotExecutable),
        '-ArtifactPath', (Quote-ProcessArgument (Join-Path $ProjectRoot '.godot\mono\temp\bin\Debug\gemuera-c#.dll'))
    ) -join ' '
    $identity = Invoke-CapturedProcess -Executable 'powershell' -Arguments $identityArguments -WorkingDirectory $ProjectRoot -TimeoutMilliseconds 600000
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $OutputDirectory) ($LogPrefix + '.stdout.log')), $identity.stdout, $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $OutputDirectory) ($LogPrefix + '.stderr.log')), $identity.stderr, $utf8NoBom)
    if ($identity.exitCode -ne 0) {
        throw "Baseline identity failed with exit code $($identity.exitCode)."
    }
}

try {
    $projectRootResolved = (Resolve-Path -LiteralPath $ProjectRoot).Path
    if (-not (Test-Path -LiteralPath (Join-Path $projectRootResolved 'project.godot') -PathType Leaf)) {
        throw "project.godot was not found under: $projectRootResolved"
    }
    $configPathResolved = (Resolve-Path -LiteralPath $ConfigPath).Path
    $godotExecutable = Resolve-GodotExecutable -Path $GodotPath
    $godotVersionResult = Invoke-CapturedProcess -Executable $godotExecutable -Arguments '--version' -WorkingDirectory $projectRootResolved -TimeoutMilliseconds 30000
    $godotVersion = ($godotVersionResult.stdout + "`n" + $godotVersionResult.stderr).Trim()
    if ($godotVersionResult.exitCode -ne 0 -or -not $godotVersion.StartsWith('4.7.', [StringComparison]::OrdinalIgnoreCase)) {
        throw "M0 runner requires Godot 4.7 Mono; captured: $godotVersion"
    }

    $baseConfig = Get-Content -LiteralPath $configPathResolved -Raw | ConvertFrom-Json
    if ($GameRoot) { $baseConfig.gameRoot = [IO.Path]::GetFullPath($GameRoot) }
    if ($Profile) { $baseConfig.profile = $Profile }
    if (-not $baseConfig.gameRoot -or -not (Test-Path -LiteralPath $baseConfig.gameRoot -PathType Container)) {
        throw 'An existing game root must be supplied through -GameRoot or the config file.'
    }
    if ($baseConfig.profile -notin @('v24pure', 'snake', 'erafl', 'megaten')) {
        throw 'Profile must be v24pure, snake, erafl, or megaten.'
    }
    $sessionIsolationProperty = $baseConfig.PSObject.Properties['sessionIsolationMode']
    $sessionIsolationMode = if ($null -eq $sessionIsolationProperty) { 'baseline' } else { [string]$sessionIsolationProperty.Value }
    $inProcessCycleProperty = $baseConfig.PSObject.Properties['inProcessSessionCycle']
    if ($null -eq $inProcessCycleProperty) {
        $baseConfig | Add-Member -NotePropertyName 'inProcessSessionCycle' -NotePropertyValue 'disabled'
    }
    $inProcessSessionCycle = [string]$baseConfig.inProcessSessionCycle
    $inProcessSwitchCountProperty = $baseConfig.PSObject.Properties['inProcessSessionSwitchCount']
    if ($null -eq $inProcessSwitchCountProperty) {
        $baseConfig | Add-Member -NotePropertyName 'inProcessSessionSwitchCount' -NotePropertyValue 2
    }
    $inProcessSessionSwitchCount = [int]$baseConfig.inProcessSessionSwitchCount
    $inProcessSessionCycleActive = $inProcessSessionCycle -ieq 'aba' -or $inProcessSessionCycle -ieq 'cross-aba'
    if ($inProcessSessionSwitchCount -lt 2 -or $inProcessSessionSwitchCount -gt 100) {
        throw 'in-process session switch count must be between 2 and 100.'
    }
    if (-not $inProcessSessionCycleActive -and $inProcessSessionSwitchCount -ne 2) {
        throw 'in-process session switch count requires an active inProcessSessionCycle.'
    }
    if ($inProcessSessionCycle -ieq 'cross-aba' -and ($inProcessSessionSwitchCount % 2) -ne 0) {
        throw 'cross-aba requires an even in-process session switch count.'
    }
    $sourceGameRoot = [IO.Path]::GetFullPath([string]$baseConfig.gameRoot)
    $alternateSourceGameRoot = ''
    $alternateProfile = ''
    $alternateSessionProperty = $baseConfig.PSObject.Properties['inProcessAlternateSession']
    if ($inProcessSessionCycle -ieq 'cross-aba') {
        if ($null -eq $alternateSessionProperty -or $null -eq $alternateSessionProperty.Value) {
            throw 'cross-aba requires inProcessAlternateSession.'
        }
        $alternateSession = $alternateSessionProperty.Value
        if (-not $alternateSession.gameRoot -or -not (Test-Path -LiteralPath $alternateSession.gameRoot -PathType Container)) {
            throw 'cross-aba alternate session requires an existing gameRoot.'
        }
        if ($alternateSession.profile -notin @('v24pure', 'snake', 'erafl', 'megaten')) {
            throw 'cross-aba alternate session profile must be v24pure, snake, erafl, or megaten.'
        }
        $alternateSourceGameRoot = [IO.Path]::GetFullPath([string]$alternateSession.gameRoot)
        $alternateProfile = [string]$alternateSession.profile
    }
    elseif ($null -ne $alternateSessionProperty) {
        throw 'inProcessAlternateSession requires inProcessSessionCycle=cross-aba.'
    }
    if ($ValidateOnly) {
        [ordered]@{
            status = 'Valid'
            godotVersion = $godotVersion
            godotSha256 = Get-Sha256File -Path $godotExecutable
            gameRoot = Get-NormalizedFullPath -Path $baseConfig.gameRoot
            profile = $baseConfig.profile
            inProcessSessionCycle = $inProcessSessionCycle
            inProcessSessionSwitchCount = $inProcessSessionSwitchCount
            alternateSession = if ([string]::IsNullOrWhiteSpace($alternateSourceGameRoot)) { $null } else {
                [ordered]@{
                    gameRoot = Get-NormalizedFullPath -Path $alternateSourceGameRoot
                    profile = $alternateProfile
                }
            }
        } | ConvertTo-Json -Depth 10
        exit 0
    }

    $outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $outputFullPath) {
        if ((Get-ChildItem -LiteralPath $outputFullPath -Force | Select-Object -First 1)) {
            throw "Output directory must be absent or empty: $outputFullPath"
        }
    }
    [IO.Directory]::CreateDirectory($outputFullPath) | Out-Null
    $requiredReportFilesForRun = @($requiredReportFiles)
    if ($inProcessSessionCycleActive) {
        $requiredReportFilesForRun += 'in-process-session-cycle.json'
    }

    if (-not $SkipBuild) {
        $solutionPath = Join-Path $projectRootResolved 'gemuera-c#.sln'
        if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
            throw "M0 runner build solution was not found: $solutionPath"
        }
        $build = Invoke-CapturedProcess -Executable 'dotnet' -Arguments ('build ' + (Quote-ProcessArgument $solutionPath) + ' -c Debug --no-restore') -WorkingDirectory $projectRootResolved -TimeoutMilliseconds 180000
        [IO.File]::WriteAllText((Join-Path $outputFullPath 'build.stdout.log'), $build.stdout, $utf8NoBom)
        [IO.File]::WriteAllText((Join-Path $outputFullPath 'build.stderr.log'), $build.stderr, $utf8NoBom)
        if ($build.exitCode -ne 0) {
            throw "C# build failed with exit code $($build.exitCode). The Godot C# solution must be prepared before running the harness."
        }
    }

    $identityDirectory = Join-Path $outputFullPath 'identity'
    if ($ExistingIdentityDirectory) {
        $existingIdentity = (Resolve-Path -LiteralPath $ExistingIdentityDirectory).Path
        $existingIdentityPath = Join-Path $existingIdentity 'identity.json'
        if (-not (Test-Path -LiteralPath $existingIdentityPath -PathType Leaf)) {
            throw "Existing identity directory has no identity.json: $existingIdentity"
        }
        $identityReport = Get-Content -LiteralPath $existingIdentityPath -Raw | ConvertFrom-Json
        if ((Get-NormalizedFullPath -Path $identityReport.source.root) -ne (Get-NormalizedFullPath -Path $projectRootResolved)) {
            throw 'Existing identity source root does not match this project.'
        }
        if ((Get-NormalizedFullPath -Path $identityReport.game.root) -ne (Get-NormalizedFullPath -Path $sourceGameRoot)) {
            throw 'Existing identity game root does not match this run.'
        }
        if ($identityReport.toolchain.godot.executableSha256 -ne (Get-Sha256File -Path $godotExecutable)) {
            throw 'Existing identity Godot executable hash does not match this run.'
        }
        [IO.Directory]::CreateDirectory($identityDirectory) | Out-Null
        Get-ChildItem -LiteralPath $existingIdentity -File | Copy-Item -Destination $identityDirectory
        [IO.File]::WriteAllText((Join-Path $outputFullPath 'identity.reused.log'), (Get-NormalizedFullPath -Path $existingIdentity), $utf8NoBom)
    }
    else {
        Invoke-RunnerBaselineIdentity -ProjectRoot $projectRootResolved -OutputDirectory $identityDirectory `
            -GameRoot $sourceGameRoot -GodotExecutable $godotExecutable -LogPrefix 'identity'
    }

    $alternateIdentityDirectory = $null
    $alternateUsesPrimarySource = -not [string]::IsNullOrWhiteSpace($alternateSourceGameRoot) -and
        [string]::Equals(
            (Get-NormalizedFullPath -Path $alternateSourceGameRoot),
            (Get-NormalizedFullPath -Path $sourceGameRoot),
            [StringComparison]::OrdinalIgnoreCase)
    if (-not [string]::IsNullOrWhiteSpace($alternateSourceGameRoot)) {
        if ($alternateUsesPrimarySource) {
            $alternateIdentityDirectory = $identityDirectory
        }
        else {
            $alternateIdentityDirectory = Join-Path $outputFullPath 'identity-alternate'
            Invoke-RunnerBaselineIdentity -ProjectRoot $projectRootResolved -OutputDirectory $alternateIdentityDirectory `
                -GameRoot $alternateSourceGameRoot -GodotExecutable $godotExecutable -LogPrefix 'identity-alternate'
        }
    }

    $runSummaries = @()
    $semanticHashes = @()
    $transportTraceHashes = @()
    for ($runIndex = 1; $runIndex -le $RepeatCount; $runIndex++) {
        $runName = 'run-{0:D3}' -f $runIndex
        $runDirectory = Join-Path $outputFullPath $runName
        [IO.Directory]::CreateDirectory($runDirectory) | Out-Null
        $runtimeGameRoot = $sourceGameRoot
        if (-not $AllowGameDirectoryWrites) {
            $runtimeGameRoot = Copy-IsolatedGameFixture -Source $sourceGameRoot -Destination (Join-Path $runDirectory 'runtime-game') -RunDirectory $runDirectory
        }
        $runtimeAlternateGameRoot = ''
        if (-not [string]::IsNullOrWhiteSpace($alternateSourceGameRoot)) {
            if ($alternateUsesPrimarySource) {
                $runtimeAlternateGameRoot = $runtimeGameRoot
            }
            else {
                $runtimeAlternateGameRoot = $alternateSourceGameRoot
                if (-not $AllowGameDirectoryWrites) {
                    $runtimeAlternateGameRoot = Copy-IsolatedGameFixture -Source $alternateSourceGameRoot -Destination (Join-Path $runDirectory 'runtime-alternate-game') -RunDirectory $runDirectory
                }
            }
        }
        $runConfigPath = Join-Path $runDirectory 'runner-config.json'
        $baseConfig.gameRoot = $runtimeGameRoot
        if (-not [string]::IsNullOrWhiteSpace($runtimeAlternateGameRoot)) {
            $baseConfig.inProcessAlternateSession.gameRoot = $runtimeAlternateGameRoot
            $baseConfig.inProcessAlternateSession.profile = $alternateProfile
        }
        $baseConfig.outputDirectory = $runDirectory
        Write-Json -Value $baseConfig -Path $runConfigPath
        Copy-Item -LiteralPath (Join-Path $identityDirectory 'identity.json') -Destination (Join-Path $runDirectory 'identity.json')
        if ($null -ne $alternateIdentityDirectory) {
            Copy-Item -LiteralPath (Join-Path $alternateIdentityDirectory 'identity.json') -Destination (Join-Path $runDirectory 'alternate-identity.json')
        }

        $godotLogPath = Join-Path $runDirectory 'godot.log'
        $argumentParts = @()
        if (-not $UseDisplayServer) { $argumentParts += '--headless' }
        $argumentParts += @(
            '--path', (Quote-ProcessArgument $projectRootResolved),
            '--scene', 'res://tools/legacy-runner/legacy_runner.tscn',
            '--language', 'zh',
            '--log-file', (Quote-ProcessArgument $godotLogPath),
            '--',
            (Quote-ProcessArgument ('--m0-runner-config=' + $runConfigPath))
        )
        $arguments = $argumentParts -join ' '
        $startedAtUtc = [DateTime]::UtcNow
        $run = Invoke-CapturedProcess -Executable $godotExecutable -Arguments $arguments -WorkingDirectory $projectRootResolved -TimeoutMilliseconds ($TimeoutSeconds * 1000)
        $finishedAtUtc = [DateTime]::UtcNow
        [IO.File]::WriteAllText((Join-Path $runDirectory 'stdout.log'), $run.stdout, $utf8NoBom)
        [IO.File]::WriteAllText((Join-Path $runDirectory 'stderr.log'), $run.stderr, $utf8NoBom)

        $missingReports = @($requiredReportFilesForRun | Where-Object { -not (Test-Path -LiteralPath (Join-Path $runDirectory $_) -PathType Leaf) })
        $canonicalEntries = @()
        foreach ($name in $requiredSemanticFiles) {
            $path = Join-Path $runDirectory $name
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $canonicalEntries += "${name}`t$(Get-Sha256File -Path $path)"
            }
        }
        $semanticHash = if ($canonicalEntries.Count -eq $requiredSemanticFiles.Count) {
            Get-Sha256Text -Value ([string]::Join("`n", [string[]]$canonicalEntries))
        } else { '' }
        $semanticHashes += $semanticHash
        $transportTracePath = Join-Path $runDirectory 'trace.json'
        $transportTraceHash = if (Test-Path -LiteralPath $transportTracePath -PathType Leaf) {
            Get-Sha256File -Path $transportTracePath
        } else { '' }
        $transportTraceHashes += $transportTraceHash

        $runStatus = if ($run.exitCode -eq 0 -and $missingReports.Count -eq 0) { 'Passed' } else { 'Failed' }
        $runReport = [ordered]@{
            schemaVersion = $schemaVersion
            workPackage = 'M0-RUN-01'
            run = $runIndex
            status = $runStatus
            exitCode = $run.exitCode
            timedOut = $run.timedOut
            startedAtUtc = $startedAtUtc.ToString('o')
            finishedAtUtc = $finishedAtUtc.ToString('o')
            semanticSha256 = $semanticHash
            transportTraceSha256 = $transportTraceHash
            inProcessSessionCycle = $inProcessSessionCycle
            inProcessSessionSwitchCount = $inProcessSessionSwitchCount
            alternateGameRoot = if ([string]::IsNullOrWhiteSpace($runtimeAlternateGameRoot)) { '' } else { Get-NormalizedFullPath -Path $runtimeAlternateGameRoot }
            alternateProfile = $alternateProfile
            missingReports = $missingReports
        }
        Write-Json -Value $runReport -Path (Join-Path $runDirectory 'run.json')

        if (-not $AllowGameDirectoryWrites) {
            Write-Json -Value (New-FixtureMutationReport -Source $sourceGameRoot -RuntimeCopy $runtimeGameRoot) -Path (Join-Path $runDirectory 'fixture-mutations.json')
            Remove-IsolatedGameFixture -Fixture $runtimeGameRoot -RunDirectory $runDirectory
            if (-not [string]::IsNullOrWhiteSpace($runtimeAlternateGameRoot) -and -not $alternateUsesPrimarySource) {
                Write-Json -Value (New-FixtureMutationReport -Source $alternateSourceGameRoot -RuntimeCopy $runtimeAlternateGameRoot) -Path (Join-Path $runDirectory 'alternate-fixture-mutations.json')
                Remove-IsolatedGameFixture -Fixture $runtimeAlternateGameRoot -RunDirectory $runDirectory -ExpectedDirectoryName 'runtime-alternate-game'
            }
        }

        $diagnosticInputs = @('godot.log', 'stdout.log', 'stderr.log', 'diagnostics.json', 'emuera_startup_errors.log', 'emuera.log') |
            ForEach-Object { Join-Path $runDirectory $_ } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
        if ($diagnosticInputs.Count -gt 0) {
            Compress-Archive -LiteralPath $diagnosticInputs -DestinationPath (Join-Path $runDirectory 'diagnostics.zip') -CompressionLevel Optimal
        }
        Write-Json -Value (New-ArtifactManifest -RunDirectory $runDirectory) -Path (Join-Path $runDirectory 'artifacts.json')
        $runSummaries += $runReport
    }

    $consistent = $semanticHashes.Count -eq $RepeatCount -and
        @($semanticHashes | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0 -and
        @($semanticHashes | Select-Object -Unique).Count -eq 1
    $transportTraceConsistent = $transportTraceHashes.Count -eq $RepeatCount -and
        @($transportTraceHashes | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0 -and
        @($transportTraceHashes | Select-Object -Unique).Count -eq 1
    $allPassed = @($runSummaries | Where-Object { $_.status -ne 'Passed' }).Count -eq 0
    $uncovered = @(
        'Trace coverage outside the current EraTW first-wait fixture',
        'M0-DSP-01 eraFL nested div/srcb/dynamic-map/data-only and signed dual-backend evidence',
        'APK and Android device execution',
        'Upstream and v24pure fixture runs',
        'Human review and approval signatures'
    )
    if (-not $transportTraceConsistent) {
        $uncovered += 'UI projection transport batches differ across repeats; raw trace.json remains retained for diagnosis.'
    }
    $summary = [ordered]@{
        schemaVersion = $schemaVersion
        workPackage = 'M0-RUN-01'
        executionStatus = if ($allPassed -and $consistent) { 'InProgress' } else { 'InProgress' }
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = if ($allPassed -and $consistent) { 'PassedRunnerConsistency' } else { 'Failed' }
        semanticReportsConsistent = $consistent
        transportTraceReportsConsistent = $transportTraceConsistent
        repeatCount = $RepeatCount
        godot = [ordered]@{
            version = $godotVersion
            executable = Get-NormalizedFullPath -Path $godotExecutable
            sha256 = Get-Sha256File -Path $godotExecutable
        }
        gameRoot = Get-NormalizedFullPath -Path $sourceGameRoot
        isolatedGameCopy = -not $AllowGameDirectoryWrites
        profile = $baseConfig.profile
        sessionIsolationMode = $sessionIsolationMode
        inProcessSessionCycle = $inProcessSessionCycle
        inProcessSessionSwitchCount = $inProcessSessionSwitchCount
        alternateSession = if ([string]::IsNullOrWhiteSpace($alternateSourceGameRoot)) { $null } else {
            [ordered]@{
                gameRoot = Get-NormalizedFullPath -Path $alternateSourceGameRoot
                profile = $alternateProfile
                sharesPrimaryFixtureSource = $alternateUsesPrimarySource
            }
        }
        preparedBy = [Environment]::UserName
        reviewedBy = ''
        approvedBy = ''
        gateDecision = 'NeedsEvidence'
        compatibility = @('None: observation-only runner')
        uncovered = $uncovered
        runs = $runSummaries
    }
    Write-Json -Value $summary -Path (Join-Path $outputFullPath 'summary.json')
    $summary | ConvertTo-Json -Depth 30
    if (-not $allPassed -or -not $consistent) { exit 2 }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
