using System;
using System.Threading;
using Godot;

public class EmueraThread
{
    public static EmueraThread instance { get { return instance_; } }
    static EmueraThread instance_ = new EmueraThread();

    EmueraThread()
    { }

    public void Start(bool debug, bool use_coroutine)
    {
        debugmode = debug;
        running = true;
        if(inputEvent == null)
            inputEvent = new ManualResetEventSlim(false);
        if(thread != null)
        {
            thread.Join(1000);
            thread = null;
        }
        thread = new Thread(Work);
        thread.Start();
    }

    public void End()
    {
        if(!running && thread == null)
            return;
        running = false;
        // Wake up the input wait loop
        inputEvent?.Set();
        if(thread != null)
        {
            thread.Join(2000);
            thread = null;
        }
        inputEvent?.Dispose();
        inputEvent = null;
    }

    public bool Running()
    {
        var console = MinorShift.Emuera.GlobalStatic.Console;
        if(console != null && console.IsInProcess)
            return true;
        return false;
    }

    public void Input(string c, bool from_button, bool skip = false)
    {
        var console = MinorShift.Emuera.GlobalStatic.Console;
        if(console == null)
            return;
        if(!from_button && console.IsWaitingInputSomething)
            return;
        input = c;
        skipflag = skip;
        inputEvent?.Set();
    }

    public bool IsSkipFlag { get { return skipflag; } }

    void Work()
    {
        MinorShift.Emuera.Program.debugMode = debugmode;
        MinorShift.Emuera.Program.Main(new string[0] { });

        uEmuera.Utils.ResourceClear();
        GC.Collect();

        input = null;
        var console = MinorShift.Emuera.GlobalStatic.Console;
        var random = new System.Random();
        while(running)
        {
            skipflag = false;
            inputEvent.Reset();

            while(input == null)
            {
                // Block efficiently until Input() is called or a short timeout expires
                inputEvent.Wait(100);
                if(!running)
                    return;
                uEmuera.Forms.Timer.Update();
            }

            if(console.IsWaitingInput)
            {
                if(console.IsWaitingEnterKey)
                    input = "";
                console.PressEnterKey(skipflag, input, false);
            }
            input = null;
        }
    }

    Thread thread = null;
    ManualResetEventSlim inputEvent = new ManualResetEventSlim(false);
    bool debugmode;
    volatile bool running;
    volatile string input;
    volatile bool skipflag;
}
