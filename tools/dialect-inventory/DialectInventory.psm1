Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)
$script:BranchMarkers = @(
    [ordered]@{ id = 'profile.core-state'; regex = '\b(?:CoreProfile|EmueraCoreProfile)\b' },
    [ordered]@{ id = 'profile.is-snake'; regex = '\bIsSnakeProfile\b' },
    [ordered]@{ id = 'profile.is-snake-modern-mobile'; regex = '\bIsSnakeModernMobileProfile\b' },
    [ordered]@{ id = 'profile.selected-name'; regex = '\bSelectedCoreProfileName\b' },
    [ordered]@{ id = 'profile.v24-id'; regex = '\bCoreProfileV24Pure\b' },
    [ordered]@{ id = 'profile.snake-id'; regex = '\bCoreProfileSnake\b' },
    [ordered]@{ id = 'profile.detect'; regex = '\bDetectCoreProfile\b' },
    [ordered]@{ id = 'registry.v24-method'; regex = '\baddV24CompatibilityFunctions\b' },
    [ordered]@{ id = 'registry.snake-method'; regex = '\baddSnakeCompatibilityFunctions\b' },
    [ordered]@{ id = 'setting.scoped-variable-instruction'; regex = '\bUseScopedVariableInstruction\b' }
)

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha256.Dispose() }
}

function Get-RelativeSourcePath {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$Path)

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source path escapes project root: $fullPath"
    }
    return $fullPath.Substring($rootPath.Length).Replace('\', '/')
}

function Sort-Ordinal {
    param([object[]]$Items, [Parameter(Mandatory = $true)][scriptblock]$KeySelector)

    $map = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $index = 0
    foreach ($item in @($Items)) {
        $key = [string](& $KeySelector $item)
        $map.Add(($key + [char]0 + $index.ToString('D10', [Globalization.CultureInfo]::InvariantCulture)), $item)
        $index++
    }
    return @($map.Values)
}

function Get-Classification {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][AllowNull()][object[]]$Rules,
        [Parameter(Mandatory = $true)][string]$MarkerId,
        [Parameter(Mandatory = $true)][string]$SourceFile
    )

    $matches = @($Rules | Where-Object {
        $_.markerId -eq $MarkerId -and [regex]::IsMatch($SourceFile, [string]$_.fileRegex, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    })
    if ($matches.Count -eq 0) {
        throw "Unmapped dialect branch hit: marker=$MarkerId file=$SourceFile"
    }
    if ($matches.Count -gt 1) {
        throw "Ambiguous dialect branch classification: marker=$MarkerId file=$SourceFile rules=$(@($matches.id) -join ',')"
    }
    return $matches[0]
}

function Get-CSharpBlock {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$DeclarationRegex,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $declarationLines = @()
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match $DeclarationRegex) { $declarationLines += $i }
    }
    if ($declarationLines.Count -ne 1) {
        throw "Expected exactly one $Label declaration; found $($declarationLines.Count)."
    }

    $start = $declarationLines[0]
    $depth = 0
    $opened = $false
    for ($i = $start; $i -lt $Lines.Count; $i++) {
        foreach ($character in $Lines[$i].ToCharArray()) {
            if ($character -eq '{') { $depth++; $opened = $true }
            elseif ($character -eq '}') { $depth-- }
        }
        if ($opened -and $depth -eq 0) {
            return [pscustomobject][ordered]@{ startIndex = $start; endIndex = $i; lines = @($Lines[$start..$i]) }
        }
        if ($opened -and $depth -lt 0) { break }
    }
    throw "Could not locate the end of C# block: $Label"
}

