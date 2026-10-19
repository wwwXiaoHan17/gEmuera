using System;

namespace gEmuera.LegacyRunner
{
    /// <summary>
    /// Tracks consecutive identical Legacy runner snapshots. The runner owns the
    /// snapshot content; this type only owns the no-side-effect stability rule.
    /// </summary>
    internal sealed class LegacySettlementTracker
    {
        string _lastFingerprint = "";
        int _stableFrameCount;

        public int StableFrameCount
        {
            get { return _stableFrameCount; }
        }

        public bool Observe(string fingerprint, int requiredStableFrames)
        {
            if (string.IsNullOrEmpty(fingerprint))
                throw new ArgumentException("Settlement fingerprint must not be empty.", "fingerprint");
            if (requiredStableFrames < 1)
                throw new ArgumentOutOfRangeException("requiredStableFrames");

            if (!string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
            {
                _lastFingerprint = fingerprint;
                _stableFrameCount = 1;
            }
            else if (_stableFrameCount < requiredStableFrames)
            {
                _stableFrameCount++;
            }

            return _stableFrameCount >= requiredStableFrames;
        }

        public void Reset()
        {
            _lastFingerprint = "";
            _stableFrameCount = 0;
        }
    }
}
