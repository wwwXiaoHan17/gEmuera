//This is used by ctrl-z shortcut to undo last input.
//It does this by loading last save, and repeating all inputs except last one.

using System;
using System.Collections.Generic;

namespace MinorShift.Emuera
{
	internal class CtrlZ
	{
		public int mLastSave = -1;
		private int mLastSaveExpected = -1;
		public List<string> mInputs = new List<string>(0x40);

		public long[] mRandomSeed = new long[_Library.MTRandom.N32 + 1];

		public bool mRewindInProgress = false;
		public bool mRepeatedUndoRequested = false;

		public void Add(string s)
		{
			if (mRewindInProgress) return;
			mInputs.Add(s);
		}

		public void OnSavePrepare(int aLastSaveExpected)
		{
			mLastSaveExpected = aLastSaveExpected;
		}

		public void OnSave()
		{
			mLastSave = mLastSaveExpected;
			mInputs.Clear();
			GlobalStatic.VEvaluator.Rand.GetRand(mRandomSeed);
		}

		public void OnLoad(int aSaveFile)
		{
			if (mRewindInProgress) return;
			mLastSave = aSaveFile;
			mInputs.Clear();
			GlobalStatic.VEvaluator.Rand.GetRand(mRandomSeed);
		}

		internal void ResetSessionState()
		{
			mLastSave = -1;
			mLastSaveExpected = -1;
			mInputs.Clear();
			Array.Clear(mRandomSeed, 0, mRandomSeed.Length);
			mRewindInProgress = false;
			mRepeatedUndoRequested = false;
		}
	}
}