function Get-InstructionRegistrations {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $relativePath = 'Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs'
    $path = Join-Path $ProjectRoot ($relativePath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $lines = [IO.File]::ReadAllLines([IO.Path]::GetFullPath($path), [Text.Encoding]::UTF8)
    $definitions = @(
        [ordered]@{ id = 'legacy-static-common'; declaration = '^\s*static\s+FunctionIdentifier\s*\(\s*\)'; defaultTarget = 'legacy.common.unresolved'; currentGuard = 'none (static initialization)' },
        [ordered]@{ id = 'gemuera-v24-method'; declaration = '^\s*private\s+static\s+void\s+addV24CompatibilityFunctions\s*\('; defaultTarget = 'gemuera.v24'; currentGuard = 'none (currently called unconditionally)' },
        [ordered]@{ id = 'game-snake-method'; declaration = '^\s*private\s+static\s+void\s+addSnakeCompatibilityFunctions\s*\('; defaultTarget = 'game.snake'; currentGuard = 'none (currently called unconditionally)' }
    )

    $registrations = New-Object System.Collections.Generic.List[object]
    foreach ($definition in $definitions) {
        $block = Get-CSharpBlock -Lines $lines -DeclarationRegex $definition.declaration -Label $definition.id
        for ($offset = 0; $offset -lt $block.lines.Count; $offset++) {
            $line = [string]$block.lines[$offset]
            if ($line.TrimStart().StartsWith('//', [StringComparison]::Ordinal)) { continue }
            $match = [regex]::Match($line, 'add(?<kind>Function|PrintFunction|PrintDataFunction)\s*\(\s*FunctionCode\.(?<key>[A-Za-z0-9_]+)(?<tail>.*)\)\s*;')
            if (-not $match.Success) { continue }

            $kind = $match.Groups['kind'].Value
            $tail = $match.Groups['tail'].Value
            if ($kind -eq 'PrintFunction') { $handler = 'PRINT_Instruction' }
            elseif ($kind -eq 'PrintDataFunction') { $handler = 'PRINT_DATA_Instruction' }
            else {
                $handlerMatch = [regex]::Match($tail, '\bnew\s+(?<handler>[A-Za-z_][A-Za-z0-9_]*)')
                if ($handlerMatch.Success) { $handler = $handlerMatch.Groups['handler'].Value }
                else { $handler = $tail.Trim().TrimStart(',').Trim() }
            }

            $targetModule = [string]$definition.defaultTarget
            $provenanceWarning = ''
            if ($handler -match '^SNAKE_' -and $definition.id -ne 'game-snake-method') {
                $targetModule = 'game.snake.candidate'
                $provenanceWarning = 'Snake-named handler is registered outside the Snake contribution and requires fixture-backed ownership review.'
            }
            $guard = [string]$definition.currentGuard
            if ($match.Groups['key'].Value -in @('VARI', 'VARS') -and $definition.id -eq 'game-snake-method') {
                $guard = 'Config.UseScopedVariableInstruction'
            }

            $registrations.Add([pscustomobject][ordered]@{
                publicKey = $match.Groups['key'].Value
                registryKind = 'instruction'
                sourceFile = $relativePath
                sourceLine = $block.startIndex + $offset + 1
                currentContribution = $definition.id
                currentGuard = $guard
                handler = $handler
                targetModule = $targetModule
                provenanceWarning = $provenanceWarning
            })
        }
    }

    $groups = @($registrations | Group-Object publicKey | Where-Object Count -gt 1)
    if ($groups.Count -gt 0) {
        $details = @($groups | ForEach-Object { $_.Name + '=' + @($_.Group.currentContribution) -join ',' }) -join '; '
        throw "Duplicate instruction registration detected: $details"
    }
    return Sort-Ordinal -Items $registrations.ToArray() -KeySelector { param($item) $item.publicKey + [char]0 + $item.currentContribution }
}

function Get-ExpressionRegistrations {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $relativePath = 'Scripts/Emuera/GameData/Function/Creator.cs'
    $path = Join-Path $ProjectRoot ($relativePath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($path), [Text.Encoding]::UTF8)
    $text = [regex]::Replace($text, '/\*.*?\*/', '', [Text.RegularExpressions.RegexOptions]::Singleline)
    $text = [regex]::Replace($text, '//.*$', '', [Text.RegularExpressions.RegexOptions]::Multiline)
    $matches = [regex]::Matches($text, '\[\s*"(?<key>(?:\\.|[^"\\])*)"\s*\]\s*=\s*new\s+(?<handler>[A-Za-z_][A-Za-z0-9_]*)\s*\(', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $registrations = New-Object System.Collections.Generic.List[object]
    foreach ($match in $matches) {
        $key = [Text.RegularExpressions.Regex]::Unescape($match.Groups['key'].Value)
        $handler = $match.Groups['handler'].Value
        $prefix = $text.Substring(0, $match.Index)
        $lineNumber = ([regex]::Matches($prefix, "`n").Count + 1)
        $targetModule = if ($handler -match 'Snake') { 'game.snake.candidate' } else { 'legacy.expression.unresolved' }
        $warning = if ($handler -match 'Snake') { 'Snake-named expression handler is currently present in the unconditional method dictionary.' } else { '' }
        $registrations.Add([pscustomobject][ordered]@{
            publicKey = $key
            registryKind = 'expression-function'
            sourceFile = $relativePath
            sourceLine = $lineNumber
            currentContribution = 'legacy-expression-method-list'
            currentGuard = 'none (static initialization)'
            handler = $handler
            targetModule = $targetModule
            provenanceWarning = $warning
        })
    }

    $groups = @($registrations | Group-Object publicKey | Where-Object Count -gt 1)
    if ($groups.Count -gt 0) {
        throw "Duplicate expression registration detected: $(@($groups.Name) -join ',')"
    }
    return Sort-Ordinal -Items $registrations.ToArray() -KeySelector { param($item) $item.publicKey }
}

function New-DialectInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ClassificationPath,
        [string]$OutputPath = ''
    )

    $resolvedRoot = [IO.Path]::GetFullPath($ProjectRoot)
    $scriptsRoot = Join-Path $resolvedRoot 'Scripts'
    if (-not (Test-Path -LiteralPath $scriptsRoot -PathType Container)) { throw "Scripts root does not exist: $scriptsRoot" }
    $classificationFullPath = [IO.Path]::GetFullPath($ClassificationPath)
    if (-not (Test-Path -LiteralPath $classificationFullPath -PathType Leaf)) { throw "Classification catalog does not exist: $classificationFullPath" }
    $catalog = Get-Content -LiteralPath $classificationFullPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($catalog.schemaVersion -ne '1.0.0' -or $catalog.workPackage -ne 'M0-DIA-01') { throw 'Unsupported dialect classification catalog.' }
    $rules = @($catalog.classifications)

    $sourceFiles = @([IO.Directory]::EnumerateFiles($scriptsRoot, '*.cs', [IO.SearchOption]::AllDirectories))
    $sourceFiles = Sort-Ordinal -Items $sourceFiles -KeySelector { param($path) Get-RelativeSourcePath -Root $resolvedRoot -Path $path }
    $branchHits = New-Object System.Collections.Generic.List[object]
    $hashedSources = @{}

    foreach ($path in $sourceFiles) {
        $relativePath = Get-RelativeSourcePath -Root $resolvedRoot -Path $path
        if ($relativePath -match '/(?:bin|obj)/') { continue }
        $lines = [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            foreach ($marker in $script:BranchMarkers) {
                if (-not [regex]::IsMatch($lines[$lineIndex], $marker.regex, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) { continue }
                $classification = Get-Classification -Rules $rules -MarkerId $marker.id -SourceFile $relativePath
                $branchHits.Add([pscustomobject][ordered]@{
                    markerId = $marker.id
                    sourceFile = $relativePath
                    sourceLine = $lineIndex + 1
                    sourceText = $lines[$lineIndex].Trim()
                    classificationId = $classification.id
                    category = $classification.category
                    currentOwner = $classification.currentOwner
                    intendedOwner = $classification.intendedOwner
                    targetModule = $classification.targetModule
                    behaviorKey = $classification.behaviorKey
                    capabilityId = $classification.capabilityId
                    fixtureId = $classification.fixtureId
                    notes = $classification.notes
                })
                $hashedSources[$relativePath] = $path
            }
        }
    }

    $instructionRegistrations = @(Get-InstructionRegistrations -ProjectRoot $resolvedRoot)
    $expressionRegistrations = @(Get-ExpressionRegistrations -ProjectRoot $resolvedRoot)
    foreach ($registration in @($instructionRegistrations + $expressionRegistrations)) {
        $sourcePath = Join-Path $resolvedRoot ($registration.sourceFile.Replace('/', '\'))
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { $hashedSources[$registration.sourceFile] = $sourcePath }
    }

    $sortedHits = Sort-Ordinal -Items $branchHits.ToArray() -KeySelector {
        param($item) $item.sourceFile + [char]0 + $item.sourceLine.ToString('D10', [Globalization.CultureInfo]::InvariantCulture) + [char]0 + $item.markerId
    }
    $sourceIdentities = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $hashedSources.GetEnumerator()) {
        $bytes = [IO.File]::ReadAllBytes([string]$entry.Value)
        $sourceIdentities.Add([pscustomobject][ordered]@{ sourceFile = [string]$entry.Key; sha256 = Get-Sha256Hex -Bytes $bytes; bytes = $bytes.LongLength })
    }
    $sortedSources = Sort-Ordinal -Items $sourceIdentities.ToArray() -KeySelector { param($item) $item.sourceFile }

    $instructionKeys = @{}; foreach ($item in $instructionRegistrations) { $instructionKeys[$item.publicKey] = $item }
    $collisions = New-Object System.Collections.Generic.List[object]
    foreach ($item in $expressionRegistrations) {
        if ($instructionKeys.ContainsKey($item.publicKey)) {
            $collisions.Add([pscustomobject][ordered]@{
                publicKey = $item.publicKey
                instructionHandler = $instructionKeys[$item.publicKey].handler
                expressionHandler = $item.handler
                currentResolution = 'instruction-precedence-via-ContainsKey'
            })
        }
    }
    $sortedCollisions = Sort-Ordinal -Items $collisions.ToArray() -KeySelector { param($item) $item.publicKey }
    $unresolvedInstructionCount = @($instructionRegistrations | Where-Object targetModule -match 'unresolved|candidate').Count
    $unresolvedExpressionCount = @($expressionRegistrations | Where-Object targetModule -match 'unresolved|candidate').Count

    $canonicalPayload = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-01'
        sourceFiles = @($sortedSources)
        branchHits = @($sortedHits)
        instructionRegistrations = @($instructionRegistrations)
        expressionRegistrations = @($expressionRegistrations)
        crossRegistryCollisions = @($sortedCollisions)
    }
    $canonicalJson = $canonicalPayload | ConvertTo-Json -Depth 20 -Compress
    $canonicalHash = Get-Sha256Hex -Bytes $script:Utf8NoBom.GetBytes($canonicalJson)

    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-01'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        # Kept out of the canonical payload: it is execution context used only
        # to verify the local legacy lookup binding when generating DIA-02.
        projectRoot = $resolvedRoot
        canonicalHash = $canonicalHash
        branchHitCount = @($sortedHits).Count
        unmappedHitCount = 0
        instructionRegistrationCount = $instructionRegistrations.Count
        expressionRegistrationCount = $expressionRegistrations.Count
        crossRegistryCollisionCount = @($sortedCollisions).Count
        unresolvedInstructionCount = $unresolvedInstructionCount
        unresolvedExpressionCount = $unresolvedExpressionCount
        uncovered = @(
            'Static inventory does not prove argument binding, return values, errors, waits, display effects, or timing.',
            'Common instruction and expression registrations still require upstream/v24/Snake module attribution.',
            'D1 session plan and D2 runtime frozen registry switch are not implemented; M0-DIA-02 only covers a test projection invariant.',
            'No upstream/APK/device/sign-off evidence is produced by this tool.'
        )
        sourceFiles = @($sortedSources)
        branchHits = @($sortedHits)
        instructionRegistrations = @($instructionRegistrations)
        expressionRegistrations = @($expressionRegistrations)
        crossRegistryCollisions = @($sortedCollisions)
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
        [IO.File]::WriteAllText($resolvedOutput, (($report | ConvertTo-Json -Depth 20) + "`n"), $script:Utf8NoBom)
    }
    return [pscustomobject]$report
}

