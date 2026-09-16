[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SettlementContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $trackerPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacySettlementTracker.cs'
    $hostPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerHost.cs'
    $surfacePath = Join-Path $ProjectRoot 'Scripts\EmueraContent.LegacyRunner.cs'
    foreach ($path in @($trackerPath, $hostPath, $surfacePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "M0 settlement contract file is missing: $path"
        }
    }

    $source = [IO.File]::ReadAllText($trackerPath) + @'

namespace gEmuera.LegacyRunner.Tests
{
    public static class LegacySettlementProbe
    {
        public static bool RequiresConsecutiveUnchangedSnapshots()
        {
            var tracker = new LegacySettlementTracker();
            if (tracker.Observe("first", 3)) return false;
            if (tracker.StableFrameCount != 1) return false;
            if (tracker.Observe("first", 3)) return false;
            if (tracker.StableFrameCount != 2) return false;
            if (!tracker.Observe("first", 3)) return false;
            if (tracker.StableFrameCount != 3) return false;
            if (tracker.Observe("changed", 3)) return false;
            if (tracker.StableFrameCount != 1) return false;
            tracker.Reset();
            return tracker.StableFrameCount == 0;
        }
    }
}
'@
    Add-Type -TypeDefinition $source -Language CSharp
    Assert-SettlementContract ([gEmuera.LegacyRunner.Tests.LegacySettlementProbe]::RequiresConsecutiveUnchangedSnapshots()) 'Settlement tracker did not require consecutive identical snapshots.'

    $hostSource = [IO.File]::ReadAllText($hostPath)
    Assert-SettlementContract ($hostSource.Contains('CaptureSettlementFingerprint')) 'Runner host does not capture a settlement fingerprint.'
    Assert-SettlementContract ($hostSource.Contains('LegacyTrace.RecordedCount')) 'Runner settlement fingerprint does not include trace progress.'
    Assert-SettlementContract ($hostSource.Contains('CaptureLegacySettlementFingerprint')) 'Runner settlement fingerprint does not include presentation progress.'
    Assert-SettlementContract (-not $hostSource.Contains('_settleFramesRemaining')) 'Runner still uses a blind fixed-frame settlement countdown.'
    Assert-SettlementContract ([IO.File]::ReadAllText($surfacePath).Contains('CaptureLegacySettlementFingerprint')) 'Presentation layer does not expose its read-only M0 settlement fingerprint.'

    Write-Output 'M0 legacy settlement contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
