[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-InProcessCycleContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $configPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerConfig.cs'
    $hostPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerHost.cs'
    $mainPath = Join-Path $ProjectRoot 'Scripts\EmueraMain.cs'
    $gpuComponentPath = Join-Path $ProjectRoot 'Scripts\GodotHost\EmueraGpuRenderComponent.cs'
    $textComponentPath = Join-Path $ProjectRoot 'Scripts\GodotHost\EmueraTextRenderComponent.cs'
    $backendPath = Join-Path $ProjectRoot 'Scripts\GodotHost\LegacySessionBackend.cs'
    $contentPath = Join-Path $ProjectRoot 'Scripts\EmueraContent.cs'
    $spriteManagerPath = Join-Path $ProjectRoot 'Scripts\SpriteManager.cs'
    $colorMatrixGpuPath = Join-Path $ProjectRoot 'Scripts\ColorMatrixGPU.cs'
    $launchRegistryPath = Join-Path $ProjectRoot 'Scripts\GodotHost\LegacySessionLaunchRegistry.cs'
    $threadPath = Join-Path $ProjectRoot 'Scripts\EmueraThread.cs'
    $quiescencePath = Join-Path $ProjectRoot 'Scripts\GodotHost\LegacyThreadQuiescence.cs'
    $programPath = Join-Path $ProjectRoot 'Scripts\Emuera\Program.cs'
    $globalStaticPath = Join-Path $ProjectRoot 'Scripts\Emuera\GlobalStatic.cs'
    $ctrlZPath = Join-Path $ProjectRoot 'Scripts\Emuera\CtrlZ.cs'
    $utilsPath = Join-Path $ProjectRoot 'Scripts\uEmuera\Utils.cs'
    $genericUtilsPath = Join-Path $ProjectRoot 'Scripts\GenericUtils.cs'
    $parserMediatorPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameData\ParserMediator.cs'
    $identifierDictionaryPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameData\IdentifierDictionary.cs'
    $processPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\Process.cs'
    $keyMacroPath = Join-Path $ProjectRoot 'Scripts\Emuera\Config\KeyMacro.cs'
    $functionCreatorMethodPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameData\Function\Creator.Method.cs'
    $configPathSource = Join-Path $ProjectRoot 'Scripts\Emuera\Config\Config.cs'
    $lazyLoadingPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\Process.LazyLoading.cs'
    $formsPath = Join-Path $ProjectRoot 'Scripts\uEmuera\Forms.cs'
    $winInputPath = Join-Path $ProjectRoot 'Scripts\Emuera\_Library\WinInput.cs'
    $saveTrailPath = Join-Path $ProjectRoot 'Scripts\Diagnostics\SaveLogOperationTrail.cs'
    $consolePrintPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameView\EmueraConsole.Print.cs'
    $runnerPath = Join-Path $ProjectRoot 'tools\legacy-runner\Invoke-LegacyRunner.ps1'
    $schemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-runner-config.schema.json'
    $fixturePath = Join-Path $ProjectRoot 'tools\legacy-runner\fixtures\session-isolation-inprocess-aba.json'
    $crossFixturePath = Join-Path $ProjectRoot 'tools\legacy-runner\fixtures\session-isolation-inprocess-cross-aba.json'
    $comparisonPath = Join-Path $ProjectRoot 'tools\legacy-runner\Invoke-SessionIsolationInProcessAba.ps1'
    $crossComparisonPath = Join-Path $ProjectRoot 'tools\legacy-runner\Invoke-SessionIsolationInProcessCrossAba.ps1'
    foreach ($path in @($configPath, $hostPath, $mainPath, $gpuComponentPath, $textComponentPath, $backendPath, $contentPath, $spriteManagerPath, $colorMatrixGpuPath, $launchRegistryPath, $threadPath, $quiescencePath, $programPath, $globalStaticPath, $ctrlZPath, $utilsPath, $genericUtilsPath, $parserMediatorPath, $identifierDictionaryPath, $processPath, $keyMacroPath, $functionCreatorMethodPath, $configPathSource, $lazyLoadingPath, $formsPath, $winInputPath, $saveTrailPath, $consolePrintPath, $runnerPath, $schemaPath, $fixturePath, $crossFixturePath, $comparisonPath, $crossComparisonPath)) {
        Assert-InProcessCycleContract (Test-Path -LiteralPath $path -PathType Leaf) "In-process session-cycle contract file is missing: $path"
    }

    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    $cycleProperty = $schema.properties.inProcessSessionCycle
    Assert-InProcessCycleContract ($null -ne $cycleProperty) 'Runner schema does not expose inProcessSessionCycle.'
    Assert-InProcessCycleContract (@($cycleProperty.enum).Count -eq 3) 'In-process session-cycle schema must have exactly three values.'
    foreach ($mode in @('disabled', 'aba', 'cross-aba')) {
        Assert-InProcessCycleContract (@($cycleProperty.enum) -contains $mode) "In-process session-cycle schema is missing '$mode'."
    }
    $switchCountProperty = $schema.properties.inProcessSessionSwitchCount
    Assert-InProcessCycleContract ($null -ne $switchCountProperty) 'Runner schema does not expose inProcessSessionSwitchCount.'
    Assert-InProcessCycleContract ($switchCountProperty.minimum -eq 2 -and $switchCountProperty.maximum -eq 100) 'In-process session switch-count schema bounds changed.'
    Assert-InProcessCycleContract ($null -ne $schema.properties.inProcessAlternateSession) 'Runner schema does not expose the cross-ABA alternate session.'

    $configSource = [IO.File]::ReadAllText($configPath)
    Assert-InProcessCycleContract ($configSource.Contains('InProcessSessionCycle')) 'Runner config does not model the in-process session cycle.'
    Assert-InProcessCycleContract ($configSource.Contains('InProcessSessionSwitchCount')) 'Runner config does not model the in-process session switch count.'
    Assert-InProcessCycleContract ($configSource.Contains('InProcessSessionCycleCrossAba')) 'Runner config does not model cross-ABA.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_session_cycle_must_be_disabled_aba_or_cross_aba')) 'Runner config does not fail fast for an unknown in-process session cycle.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_session_cycle_requires_canary')) 'In-process session cycle does not require the canary path.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_session_cycle_does_not_support_inputs')) 'In-process session cycle does not reject input replay.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_session_switch_count_out_of_range')) 'Runner config does not validate the in-process switch count.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_cross_aba_switch_count_must_be_even')) 'Cross-ABA does not reject an odd switch count.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_session_switch_count_requires_cycle')) 'Runner config allows a stress switch count without a runner cycle.'
    Assert-InProcessCycleContract ($configSource.Contains('in_process_cross_aba_requires_alternate_session')) 'Cross-ABA does not require an alternate session.'
    Assert-InProcessCycleContract ($configSource.Contains('CreateLegacyRunnerSessionLaunchRegistry')) 'Runner config does not build an immutable launch registry.'

    $hostSource = [IO.File]::ReadAllText($hostPath)
    Assert-InProcessCycleContract ($hostSource.Contains('TickInProcessSessionCycle')) 'Runner host does not implement the in-process session-cycle state machine.'
    Assert-InProcessCycleContract ($hostSource.Contains('CreateInProcessSessionCycleTargets')) 'Runner host does not precompute the runner-only session cycle targets.'
    Assert-InProcessCycleContract ($hostSource.Contains('SwitchLegacySessionForLegacyRunnerAsync')) 'Runner host does not call the cross-configuration session boundary.'
    Assert-InProcessCycleContract ($hostSource.Contains('in_process_session_wait_fingerprint')) 'Runner host does not record per-cycle semantic fingerprints.'
    Assert-InProcessCycleContract ($hostSource.Contains('in_process_session_cycle_targets_must_match_switch_count')) 'Runner host does not validate the generated stress cycle length.'
    Assert-InProcessCycleContract ($hostSource.Contains('AreInProcessCycleFingerprintsStable')) 'Runner host does not validate all repeated session fingerprints.'
    Assert-InProcessCycleContract ($hostSource.Contains('WriteInProcessSessionCycleEvidence')) 'Runner host does not emit lifecycle evidence for the switch cycle.'
    Assert-InProcessCycleContract ($hostSource.Contains('in_process_aba_cycle_completed')) 'Runner host does not expose a completed A-to-B-to-A result.'
    Assert-InProcessCycleContract ($hostSource.Contains('in_process_cross_aba_cycle_completed')) 'Runner host does not expose a completed cross-configuration A-to-B-to-A result.'

    $mainSource = [IO.File]::ReadAllText($mainPath)
    Assert-InProcessCycleContract ($mainSource.Contains('RestartLegacySessionForLegacyRunnerAsync')) 'EmueraMain does not expose the runner-only restart boundary.'
    Assert-InProcessCycleContract ($mainSource.Contains('ConfigureLegacyRunnerSessionLaunchRegistry')) 'EmueraMain does not receive runner routes before startup.'
    Assert-InProcessCycleContract ($mainSource.Contains('SwitchLegacySessionForLegacyRunnerAsync')) 'EmueraMain does not expose the cross-configuration switch boundary.'
    Assert-InProcessCycleContract ($mainSource.Contains('legacySessionFacade.SwitchAsync')) 'Runner-only restart does not use LegacySessionFacade.'
    Assert-InProcessCycleContract ($mainSource.Contains('if (!facade.IsBackendRunning)')) 'Runner-only restart does not use the facade-owned backend state.'
    Assert-InProcessCycleContract ($mainSource.Contains('Console.IsInProcess, which is false while')) 'Runner-only restart does not document the input-wait lifecycle distinction.'
    Assert-InProcessCycleContract ($mainSource.Contains('ResetCanarySessionState')) 'EmueraMain has no canary render-queue reset boundary.'
    $gpuComponentSource = [IO.File]::ReadAllText($gpuComponentPath)
    Assert-InProcessCycleContract ($gpuComponentSource.Contains('while (workQueue.TryDequeue(out var item))')) 'GPU component canary reset does not drain the stale GPU work queue.'
    Assert-InProcessCycleContract ($gpuComponentSource.Contains('ResetCanarySessionState')) 'GPU component has no canary queue reset boundary.'
    Assert-InProcessCycleContract ($gpuComponentSource.Contains('workQueue.TryDequeue')) 'GPU component reset does not drain stale work.'
    Assert-InProcessCycleContract ($gpuComponentSource.Contains('ResetPendingRenderState')) 'GPU component reset does not complete in-flight work.'
    $textComponentSource = [IO.File]::ReadAllText($textComponentPath)
    Assert-InProcessCycleContract ($textComponentSource.Contains('while (renderQueue.TryDequeue(out var item))')) 'Text component canary reset does not drain the stale text work queue.'
    Assert-InProcessCycleContract ($textComponentSource.Contains('slot.Item.Completed.Set();')) 'Text component canary reset does not release the in-flight render-slot waiters.'
    Assert-InProcessCycleContract ($textComponentSource.Contains('ResetCanarySessionState')) 'Text component has no canary queue reset boundary.'
    Assert-InProcessCycleContract ($textComponentSource.Contains('renderQueue.TryDequeue')) 'Text component reset does not drain stale work.'
    Assert-InProcessCycleContract ($textComponentSource.Contains('ResetPendingRenderState')) 'Text component reset does not complete in-flight work.'

    $programSource = [IO.File]::ReadAllText($programPath)
    Assert-InProcessCycleContract ($programSource.Contains('ConfigureLegacyRunnerStartupErrorLogPath')) 'Legacy Program does not expose a runner-only startup-error-log redirect.'
    Assert-InProcessCycleContract ($programSource.Contains('GetSnakeStartupErrorLogPath')) 'Snake startup diagnostics do not resolve the runner-owned log path.'
    Assert-InProcessCycleContract ($programSource.Contains('ConfigureLegacyRunnerDefaultOutputLogPath')) 'Legacy Program does not expose a runner-only default-output-log redirect.'
    Assert-InProcessCycleContract ($programSource.Contains('TryResolveLegacyRunnerDefaultOutputLogPath')) 'Legacy Program does not constrain the default-output-log redirect to the runner-owned path.'
    $consolePrintSource = [IO.File]::ReadAllText($consolePrintPath)
    Assert-InProcessCycleContract ($consolePrintSource.Contains('TryResolveLegacyRunnerDefaultOutputLogPath')) 'Console output logging does not consume the runner-owned default log redirect.'

    Assert-InProcessCycleContract ($hostSource.Contains('ConfigureLegacyRunnerStartupErrorLogPath')) 'Runner host does not configure the startup-error-log redirect before main.tscn is attached.'
    Assert-InProcessCycleContract ($hostSource.Contains('emuera_startup_errors.log')) 'Runner host does not preserve the startup-error-log artifact name.'
    Assert-InProcessCycleContract ($hostSource.Contains('ConfigureLegacyRunnerDefaultOutputLogPath')) 'Runner host does not configure the default-output-log redirect before main.tscn is attached.'
    Assert-InProcessCycleContract ($hostSource.Contains('"emuera.log"')) 'Runner host does not preserve the default-output-log artifact name.'

    $runnerSource = [IO.File]::ReadAllText($runnerPath)
    Assert-InProcessCycleContract ($runnerSource.Contains('$inProcessSessionCycle = [string]$baseConfig.inProcessSessionCycle')) 'Runner does not normalize the optional in-process session cycle.'
    Assert-InProcessCycleContract ($runnerSource.Contains('$inProcessSessionSwitchCount = [int]$baseConfig.inProcessSessionSwitchCount')) 'Runner does not normalize the optional in-process session switch count.'
    Assert-InProcessCycleContract ($runnerSource.Contains('inProcessSessionCycle = $inProcessSessionCycle')) 'Runner summary does not record the normalized in-process session cycle.'
    Assert-InProcessCycleContract ($runnerSource.Contains('inProcessSessionSwitchCount = $inProcessSessionSwitchCount')) 'Runner summary does not record the normalized in-process session switch count.'
    Assert-InProcessCycleContract ($runnerSource.Contains("'emuera_startup_errors.log'")) 'Runner does not collect the redirected startup-error-log artifact.'
    Assert-InProcessCycleContract ($runnerSource.Contains("'emuera.log'")) 'Runner does not collect the redirected default-output-log artifact.'
    Assert-InProcessCycleContract ($runnerSource.Contains('runtime-alternate-game')) 'Runner does not isolate the alternate cross-ABA fixture.'
    Assert-InProcessCycleContract ($runnerSource.Contains('alternate-fixture-mutations.json')) 'Runner does not emit alternate fixture mutation evidence.'

    $crossComparisonSource = [IO.File]::ReadAllText($crossComparisonPath)
    foreach ($metric in @('privateBytes', 'workingSetBytes', 'handleCount', 'threadCount')) {
        Assert-InProcessCycleContract ($crossComparisonSource.Contains("[long]`$sample.$metric") -or $crossComparisonSource.Contains("[int]`$sample.$metric")) "Cross-ABA comparison does not validate lifecycle metric '$metric'."
    }
    Assert-InProcessCycleContract ($crossComparisonSource.Contains('cross_aba_cycle_evidence_sample_invalid')) 'Cross-ABA comparison does not reject incomplete lifecycle samples.'

    $backendSource = [IO.File]::ReadAllText($backendPath)
    $threadSource = [IO.File]::ReadAllText($threadPath)
    $quiescenceSource = [IO.File]::ReadAllText($quiescencePath)
    Assert-InProcessCycleContract ($threadSource.Contains('LegacyThreadQuiescence.WaitForStopOrThrow')) 'EmueraThread does not require the old worker to quiesce before releasing its wait handle.'
    Assert-InProcessCycleContract ($threadSource.Contains('if (thread != null && thread.IsAlive)')) 'EmueraThread does not reject a new start while an old worker is still alive.'
    Assert-InProcessCycleContract ($threadSource.IndexOf('LegacyThreadQuiescence.WaitForStopOrThrow') -lt $threadSource.IndexOf('inputEvent?.Dispose()')) 'EmueraThread can dispose the input event before worker quiescence is confirmed.'
    Assert-InProcessCycleContract ($quiescenceSource.Contains('throw new TimeoutException')) 'Thread quiescence timeout does not fail fast.'
    Assert-InProcessCycleContract ($backendSource.Contains('EmueraThread.instance.IsSessionActive')) 'Godot backend does not use the thread lifecycle state for its running check.'
    Assert-InProcessCycleContract ($backendSource.Contains('launchRegistry.Resolve(selection)')) 'Godot backend does not resolve the Core selection through a host allowlist.'
    Assert-InProcessCycleContract ($backendSource.Contains('ApplyLaunchConfiguration')) 'Godot backend does not apply the resolved host launch binding.'
    Assert-InProcessCycleContract ($backendSource.Contains('ResetCanarySessionConfiguration')) 'Godot backend does not reset profile-sensitive legacy configuration before a canary start.'
    Assert-InProcessCycleContract ($backendSource.Contains('ConfigureCompatibilityPlan(compatibility)')) 'Godot backend does not bind the committed CompatibilityPlan before legacy startup.'
    Assert-InProcessCycleContract ($backendSource.Contains('ClearCompatibilityPlan(compatibility)')) 'Godot backend does not clear a plan after failed candidate startup.'
    $backendStartMethod = $backendSource.IndexOf('public async ValueTask StartAsync(')
    $backendStartMethodEnd = $backendSource.IndexOf('/// <summary>', $backendStartMethod + 1)
    Assert-InProcessCycleContract ($backendStartMethod -ge 0 -and $backendStartMethodEnd -gt $backendStartMethod) 'Godot backend StartAsync boundary could not be isolated for ordering checks.'
    $backendStartSource = $backendSource.Substring($backendStartMethod, $backendStartMethodEnd - $backendStartMethod)
    Assert-InProcessCycleContract ($backendStartSource.IndexOf('ConfigureCompatibilityPlan(compatibility)') -lt $backendStartSource.IndexOf('launchRegistry.Resolve(selection)')) 'Godot backend resolves a launch before binding its CompatibilityPlan.'
    Assert-InProcessCycleContract ($backendStartSource.IndexOf('launchRegistry.Resolve(selection)') -lt $backendStartSource.IndexOf('StartLegacyBaselineAsync')) 'Godot backend starts legacy execution before applying the resolved launch.'
    Assert-InProcessCycleContract ($backendStartSource.IndexOf('catch') -lt $backendStartSource.IndexOf('ClearCompatibilityPlan(compatibility)')) 'Godot backend does not clear the plan from the failed-start catch boundary.'
    Assert-InProcessCycleContract ($backendSource.Contains('ConfigData.Instance.Clear()')) 'Godot backend does not restore ConfigData defaults between canary sessions.'
    Assert-InProcessCycleContract ($backendSource.Contains('ParserMediator.ClearWarningList()')) 'Godot backend does not clear parser warnings before a canary session rebinds its console.'
    Assert-InProcessCycleContract ($backendSource.Contains('AppContents.UnloadContents()')) 'Godot backend does not release legacy static content before a canary session starts.'
    Assert-InProcessCycleContract ($backendSource.Contains('_canarySessionActive')) 'Godot backend does not retain the canary-only lifecycle state.'
    Assert-InProcessCycleContract ($backendSource.Contains('StopCanarySession')) 'Godot backend does not isolate canary stop cleanup from the M0 baseline stop path.'
    Assert-InProcessCycleContract ($backendSource.Contains('EnsureCanaryCleanupRunsOnGodotMainThread')) 'Godot backend does not enforce main-thread ownership for canary view cleanup.'
    $canaryStopSource = $backendSource.Substring($backendSource.IndexOf('StopCanarySession'))
    Assert-InProcessCycleContract ($canaryStopSource.Contains('EmueraThread.instance.End()')) 'Canary cleanup does not quiesce the legacy worker before releasing view resources.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('ClearForCanarySessionTransition')) 'Canary cleanup does not release the session-owned Godot view state.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('uEmuera.Utils.ResourceClear()')) 'Canary cleanup does not release legacy resource lookup state.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('SpriteManager.ForceClear()')) 'Canary cleanup does not release the session texture cache.'
    Assert-InProcessCycleContract ($canaryStopSource.IndexOf('EmueraThread.instance.End()') -lt $canaryStopSource.IndexOf('ClearForCanarySessionTransition')) 'Canary view cleanup can race the legacy worker.'
    Assert-InProcessCycleContract ($canaryStopSource.IndexOf('ClearForCanarySessionTransition') -lt $canaryStopSource.IndexOf('SpriteManager.ForceClear()')) 'Canary texture cleanup can run before view pins are released.'
    Assert-InProcessCycleContract ($canaryStopSource.IndexOf('SpriteManager.ForceClear()') -lt $canaryStopSource.IndexOf('GlobalStatic.Reset()')) 'Canary global reset can run before cached textures are released.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('GlobalStatic.ResetCanarySessionState()')) 'Canary cleanup does not reset the remaining GlobalStatic session root state.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('Program.ResetSessionState()')) 'Canary cleanup does not reset process-wide Program session state.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('EmueraMain.ResetCanarySessionState()')) 'Canary cleanup does not reset the legacy render queue.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('EmueraGpuRenderComponent.ResetCanarySessionState()')) 'Canary cleanup does not reset the GPU component queue.'
    Assert-InProcessCycleContract ($canaryStopSource.Contains('EmueraTextRenderComponent.ResetCanarySessionState()')) 'Canary cleanup does not reset the text component queue.'
    Assert-InProcessCycleContract ($canaryStopSource.IndexOf('SpriteManager.ForceClear()') -lt $canaryStopSource.IndexOf('EmueraMain.ResetCanarySessionState()')) 'Render queue cleanup must follow sprite/resource cleanup.'
    Assert-InProcessCycleContract ($canaryStopSource.IndexOf('SpriteManager.ForceClear()') -lt $canaryStopSource.IndexOf('ColorMatrixGPU.ResetCanarySessionState()')) 'ColorMatrix material cleanup must follow sprite/resource cleanup.'

    $programSource = [IO.File]::ReadAllText($programPath)
    Assert-InProcessCycleContract ($programSource.Contains('ResetSessionState')) 'Program does not expose an explicit canary session-state reset boundary.'
    $programResetStart = $programSource.IndexOf('internal static void ResetSessionState()')
    $programResetEnd = $programSource.IndexOf('internal static bool TryResolveLegacyRunnerDefaultOutputLogPath(', $programResetStart + 1)
    Assert-InProcessCycleContract ($programResetStart -ge 0 -and $programResetEnd -gt $programResetStart) 'Program session-state reset boundary could not be isolated for the runner-owned sink checks.'
    $programResetSource = $programSource.Substring($programResetStart, $programResetEnd - $programResetStart)
    Assert-InProcessCycleContract (-not $programResetSource.Contains('m0RunnerStartupErrorLogPath')) 'Program session reset must preserve the runner-owned startup-error sink.'
    Assert-InProcessCycleContract (-not $programResetSource.Contains('m0RunnerDefaultOutputLogPath')) 'Program session reset must preserve the runner-owned default-output sink.'
    $globalStaticSource = [IO.File]::ReadAllText($globalStaticPath)
    $ctrlZSource = [IO.File]::ReadAllText($ctrlZPath)
    Assert-InProcessCycleContract ($globalStaticSource.Contains('ResetCanarySessionState')) 'GlobalStatic does not expose the canary-only residual root reset boundary.'
    Assert-InProcessCycleContract ($globalStaticSource.Contains('ctrlZ.ResetSessionState()')) 'GlobalStatic does not reset the CtrlZ session root.'
    Assert-InProcessCycleContract ($globalStaticSource.Contains('#if UEMUERA_DEBUG') -and $globalStaticSource.Contains('StackList.Clear()')) 'GlobalStatic does not reset the conditional debug StackList root.'
    Assert-InProcessCycleContract ($ctrlZSource.Contains('internal void ResetSessionState()')) 'CtrlZ does not expose a session reset operation.'
    Assert-InProcessCycleContract ($ctrlZSource.Contains('mInputs.Clear()')) 'CtrlZ reset does not clear the recorded input history.'
    Assert-InProcessCycleContract ($ctrlZSource.Contains('Array.Clear(mRandomSeed')) 'CtrlZ reset does not clear the random seed snapshot.'
    Assert-InProcessCycleContract ($globalStaticSource.Contains('KeyMacro.ResetCanarySessionState()')) 'GlobalStatic does not reset session macro definitions.'
    Assert-InProcessCycleContract ($globalStaticSource.Contains('FunctionMethodCreator.ResetCanarySessionState()')) 'GlobalStatic does not reset function key-toggle state.'
    Assert-InProcessCycleContract ($globalStaticSource.Contains('Config.ResetCanarySessionState()')) 'GlobalStatic does not reset legacy config and font cache resources.'

    $utilsSource = [IO.File]::ReadAllText($utilsPath)
    Assert-InProcessCycleContract ($utilsSource.Contains('ResetCanarySessionState')) 'Legacy Utils does not expose the path/resource cache reset boundary.'
    Assert-InProcessCycleContract ($utilsSource.Contains('recursiveFileIndexCache.Clear()')) 'Legacy Utils reset does not clear the recursive file index.'
    Assert-InProcessCycleContract ($utilsSource.Contains('Preload.Clear()')) 'Legacy Utils reset does not clear preloaded file lines.'
    Assert-InProcessCycleContract (-not $utilsSource.Contains('shiftjis_to_utf8 = null')) 'Process-scoped encoding maps must survive a canary switch.'

    $genericUtilsSource = [IO.File]::ReadAllText($genericUtilsPath)
    Assert-InProcessCycleContract ($genericUtilsSource.Contains('ResetCanarySessionState')) 'GenericUtils does not expose the bridge session reset boundary.'
    Assert-InProcessCycleContract ($genericUtilsSource.Contains('soundFallbackResolveCache.Clear()')) 'GenericUtils reset does not clear sound fallback paths.'
    Assert-InProcessCycleContract ($genericUtilsSource.Contains('while (uiQueueCount > 0)')) 'GenericUtils reset does not drain the stale UI ring buffer.'
    Assert-InProcessCycleContract ($genericUtilsSource.Contains('WinInput.ResetCanarySessionState()')) 'GenericUtils reset does not clear compatibility input state.'
    Assert-InProcessCycleContract ($genericUtilsSource.Contains('_saveLogOperationTrail.Clear()')) 'GenericUtils reset does not clear the session save-operation trail.'

    $parserMediatorSource = [IO.File]::ReadAllText($parserMediatorPath)
    $processSource = [IO.File]::ReadAllText($processPath)
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('ResetSessionState')) 'ParserMediator does not expose a session reset boundary.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('BindCompatibilityPlan')) 'ParserMediator does not bind the immutable CompatibilityPlan identity.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('Program.CurrentCompatibilityPlan')) 'ParserMediator does not compare against the startup-bound CompatibilityPlan.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('Parser plan hash does not match')) 'ParserMediator does not fail closed on a plan hash mismatch.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('Parser plan cannot clear')) 'ParserMediator allows a null parser plan to clear a startup-bound plan.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('ConsumeCompatibilityPlan')) 'ParserMediator does not consume the frozen descriptor registry after legacy initialization.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('CurrentCompatibilityConsumption')) 'ParserMediator does not expose the immutable descriptor-consumption snapshot.'
    Assert-InProcessCycleContract ($processSource.Contains('Program.CurrentCompatibilityPlan') -and $processSource.Contains('ConsumeCompatibilityPlan')) 'Process does not guard descriptor consumption behind a startup-bound plan.'
    Assert-InProcessCycleContract ($programSource.Contains('CurrentCompatibilityPlan')) 'Program does not expose the bound CompatibilityPlan to parser initialization.'
    Assert-InProcessCycleContract ($programSource.Contains('ClearCompatibilityPlan')) 'Program does not expose a failed-start plan cleanup boundary.'
    Assert-InProcessCycleContract ($parserMediatorSource.Contains('RenameDic = null')) 'ParserMediator reset does not release the rename dictionary.'
    $identifierDictionarySource = [IO.File]::ReadAllText($identifierDictionaryPath)
    Assert-InProcessCycleContract ($identifierDictionarySource.Contains('GetLegacyInstructionNames')) 'IdentifierDictionary does not expose the legacy instruction registry for plan validation.'
    Assert-InProcessCycleContract ($identifierDictionarySource.Contains('GetLegacyFunctionNames')) 'IdentifierDictionary does not expose the legacy function registry for plan validation.'
    Assert-InProcessCycleContract ($identifierDictionarySource.Contains('BindCompatibilityPlan')) 'IdentifierDictionary does not bind the frozen descriptor route.'
    Assert-InProcessCycleContract ($identifierDictionarySource.Contains('compatibilityInstructionDic ?? instructionDic')) 'Instruction lookup does not honor the frozen descriptor route.'
    Assert-InProcessCycleContract ($identifierDictionarySource.Contains('compatibilityMethodDic ?? methodDic')) 'Function lookup does not honor the frozen descriptor route.'
    Assert-InProcessCycleContract ($identifierDictionarySource.Contains('CompatibilityDescriptorRoute<FunctionIdentifier, FunctionMethod>')) 'IdentifierDictionary does not delegate to the fail-closed descriptor route.'
    Assert-InProcessCycleContract ($processSource.LastIndexOf('ConsumeCompatibilityPlan') -lt $processSource.LastIndexOf('BindCompatibilityPlan(Program.CurrentCompatibilityPlan)')) 'Process binds the descriptor route before validating the legacy registry.'
    $keyMacroSource = [IO.File]::ReadAllText($keyMacroPath)
    Assert-InProcessCycleContract ($keyMacroSource.Contains('ResetCanarySessionState')) 'KeyMacro has no canary reset boundary.'
    Assert-InProcessCycleContract ($keyMacroSource.Contains('macro[i] = ""')) 'KeyMacro reset does not clear macro contents.'
    Assert-InProcessCycleContract (-not $keyMacroSource.Contains('readonly static string macroPath')) 'KeyMacro must resolve macro.txt from the active Program.ExeDir.'
    $functionCreatorMethodSource = [IO.File]::ReadAllText($functionCreatorMethodPath)
    Assert-InProcessCycleContract ($functionCreatorMethodSource.Contains('ResetCanarySessionState')) 'FunctionMethodCreator has no key-toggle reset boundary.'
    Assert-InProcessCycleContract ($functionCreatorMethodSource.Contains('Array.Clear(keytoggle')) 'FunctionMethodCreator reset does not clear GETKEY toggle state.'
    $configSource = [IO.File]::ReadAllText($configPathSource)
    Assert-InProcessCycleContract ($configSource.Contains('ResetCanarySessionState')) 'Legacy Config has no canary projection reset boundary.'
    Assert-InProcessCycleContract ($configSource.Contains('ConfigData.Instance.Clear()')) 'Legacy Config reset does not restore ConfigData defaults.'
    $lazyLoadingSource = [IO.File]::ReadAllText($lazyLoadingPath)
    Assert-InProcessCycleContract ($lazyLoadingSource.Contains('ResetCanarySessionState')) 'Process lazy-loading memo has no canary reset boundary.'
    Assert-InProcessCycleContract ($lazyLoadingSource.Contains('cachedLazyLoadingSourceDir = null')) 'Lazy-loading source memo is not invalidated.'
    $formsSource = [IO.File]::ReadAllText($formsPath)
    Assert-InProcessCycleContract ($formsSource.Contains('ResetSessionState')) 'Compatibility Timer has no session reset boundary.'
    Assert-InProcessCycleContract ($formsSource.Contains('timers.Clear()')) 'Compatibility Timer reset does not clear session timers.'
    $winInputSource = [IO.File]::ReadAllText($winInputPath)
    Assert-InProcessCycleContract ($winInputSource.Contains('ResetCanarySessionState')) 'WinInput has no canary reset boundary.'
    Assert-InProcessCycleContract ($winInputSource.Contains('virtualPressedUntilMs.Clear()')) 'WinInput reset does not clear virtual key pulses.'
    $saveTrailSource = [IO.File]::ReadAllText($saveTrailPath)
    Assert-InProcessCycleContract ($saveTrailSource.Contains('public void Clear()')) 'Save operation trail has no session reset operation.'

    $contentSource = [IO.File]::ReadAllText($contentPath)
    Assert-InProcessCycleContract ($contentSource.Contains('ClearForCanarySessionTransition')) 'EmueraContent does not expose the canary session transition cleanup boundary.'
    Assert-InProcessCycleContract ($contentSource.Contains('ClearHtmlIsland()')) 'Canary view cleanup does not release HTML island nodes.'
    Assert-InProcessCycleContract ($contentSource.Contains('ClearCbgSessionState')) 'Canary view cleanup does not release CBG nodes and pins.'
    Assert-InProcessCycleContract ($contentSource.Contains('ClearGraphicsImageTextureCache')) 'Canary view cleanup does not release GraphicsImage texture resources.'
    Assert-InProcessCycleContract ($contentSource.Contains('ClearSessionAudioState')) 'Canary view cleanup does not release session audio streams.'
    Assert-InProcessCycleContract ($contentSource.Contains('renderedCbgLayers.Clear()')) 'CBG cleanup does not release the retained legacy layer list.'

    $spriteManagerSource = [IO.File]::ReadAllText($spriteManagerPath)
    $forceClearStart = $spriteManagerSource.IndexOf('internal static void ForceClear()')
    $forceClearEnd = $spriteManagerSource.IndexOf('static bool CanEvict', [System.Math]::Max($forceClearStart, 0))
    Assert-InProcessCycleContract ($forceClearStart -ge 0 -and $forceClearEnd -gt $forceClearStart) 'SpriteManager full lifecycle cleanup boundary is missing.'
    $forceClearSource = $spriteManagerSource.Substring($forceClearStart, $forceClearEnd - $forceClearStart)
    Assert-InProcessCycleContract (-not $forceClearSource.Contains('GC.Collect()')) 'SpriteManager full cleanup must not rely on a forced garbage collection to hide retained ownership.'
    Assert-InProcessCycleContract ($spriteManagerSource.Contains('async_texture_loads_idle')) 'SpriteManager does not expose an async texture worker quiescence boundary.'
    Assert-InProcessCycleContract ($forceClearSource.Contains('async_texture_loads_idle.Wait(')) 'SpriteManager full cleanup can clear texture results before async workers quiesce.'
    $colorMatrixGpuSource = [IO.File]::ReadAllText($colorMatrixGpuPath)
    Assert-InProcessCycleContract ($colorMatrixGpuSource.Contains('ResetCanarySessionState')) 'ColorMatrixGPU has no canary material-cache reset boundary.'
    Assert-InProcessCycleContract ($colorMatrixGpuSource.Contains('sharedMaterialCache.Clear()')) 'ColorMatrixGPU reset does not clear matrix-derived materials.'
    $launchRegistrySource = [IO.File]::ReadAllText($launchRegistryPath)
    Assert-InProcessCycleContract ($launchRegistrySource.Contains('LegacySessionLaunchRegistry')) 'Host launch registry is missing.'
    Assert-InProcessCycleContract ($launchRegistrySource.Contains('has no host launch binding')) 'Host launch registry does not reject unbound game ids.'

    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    Assert-InProcessCycleContract ($fixture.sessionIsolationMode -eq 'canary') 'In-process ABA fixture is not pinned to canary mode.'
    Assert-InProcessCycleContract ($fixture.inProcessSessionCycle -eq 'aba') 'In-process ABA fixture does not request the ABA cycle.'
    Assert-InProcessCycleContract ($fixture.inProcessSessionSwitchCount -eq 2) 'In-process ABA fixture does not pin the default two-switch cycle.'
    Assert-InProcessCycleContract (@($fixture.inputs).Count -eq 0) 'In-process ABA fixture must not replay inputs.'

    $crossFixture = Get-Content -LiteralPath $crossFixturePath -Raw | ConvertFrom-Json
    Assert-InProcessCycleContract ($crossFixture.sessionIsolationMode -eq 'canary') 'Cross-ABA fixture is not pinned to canary mode.'
    Assert-InProcessCycleContract ($crossFixture.inProcessSessionCycle -eq 'cross-aba') 'Cross-ABA fixture does not request the cross cycle.'
    Assert-InProcessCycleContract ($crossFixture.inProcessSessionSwitchCount -eq 2) 'Cross-ABA fixture does not pin the default two-switch cycle.'
    Assert-InProcessCycleContract ($null -ne $crossFixture.inProcessAlternateSession) 'Cross-ABA fixture has no alternate session.'
    Assert-InProcessCycleContract (@($crossFixture.inputs).Count -eq 0) 'Cross-ABA fixture must not replay inputs.'

    $comparisonSource = [IO.File]::ReadAllText($comparisonPath)
    foreach ($required in @('in_process_session_wait_fingerprint', 'in_process_session_switch_committed', 'in_process_aba_cycle_completed', 'PreviousGate:M0')) {
        Assert-InProcessCycleContract ($comparisonSource.Contains($required)) "In-process ABA comparison script is missing '$required'."
    }
    Assert-InProcessCycleContract ($comparisonSource.Contains('Same configured game restart only')) 'In-process ABA comparison does not state its same-game limitation.'

    $crossComparisonSource = [IO.File]::ReadAllText($crossComparisonPath)
    foreach ($required in @('in_process_cross_aba_cycle_completed', 'in_process_session_wait_fingerprint', 'in-process-session-cycle.json', 'SwitchCount', 'alternate-fixture-mutations.json', 'PreviousGate:M0')) {
        Assert-InProcessCycleContract ($crossComparisonSource.Contains($required)) "Cross-ABA comparison script is missing '$required'."
    }

    Write-Output 'M1 in-process session-cycle contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