function Resolve-DialectSnapshotModuleId {
    param([Parameter(Mandatory = $true)][string]$TargetModule)

    switch ($TargetModule) {
        'legacy.common.unresolved' { return 'legacy.current.common' }
        'legacy.expression.unresolved' { return 'legacy.current.expression' }
        'game.snake.candidate' { return 'game.snake' }
        default { return $TargetModule }
    }
}

function Get-DialectOwnershipStatus {
    param([Parameter(Mandatory = $true)][string]$TargetModule)

    if ($TargetModule.EndsWith('.unresolved', [StringComparison]::Ordinal)) { return 'Unresolved' }
    if ($TargetModule.EndsWith('.candidate', [StringComparison]::Ordinal)) { return 'Candidate' }
    return 'Mapped'
}

function New-DialectRegistrySnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Inventory,
        [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ProfileId,
        [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string[]]$SelectedModuleIds,
        [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string[]]$AvailableModuleIds
    )

    if ([string]::IsNullOrWhiteSpace([string]$Inventory.canonicalHash) -or [string]$Inventory.canonicalHash -notmatch '^[0-9a-f]{64}$') {
        throw 'Registry snapshot requires a valid source inventory canonical hash.'
    }

    $selectedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($moduleId in $SelectedModuleIds) {
        if ([string]::IsNullOrWhiteSpace($moduleId)) { throw 'Selected module id must not be empty.' }
        if (-not $selectedSet.Add($moduleId)) { throw "Duplicate selected module id: $moduleId" }
    }
    $availableSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($moduleId in $AvailableModuleIds) {
        if ([string]::IsNullOrWhiteSpace($moduleId)) { throw 'Available module id must not be empty.' }
        [void]$availableSet.Add($moduleId)
    }
    foreach ($moduleId in $selectedSet) {
        if (-not $availableSet.Contains($moduleId)) { throw "Selected module is not available: $moduleId" }
    }

    $instructions = New-Object System.Collections.Generic.List[object]
    foreach ($registration in @($Inventory.instructionRegistrations)) {
        $moduleId = Resolve-DialectSnapshotModuleId -TargetModule ([string]$registration.targetModule)
        if (-not $selectedSet.Contains($moduleId)) { continue }
        $instructions.Add([pscustomobject][ordered]@{
            publicKey = [string]$registration.publicKey
            moduleId = $moduleId
            ownershipStatus = Get-DialectOwnershipStatus -TargetModule ([string]$registration.targetModule)
            handler = [string]$registration.handler
            currentGuard = [string]$registration.currentGuard
            currentContribution = [string]$registration.currentContribution
            sourceTargetModule = [string]$registration.targetModule
            provenanceWarning = [string]$registration.provenanceWarning
        })
    }

    $expressionFunctions = New-Object System.Collections.Generic.List[object]
    foreach ($registration in @($Inventory.expressionRegistrations)) {
        $moduleId = Resolve-DialectSnapshotModuleId -TargetModule ([string]$registration.targetModule)
        if (-not $selectedSet.Contains($moduleId)) { continue }
        $expressionFunctions.Add([pscustomobject][ordered]@{
            publicKey = [string]$registration.publicKey
            moduleId = $moduleId
            ownershipStatus = Get-DialectOwnershipStatus -TargetModule ([string]$registration.targetModule)
            handler = [string]$registration.handler
            currentGuard = [string]$registration.currentGuard
            currentContribution = [string]$registration.currentContribution
            sourceTargetModule = [string]$registration.targetModule
            provenanceWarning = [string]$registration.provenanceWarning
        })
    }

    $instructionSeen = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($entry in $instructions) {
        if ($instructionSeen.ContainsKey($entry.publicKey)) {
            throw "Duplicate selected instruction key: $($entry.publicKey) modules=$($instructionSeen[$entry.publicKey].moduleId),$($entry.moduleId)"
        }
        $instructionSeen.Add($entry.publicKey, $entry)
    }
    $functionSeen = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($entry in $expressionFunctions) {
        if ($functionSeen.ContainsKey($entry.publicKey)) {
            throw "Duplicate selected expression function key: $($entry.publicKey) modules=$($functionSeen[$entry.publicKey].moduleId),$($entry.moduleId)"
        }
        $functionSeen.Add($entry.publicKey, $entry)
    }

    $sortedSelected = Sort-Ordinal -Items @($selectedSet) -KeySelector { param($item) [string]$item }
    $sortedAvailable = Sort-Ordinal -Items @($availableSet) -KeySelector { param($item) [string]$item }
    $sortedInstructions = Sort-Ordinal -Items $instructions.ToArray() -KeySelector { param($item) $item.publicKey + [char]0 + $item.moduleId }
    $sortedFunctions = Sort-Ordinal -Items $expressionFunctions.ToArray() -KeySelector { param($item) $item.publicKey + [char]0 + $item.moduleId }

    $canonicalInstructions = @($sortedInstructions | ForEach-Object {
        [ordered]@{
            publicKey = $_.publicKey
            moduleId = $_.moduleId
            ownershipStatus = $_.ownershipStatus
            handler = $_.handler
            currentGuard = $_.currentGuard
            currentContribution = $_.currentContribution
            sourceTargetModule = $_.sourceTargetModule
        }
    })
    $canonicalFunctions = @($sortedFunctions | ForEach-Object {
        [ordered]@{
            publicKey = $_.publicKey
            moduleId = $_.moduleId
            ownershipStatus = $_.ownershipStatus
            handler = $_.handler
            currentGuard = $_.currentGuard
            currentContribution = $_.currentContribution
            sourceTargetModule = $_.sourceTargetModule
        }
    })
    $canonicalPayload = [ordered]@{
        schemaVersion = '1.0.0'
        profileId = $ProfileId
        sourceInventoryHash = [string]$Inventory.canonicalHash
        nameComparison = 'Ordinal (test projection; effective legacy comparer remains Uncovered)'
        selectedModuleIds = @($sortedSelected)
        instructions = $canonicalInstructions
        expressionFunctions = $canonicalFunctions
    }
    $canonicalJson = $canonicalPayload | ConvertTo-Json -Depth 20 -Compress
    $canonicalHash = Get-Sha256Hex -Bytes $script:Utf8NoBom.GetBytes($canonicalJson)

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0.0'
        profileId = $ProfileId
        sourceInventoryHash = [string]$Inventory.canonicalHash
        canonicalHash = $canonicalHash
        nameComparison = 'Ordinal (test projection; effective legacy comparer remains Uncovered)'
        selectedModuleIds = @($sortedSelected)
        availableModuleIds = @($sortedAvailable)
        instructionCount = @($sortedInstructions).Count
        expressionFunctionCount = @($sortedFunctions).Count
        unresolvedInstructionCount = @($sortedInstructions | Where-Object ownershipStatus -eq 'Unresolved').Count
        candidateInstructionCount = @($sortedInstructions | Where-Object ownershipStatus -eq 'Candidate').Count
        unresolvedExpressionFunctionCount = @($sortedFunctions | Where-Object ownershipStatus -eq 'Unresolved').Count
        candidateExpressionFunctionCount = @($sortedFunctions | Where-Object ownershipStatus -eq 'Candidate').Count
        instructions = @($sortedInstructions)
        expressionFunctions = @($sortedFunctions)
    }
}

