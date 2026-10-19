using System.Threading;

namespace gEmuera.LegacyRunner
{
    /// <summary>
    /// Runner-only deterministic inputs. This is configured before the legacy
    /// VM thread starts and is never used by interactive launches.
    /// </summary>
    internal static class LegacyRunnerDeterminism
    {
        static int _randomSeed;
        static int _hasRandomSeed;

        public static void Configure(int randomSeed)
        {
            Volatile.Write(ref _randomSeed, randomSeed);
            Volatile.Write(ref _hasRandomSeed, 1);
        }

        public static bool TryGetRandomSeed(out int randomSeed)
        {
            if (Volatile.Read(ref _hasRandomSeed) == 0)
            {
                randomSeed = 0;
                return false;
            }
            randomSeed = Volatile.Read(ref _randomSeed);
            return true;
        }
    }
}
