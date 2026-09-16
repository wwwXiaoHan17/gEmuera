[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-TraceContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $eventPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyTraceEvent.cs'
    $recorderPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyTraceRecorder.cs'
    $schemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-trace.schema.json'
    foreach ($path in @($eventPath, $recorderPath, $schemaPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "M0 trace contract file is missing: $path"
        }
    }

    $recorderSource = [IO.File]::ReadAllText($recorderPath) -replace '(?m)^using .+;\r?\n', ''
    $source = "using System.Diagnostics;`nusing System.Globalization;`nusing System.Threading;`n" +
        [IO.File]::ReadAllText($eventPath) + "`n" + $recorderSource + @'

namespace gEmuera.LegacyRunner.Tests
{
    public static class LegacyTraceConcurrencyProbe
    {
        public static LegacyTraceSnapshot Record(int count)
        {
            var recorder = new LegacyTraceRecorder(count);
            System.Threading.Tasks.Parallel.For(0, count, i =>
            {
                recorder.TryRecord(
                    LegacyTraceCategory.Runner,
                    "concurrent_probe",
                    LegacyTraceThreadOwner.ExternalHarness,
                    LegacyTraceOrderingPoint.RunnerLifecycle,
                    LegacyTraceCompletionMode.ObserveOnly,
                    new LegacyTraceRunnerPayload { Data = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            });
            return recorder.Snapshot();
        }
    }
}
'@
    Add-Type -TypeDefinition $source -Language CSharp

    $snapshot = [gEmuera.LegacyRunner.Tests.LegacyTraceConcurrencyProbe]::Record(512)
    Assert-TraceContract ($snapshot.RecordedCount -eq 512) 'Concurrent recorder lost events.'
    for ($index = 0; $index -lt $snapshot.Events.Length; $index++) {
        Assert-TraceContract ($snapshot.Events[$index].Sequence -eq ($index + 1)) 'Trace sequence is not strictly increasing in stored order.'
    }

    $canonical = $snapshot.ToCanonical()
    Assert-TraceContract ($canonical.Events.Length -eq $snapshot.Events.Length) 'Normalizer changed the event count.'
    for ($index = 0; $index -lt $snapshot.Events.Length; $index++) {
        $raw = $snapshot.Events[$index]
        $normalized = $canonical.Events[$index]
        Assert-TraceContract ($normalized.Sequence -eq $raw.Sequence) 'Normalizer removed or changed sequence.'
        Assert-TraceContract ($normalized.Category -eq $raw.Category) 'Normalizer changed category order.'
        Assert-TraceContract ($normalized.Kind -eq $raw.Kind) 'Normalizer changed event kind order.'
        Assert-TraceContract ($normalized.ObserverMonotonicTicks -eq 0) 'Canonical observer monotonic timestamp was not normalized.'
        Assert-TraceContract ($normalized.ObserverUtc -eq '<NORMALIZED_UTC>') 'Canonical observer UTC timestamp was not normalized.'
    }

    $bounded = New-Object gEmuera.LegacyRunner.LegacyTraceRecorder 2
    $payload = New-Object gEmuera.LegacyRunner.LegacyTraceRunnerPayload
    $payload.Data = 'bounded'
    for ($index = 0; $index -lt 2; $index++) {
        Assert-TraceContract ($bounded.TryRecord(
            [gEmuera.LegacyRunner.LegacyTraceCategory]::Runner,
            'bounded_probe',
            [gEmuera.LegacyRunner.LegacyTraceThreadOwner]::ExternalHarness,
            [gEmuera.LegacyRunner.LegacyTraceOrderingPoint]::RunnerLifecycle,
            [gEmuera.LegacyRunner.LegacyTraceCompletionMode]::ObserveOnly,
            $payload)) 'Recorder rejected an event before reaching capacity.'
    }
    Assert-TraceContract (-not $bounded.TryRecord(
        [gEmuera.LegacyRunner.LegacyTraceCategory]::Runner,
        'overflow_probe',
        [gEmuera.LegacyRunner.LegacyTraceThreadOwner]::ExternalHarness,
        [gEmuera.LegacyRunner.LegacyTraceOrderingPoint]::RunnerLifecycle,
        [gEmuera.LegacyRunner.LegacyTraceCompletionMode]::ObserveOnly,
        $payload)) 'Recorder did not reject an event after reaching capacity.'
    $overflow = $bounded.Snapshot()
    Assert-TraceContract $overflow.Overflowed 'Recorder overflow was not explicit.'
    Assert-TraceContract ($overflow.DroppedEventCount -eq 1) 'Recorder overflow count is incorrect.'

    [void][gEmuera.LegacyRunner.LegacyTrace]::Enable(32)
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordClock('sampled', 'test_clock', 'observer', '1', [gEmuera.LegacyRunner.LegacyTraceThreadOwner]::ExternalHarness)
    $streamId = [gEmuera.LegacyRunner.LegacyTrace]::RegisterRng('test_rng', 'explicit', '7')
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordRngCall('test_rng', $streamId, 1, 'next', '3')
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordWait('pending', 1, 'IntValue', $true, $false, $false, -1, 4)
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordInput('submitted', [gEmuera.LegacyRunner.LegacyTraceThreadOwner]::ExternalHarness,
        [gEmuera.LegacyRunner.LegacyTraceOrderingPoint]::InputSubmission, '1', $true, $false, 0, $true, 'IntValue', 4)
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordDisplayCommit('commit', 0, 1, $false, 4, 'FollowBottom', 0)
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordDisplayProjection('projection', 0, 2, $false, 4, 'FollowBottom', 0)
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordEffect('enqueued', 'audio', 'test.ogg', 'play', 0, 1)
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordError('faulted', 'TestError', 'TEST', 'message', 'probe')
    [void][gEmuera.LegacyRunner.LegacyTrace]::TryRecordRunner('finished', '')
    $typedSnapshot = [gEmuera.LegacyRunner.LegacyTrace]::Disable().Snapshot()
    foreach ($category in @('clock', 'rng', 'wait', 'input', 'display', 'effect', 'error', 'runner')) {
        Assert-TraceContract (@($typedSnapshot.Events | Where-Object Category -eq $category).Count -gt 0) "Typed recorder facade did not emit category: $category"
    }
    Assert-TraceContract (@($typedSnapshot.Events | Where-Object OrderingPoint -eq 'ui_projection').Count -eq 1) 'Projection trace did not retain its UI transport boundary.'
    $semanticSnapshot = $typedSnapshot.ToSemanticComparison()
    Assert-TraceContract ($semanticSnapshot.Events.Length -eq ($typedSnapshot.Events.Length - 1)) 'Semantic trace did not exclude exactly the UI projection event.'
    Assert-TraceContract (@($semanticSnapshot.Events | Where-Object OrderingPoint -eq 'ui_projection').Count -eq 0) 'Semantic trace retained a UI transport event.'
    for ($index = 0; $index -lt $semanticSnapshot.Events.Length; $index++) {
        Assert-TraceContract ($semanticSnapshot.Events[$index].Sequence -eq ($index + 1)) 'Semantic trace sequence is not contiguous after transport filtering.'
    }

    [gEmuera.LegacyRunner.LegacyTrace]::Disable()
    Assert-TraceContract (-not [gEmuera.LegacyRunner.LegacyTrace]::IsEnabled) 'Static trace facade was not disabled by default.'
    Assert-TraceContract (-not [gEmuera.LegacyRunner.LegacyTrace]::TryRecordRunner('disabled_probe', '')) 'Disabled trace facade accepted an event.'

    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    Assert-TraceContract ($schema.properties.events.items.required -contains 'sequence') 'Trace schema does not require sequence.'
    Assert-TraceContract ($schema.properties.events.items.required -contains 'payload') 'Trace schema does not require typed payload.'
    foreach ($category in @('clock', 'rng', 'wait', 'input', 'display', 'effect', 'error', 'runner')) {
        Assert-TraceContract ($schema.properties.events.items.properties.category.enum -contains $category) "Trace schema category is missing: $category"
    }
    Assert-TraceContract ($schema.properties.events.items.allOf.Count -eq 8) 'Trace schema does not bind every category to its payload type.'

    Write-Output 'M0 legacy trace contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