function Get-LegacyProfileSurfaceNames {
    # 注意：必须是普通函数（不能加 [CmdletBinding()]）。高级函数在
    # Import-Module + & $mod { } 嵌套调用时，函数体内变量赋值后引用会
    # 被误判"未定义"（PowerShell 作用域隔离问题，erafl 清单修复时踩坑实证）。
    param(
        [string]$ProjectRoot,
        [string]$FieldName,
        [string]$ClassName = 'LegacySnakeCompatibilityModule'
    )

    $relativePath = 'Scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs'
    $path = Join-Path $ProjectRoot ($relativePath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Legacy compatibility surface source is missing: $relativePath"
    }

    $lines = [IO.File]::ReadAllLines([IO.Path]::GetFullPath($path), [Text.Encoding]::UTF8)
    $classBlock = Get-CSharpBlock -Lines $lines `
        -DeclarationRegex ('^\s*internal\s+sealed\s+class\s+' + [regex]::Escape($ClassName) + '\b') `
        -Label $ClassName
    $fieldBlock = Get-CSharpBlock -Lines $classBlock.lines `
        -DeclarationRegex ('^\s*private\s+static\s+readonly\s+IReadOnlyCollection<string>\s+' + [regex]::Escape($FieldName) + '\s*=') `
        -Label "$ClassName.$FieldName"
    $text = $fieldBlock.lines -join "`n"
    # 注意：不能用 $matches 承接正则结果——它是 PowerShell 只读自动变量，
    # 赋值静默失败导致函数误报空字段（erafl 清单修复时踩坑实证）。
    # 另：在 [CmdletBinding()] 函数 + Set-StrictMode 下，[regex]::Matches 返回
    # MatchCollection 赋给局部变量后，后续引用会被误判"未定义"（PowerShell 7 行为），
    # 因此改用 [System.Text.RegularExpressions.Regex] 完全限定名并立即转数组取值。
    $regex = [System.Text.RegularExpressions.Regex]::new('"(?<name>(?:\\.|[^"\\])*)"', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $nameMatchList = @($regex.Matches($text))
    if ($nameMatchList.Count -eq 0) {
        throw "Legacy profile field has no public keys: $FieldName"
    }

    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($match in $nameMatchList) {
        $name = $match.Groups['name'].Value
        if (-not $names.Add($name)) {
            throw "Duplicate public key in ${ClassName}.${FieldName}: $name"
        }
    }
    # 强制扁平数组返回：单元素集合会被 PowerShell unroll 成标量，而 ,@() 会引入
    # 嵌套数组——两者都会破坏调用方的 -notin/-contains 语义。正确做法是返回扁平
    # 数组，由调用方用 @(...) 包一层确保数组身份。
    $sorted = @($names | Sort-Object)
    return $sorted
}

function New-LegacyRuntimeSurfaceSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Inventory,
        [Parameter(Mandatory = $true)][string]$ProfileId,
        [string[]]$ExcludedInstructionNames = @(),
        [string[]]$ExcludedFunctionNames = @()
    )

    $excludedInstructions = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($name in $ExcludedInstructionNames) { [void]$excludedInstructions.Add($name) }
    $excludedFunctions = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($name in $ExcludedFunctionNames) { [void]$excludedFunctions.Add($name) }

    $knownInstructions = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($registration in @($Inventory.instructionRegistrations)) { [void]$knownInstructions.Add([string]$registration.publicKey) }
    foreach ($name in $excludedInstructions) {
        if (-not $knownInstructions.Contains($name)) { throw "Profile '$ProfileId' excludes an unknown instruction: $name" }
    }
    $knownFunctions = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($registration in @($Inventory.expressionRegistrations)) { [void]$knownFunctions.Add([string]$registration.publicKey) }
    foreach ($name in $excludedFunctions) {
        if (-not $knownFunctions.Contains($name)) { throw "Profile '$ProfileId' excludes an unknown expression function: $name" }
    }

    $instructions = @($Inventory.instructionRegistrations | Where-Object { -not $excludedInstructions.Contains([string]$_.publicKey) })
    $functions = @($Inventory.expressionRegistrations | Where-Object { -not $excludedFunctions.Contains([string]$_.publicKey) })
    $instructions = Sort-Ordinal -Items $instructions -KeySelector { param($item) [string]$item.publicKey }
    $functions = Sort-Ordinal -Items $functions -KeySelector { param($item) [string]$item.publicKey }

    $payload = [ordered]@{
        profileId = $ProfileId
        sourceInventoryHash = [string]$Inventory.canonicalHash
        excludedInstructions = @($ExcludedInstructionNames | Sort-Object)
        excludedFunctions = @($ExcludedFunctionNames | Sort-Object)
        instructions = @($instructions | ForEach-Object { [string]$_.publicKey })
        expressionFunctions = @($functions | ForEach-Object { [string]$_.publicKey })
    }
    $canonicalJson = $payload | ConvertTo-Json -Depth 20 -Compress

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0.0'
        profileId = $ProfileId
        sourceInventoryHash = [string]$Inventory.canonicalHash
        canonicalHash = Get-Sha256Hex -Bytes $script:Utf8NoBom.GetBytes($canonicalJson)
        nameComparison = 'Ordinal profile surface (legacy comparer remains a separate compatibility contract)'
        instructionCount = @($instructions).Count
        expressionFunctionCount = @($functions).Count
        instructions = @($instructions)
        expressionFunctions = @($functions)
    }
}

