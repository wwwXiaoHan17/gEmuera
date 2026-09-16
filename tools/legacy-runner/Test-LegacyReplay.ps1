[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ReplayContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $driverPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyInputReplayDriver.cs'
    $configPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerConfig.cs'
    $hostPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerHost.cs'
    $schemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-runner-config.schema.json'
    foreach ($path in @($driverPath, $configPath, $hostPath, $schemaPath)) {
        Assert-ReplayContract (Test-Path -LiteralPath $path -PathType Leaf) "M0 replay contract file is missing: $path"
    }

    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    $inputProperties = $schema.properties.inputs.items.properties
    Assert-ReplayContract ($null -ne $inputProperties.expectedInputType) 'Replay schema does not expose expectedInputType.'
    Assert-ReplayContract ($null -ne $inputProperties.expectedButtonGeneration) 'Replay schema does not expose expectedButtonGeneration.'
    $expectedTypes = @($inputProperties.expectedInputType.enum)
    Assert-ReplayContract ($expectedTypes.Count -eq 4) 'Replay schema must expose exactly four safely supported input types.'
    foreach ($inputType in @('EnterKey', 'AnyKey', 'IntValue', 'StrValue')) {
        Assert-ReplayContract ($expectedTypes -contains $inputType) "Replay schema is missing supported input type: $inputType"
    }

    $configSource = [IO.File]::ReadAllText($configPath)
    Assert-ReplayContract ($configSource.Contains('expected_input_type_not_supported')) 'Runner config does not reject unsupported expectedInputType values.'
    Assert-ReplayContract ($configSource.Contains('expected_button_generation_out_of_range')) 'Runner config does not validate expectedButtonGeneration.'

    $driverSource = [IO.File]::ReadAllText($driverPath) -replace '(?m)^using .+;\r?\n', ''
    $driverSource = $driverSource -replace 'internal readonly struct LegacyReplayTick', 'internal struct LegacyReplayTick'
    $driverSource = $driverSource -replace 'return default;', 'return default(LegacyReplayTick);'
    $driverSource = $driverSource -replace '_config = config \?\? throw new ArgumentNullException\(nameof\(config\)\);', 'if (config == null) throw new ArgumentNullException("config"); _config = config;'
    $driverSource = $driverSource -replace 'public int SubmittedCount => _nextInputIndex;', 'public int SubmittedCount { get { return _nextInputIndex; } }'
    $driverSource = $driverSource -replace 'public bool HasInputs => _config.Inputs.Count > 0;', 'public bool HasInputs { get { return _config.Inputs.Count > 0; } }'
    $driverSource = $driverSource -replace 'public bool AllInputsAdvanced => _nextInputIndex >= _config.Inputs.Count && !_awaitingAdvance;', 'public bool AllInputsAdvanced { get { return _nextInputIndex >= _config.Inputs.Count && !_awaitingAdvance; } }'
    $driverSource = $driverSource -replace 'public LegacyReplayTickKind Kind \{ get; \}', 'public LegacyReplayTickKind Kind;'
    $driverSource = $driverSource -replace 'public int InputIndex \{ get; \}', 'public int InputIndex;'
    $driverSource = $driverSource -replace 'public LegacyRunnerInput Input \{ get; \}', 'public LegacyRunnerInput Input;'
    $driverSource = $driverSource -replace 'public string Error \{ get; \}', 'public string Error;'
    $source = @'
using System;
using System.Collections.Generic;
using MinorShift.Emuera.GameView;

namespace MinorShift.Emuera.GameProc
{
    internal enum InputType
    {
        EnterKey = 1,
        AnyKey = 2,
        IntValue = 3,
        StrValue = 4,
        Void = 5
    }
}

namespace MinorShift.Emuera.GameView
{
    internal sealed class EmueraConsole
    {
        internal bool IsWaitingInput { get; set; }
        internal bool IsWaitingInputSomething { get; set; }
        internal bool IsWaitingEnterKey { get; set; }
        internal bool IsWaitAnyKey { get; set; }
        internal bool IsInProcess { get; set; }
        internal MinorShift.Emuera.GameProc.InputType InputType { get; set; }
        internal int NewButtonGeneration { get; set; }
    }
}

namespace gEmuera.LegacyRunner
{
    public sealed class LegacyRunnerInput
    {
        public string Value { get; set; }
        public int WaitTimeoutMs { get; set; }
        public string ExpectedInputType { get; set; }
        public int? ExpectedButtonGeneration { get; set; }

        public LegacyRunnerInput()
        {
            Value = "";
            WaitTimeoutMs = 15000;
            ExpectedInputType = "";
        }
    }

    public sealed class LegacyRunnerConfig
    {
        public List<LegacyRunnerInput> Inputs { get; set; }

        public LegacyRunnerConfig()
        {
            Inputs = new List<LegacyRunnerInput>();
        }
    }
}
'@ + "`n" + $driverSource + @'

namespace gEmuera.LegacyRunner.Tests
{
    public static class LegacyReplayContractProbe
    {
        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static LegacyInputReplayDriver Create(string expectedType, int? expectedGeneration = null)
        {
            var config = new LegacyRunnerConfig();
            config.Inputs.Add(new LegacyRunnerInput
            {
                Value = "0",
                ExpectedInputType = expectedType,
                ExpectedButtonGeneration = expectedGeneration,
                WaitTimeoutMs = 1000
            });
            return new LegacyInputReplayDriver(config);
        }

        public static string Run()
        {
            var enter = new MinorShift.Emuera.GameView.EmueraConsole
            {
                IsWaitingInput = true,
                IsWaitingEnterKey = true,
                IsInProcess = true,
                InputType = MinorShift.Emuera.GameProc.InputType.EnterKey,
                NewButtonGeneration = 3
            };
            Require(Create("EnterKey").Tick(enter, 0).Kind == LegacyReplayTickKind.Submit,
                "EnterKey wait was not submitted.");

            var fakeError = new MinorShift.Emuera.GameView.EmueraConsole
            {
                IsWaitingInput = false,
                IsWaitingEnterKey = true,
                IsInProcess = false,
                InputType = MinorShift.Emuera.GameProc.InputType.Void
            };
            Require(Create("EnterKey").Tick(fakeError, 0).Kind == LegacyReplayTickKind.None,
                "Error/Quit fake EnterKey wait was accepted.");

            var stringWait = new MinorShift.Emuera.GameView.EmueraConsole
            {
                IsWaitingInput = true,
                IsWaitingInputSomething = true,
                IsInProcess = true,
                InputType = MinorShift.Emuera.GameProc.InputType.StrValue,
                NewButtonGeneration = 5
            };
            var wrongType = Create("IntValue").Tick(stringWait, 0);
            Require(wrongType.Kind == LegacyReplayTickKind.Failed && wrongType.Error.Contains("unexpected_input_type"),
                "Input type drift did not fail fast.");

            var wrongGeneration = Create("StrValue", 4).Tick(stringWait, 0);
            Require(wrongGeneration.Kind == LegacyReplayTickKind.Failed && wrongGeneration.Error.Contains("unexpected_button_generation"),
                "Button generation drift did not fail fast.");

            var advancing = Create("StrValue", 5);
            Require(advancing.Tick(stringWait, 0).Kind == LegacyReplayTickKind.Submit,
                "Matching value wait was not submitted.");
            Require(advancing.Tick(stringWait, 10).Kind == LegacyReplayTickKind.None,
                "Replay advanced before the wait changed.");
            stringWait.NewButtonGeneration = 6;
            Require(advancing.Tick(stringWait, 20).Kind == LegacyReplayTickKind.Advanced,
                "Replay did not advance after generation changed.");

            return "M0 legacy replay contract tests passed.";
        }
    }
}
'@
    Add-Type -TypeDefinition $source -Language CSharp
    Write-Output ([gEmuera.LegacyRunner.Tests.LegacyReplayContractProbe]::Run())

    $hostSource = [IO.File]::ReadAllText($hostPath)
    Assert-ReplayContract ($hostSource.Contains('LegacyInputReplayDriver.IsReplayWait(console)')) 'Runner host does not reuse the safe replay wait predicate.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
