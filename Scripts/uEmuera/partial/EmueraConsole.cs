using System;

namespace MinorShift.Emuera.GameView
{
    internal sealed partial class EmueraConsole : IDisposable
    {
        internal ConsoleDisplayLine GetDisplayLinesForuEmuera(int index)
        {
            lock (displayLineLock)
            {
                if(index < 0 || index >= displayLineList.Count)
                    return null;
                return displayLineList[index];
            }
        }
        internal int GetDisplayLinesCount()
        {
            lock (displayLineLock)
                return displayLineList.Count;
        }
        internal ConsoleDisplayLine[] GetDisplayLinesSnapshotForuEmuera()
        {
            lock (displayLineLock)
            {
                var snapshot = new ConsoleDisplayLine[displayLineList.Count];
                displayLineList.CopyTo(snapshot, 0);
                return snapshot;
            }
        }
        internal bool IsInitializing
        {
            get { return state == ConsoleState.Initializing; }
        }
        internal int LastButtonGeneration
        {
            get { return lastButtonGeneration; }
        }
        internal bool IsWaitingInput
        {
            get { return state == ConsoleState.WaitInput; }
        }
        internal bool IsWaitingInputSomething
        {
            get {
                return state == ConsoleState.WaitInput &&
                          (inputReq.InputType == GameProc.InputType.IntValue || 
                          inputReq.InputType == GameProc.InputType.StrValue);
            }
        }
        internal GameProc.InputType InputType
        {
            get
            {
                if(inputReq == null)
                    return GameProc.InputType.Void;
                return inputReq.InputType;
            }
        }
        internal bool IsWaitingOnePhrase
        {
            get { return state == ConsoleState.WaitInput && inputReq != null && inputReq.OneInput; }
        }
    }
}