function Test-LegacyRuntimeSurfaceBinding {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $functionText = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs'), [Text.Encoding]::UTF8)
    $methodText = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'Scripts\Emuera\GameData\Function\Creator.cs'), [Text.Encoding]::UTF8)
    $identifierText = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'Scripts\Emuera\GameData\IdentifierDictionary.cs'), [Text.Encoding]::UTF8)

    $instructionSurface = $functionText -match 'GetInstructionNameDic\s*\(\s*LegacyCompatibilityProfile\s+compatibility\s*\)' -and
        $functionText -match 'compatibility\.IsInstructionVisible\s*\(\s*pair\.Key\s*\)'
    $functionSurface = $methodText -match 'GetMethodList\s*\(\s*LegacyCompatibilityProfile\s+compatibility\s*\)' -and
        $methodText -match 'compatibility\.IsFunctionVisible\s*\(\s*pair\.Key\s*\)'
    $parserBinding = $identifierText -match 'instructionDic\s*=\s*FunctionIdentifier\.GetInstructionNameDic\s*\(\s*compatibility\s*\)' -and
        $identifierText -match 'methodDic\s*=\s*FunctionMethodCreator\.GetMethodList\s*\(\s*compatibility\s*\)' -and
        $identifierText -match 'var\s+compatibility\s*=\s*Program\.Compatibility\s*;'
    $lookupBinding = $identifierText -match 'compatibilityInstructionDic\s*\?\?\s*instructionDic' -and
        $identifierText -match 'compatibilityMethodDic\s*\?\?\s*methodDic'

    return [pscustomobject][ordered]@{
        status = if ($instructionSurface -and $functionSurface -and $parserBinding -and $lookupBinding) { 'Passed' } else { 'Failed' }
        instructionSurfaceBound = $instructionSurface
        functionSurfaceBound = $functionSurface
        parserBindingPresent = $parserBinding
        lookupBindingPresent = $lookupBinding
        assertion = 'Legacy parser and expression lookup tables are built from the immutable LegacyCompatibilityProfile surface before parsing begins.'
    }
}

function New-DialectRegistrySnapshotReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Inventory,
        [string]$OutputPath = ''
    )

    if (-not $Inventory.PSObject.Properties['projectRoot']) {
        throw 'Dialect inventory does not carry its project root for legacy profile surface verification.'
    }
    $projectRoot = [string]$Inventory.projectRoot
    $portOnlyInstructionNames = @(Get-LegacyProfileSurfaceNames -ProjectRoot $projectRoot -ClassName 'LegacyV24CompatibilityModule' -FieldName 'PortOnlyInstructionNames')
    $snakeInstructionNames = @(Get-LegacyProfileSurfaceNames -ProjectRoot $projectRoot -FieldName 'InstructionNames')
    $v24ExcludedFunctionNames = @(Get-LegacyProfileSurfaceNames -ProjectRoot $projectRoot -FieldName 'FunctionNames')
    $snakeExcludedFunctionNames = @(Get-LegacyProfileSurfaceNames -ProjectRoot $projectRoot -FieldName 'SnakeExcludedFunctionNames')
    # eraFL 自持能力清单：erafl 会话在 v24 排除集基础上，仅解除自己声明的指令（SETANIMETIMER）。
    $eraflRaw = Get-LegacyProfileSurfaceNames -ProjectRoot $projectRoot -ClassName 'LegacyEraFlCompatibilityModule' -FieldName 'InstructionNames'
    $eraflInstructionNames = @($eraflRaw)
    $v24ExcludedInstructionNames = @($portOnlyInstructionNames + $snakeInstructionNames)
    $v24 = New-LegacyRuntimeSurfaceSnapshot -Inventory $Inventory -ProfileId 'v24pure' `
        -ExcludedInstructionNames $v24ExcludedInstructionNames -ExcludedFunctionNames $v24ExcludedFunctionNames
    $snake = New-LegacyRuntimeSurfaceSnapshot -Inventory $Inventory -ProfileId 'snake' `
        -ExcludedInstructionNames $portOnlyInstructionNames -ExcludedFunctionNames $snakeExcludedFunctionNames
    $eraflExcludedInstructionNames = @($v24ExcludedInstructionNames | Where-Object { $eraflInstructionNames -notcontains $_ })
    $erafl = New-LegacyRuntimeSurfaceSnapshot -Inventory $Inventory -ProfileId 'erafl' `
        -ExcludedInstructionNames $eraflExcludedInstructionNames -ExcludedFunctionNames $v24ExcludedFunctionNames

    $v24InstructionKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in $v24.instructions) { [void]$v24InstructionKeys.Add($entry.publicKey) }
    $snakeOnlyInstructions = @($snake.instructions | Where-Object { -not $v24InstructionKeys.Contains($_.publicKey) })
    $snakeOnlyInstructions = Sort-Ordinal -Items $snakeOnlyInstructions -KeySelector { param($item) $item.publicKey }
    $v24FunctionKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in $v24.expressionFunctions) { [void]$v24FunctionKeys.Add($entry.publicKey) }
    $snakeOnlyFunctions = @($snake.expressionFunctions | Where-Object { -not $v24FunctionKeys.Contains($_.publicKey) })
    $snakeOnlyFunctions = Sort-Ordinal -Items $snakeOnlyFunctions -KeySelector { param($item) $item.publicKey }

    $runtimeBinding = Test-LegacyRuntimeSurfaceBinding -ProjectRoot $projectRoot
    $runtimeStatus = [string]$runtimeBinding.status
    $projectionStatus = if ($runtimeStatus -eq 'Passed') { 'Passed' } else { 'Failed' }

    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-02'
        sourceInventoryHash = [string]$Inventory.canonicalHash
        v24Hash = $v24.canonicalHash
        snakeHash = $snake.canonicalHash
        eraflHash = $erafl.canonicalHash
        snakeOnlyInstructionKeys = @($snakeOnlyInstructions.publicKey)
        snakeOnlyExpressionFunctionKeys = @($snakeOnlyFunctions.publicKey)
        testProjectionInvariantStatus = $projectionStatus
        currentRuntimeIsolationStatus = $runtimeStatus
    }
    $setJson = $setPayload | ConvertTo-Json -Depth 20 -Compress
    $snapshotSetHash = Get-Sha256Hex -Bytes $script:Utf8NoBom.GetBytes($setJson)

    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-02'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceInventoryHash = [string]$Inventory.canonicalHash
        snapshotSetHash = $snapshotSetHash
        testProjectionInvariant = [ordered]@{
            status = $projectionStatus
            v24ProfileHash = $v24.canonicalHash
            assertion = 'The v24 surface excludes the exact Snake public-key delta declared by the legacy profile boundary.'
        }
        currentRuntimeIsolation = [ordered]@{
            status = $runtimeStatus
            instructionSurfaceBound = $runtimeBinding.instructionSurfaceBound
            functionSurfaceBound = $runtimeBinding.functionSurfaceBound
            parserBindingPresent = $runtimeBinding.parserBindingPresent
            lookupBindingPresent = $runtimeBinding.lookupBindingPresent
            assertion = $runtimeBinding.assertion
        }
        profiles = [ordered]@{
            v24 = $v24
            snake = $snake
            erafl = $erafl
        }
        diff = [ordered]@{
            snakeOnlyInstructionCount = @($snakeOnlyInstructions).Count
            snakeOnlyExpressionFunctionCount = @($snakeOnlyFunctions).Count
            snakeOnlyInstructions = @($snakeOnlyInstructions)
            snakeOnlyExpressionFunctions = @($snakeOnlyFunctions)
        }
        uncovered = @(
            'The report verifies parser-visible legacy surfaces, not instruction/function behavior, errors, waits, rendering, or timing.',
            'The legacy handler store remains static; profile isolation is enforced by immutable lookup surfaces, not per-profile handler allocation.',
            'Effective name comparer, aliases, replacements, signatures, completion modes, typed policies, and behavior fixtures remain Uncovered.',
            'D1 session ownership, D2 runtime frozen registry switch, upstream/target/APK/device evidence, and gate signatures are not implemented.'
        )
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
        [IO.File]::WriteAllText($resolvedOutput, (($report | ConvertTo-Json -Depth 30) + "`n"), $script:Utf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectInventory, New-DialectRegistrySnapshot, New-DialectRegistrySnapshotReport
