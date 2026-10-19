using System;
using System.Collections.Generic;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Compatibility;

namespace MinorShift.Emuera.GameProc
{
	//1756 インナークラス解除して一般に開放


	//難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
	[global::System.Reflection.Obfuscation(Exclude = false)]
	internal enum SystemStateCode
	{
		__CAN_SAVE__ = 0x10000,//セーブロード画面を呼び出し可能か？
		__CAN_BEGIN__ = 0x20000,//BEGIN命令を呼び出し可能か？
		Title_Begin = 0,//初期状態
		Openning = 1,//最初の入力待ち
		Train_Begin = 0x10,//BEGIN TRAINから。
		Train_CallEventTrain = 0x11,//@EVENTTRAINの呼び出し中。スキップ可能
		Train_CallShowStatus = 0x12,//@SHOW_STATUSの呼び出し中
		Train_CallComAbleXX = 0x13,//@COM_ABLExxの呼び出し中。スキップの場合、RETURN 1とする。
		Train_CallShowUserCom = 0x14,//@SHOW_USERCOMの呼び出し中
		Train_WaitInput = 0x15,//入力待ち状態。選択が実行可能ならEVENTCOMからCOMxx、そうでなければ@USERCOMにRESULTを渡す
		Train_CallEventCom = 0x16 | __CAN_BEGIN__,//@EVENTCOMの呼び出し中

		Train_CallComXX = 0x17 | __CAN_BEGIN__,//@COMxxの呼び出し中
		Train_CallSourceCheck = 0x18 | __CAN_BEGIN__,//@SOURCE_CHECKの呼び出し中
		Train_CallEventComEnd = 0x19 | __CAN_BEGIN__,//@EVENTCOMENDの呼び出し中。スキップ可能。Train_CallEventTrainへ帰る。@USERCOMの呼び出し中もここ

		Train_DoTrain = 0x1A,

		AfterTrain_Begin = 0x20 | __CAN_BEGIN__,//BEGIN AFTERTRAINから。@EVENTENDを呼び出してNormalへ。

		Ablup_Begin = 0x30,//BEGIN ABLUPから。
		Ablup_CallShowJuel = 0x31,//@SHOW_JUEL
		Ablup_CallShowAblupSelect = 0x32,//@SHOW_ABLUP_SELECT
		Ablup_WaitInput = 0x33,//
		Ablup_CallAblupXX = 0x34 | __CAN_BEGIN__,//@ABLUPxxがない場合は、@USERABLUPにRESULTを渡す。Ablup_CallShowJuelへ戻る。

		Turnend_Begin = 0x40 | __CAN_BEGIN__,//BEGIN TURNENDから。@EVENTTURNENDを呼び出してNormalへ。

		Shop_Begin = 0x50 | __CAN_SAVE__,//BEGIN SHOPから
		Shop_CallEventShop = 0x51 | __CAN_BEGIN__ | __CAN_SAVE__,//@EVENTSHOPの呼び出し中。スキップ可能
		Shop_CallShowShop = 0x52 | __CAN_SAVE__,//@SHOW_SHOPの呼び出し中
		Shop_WaitInput = 0x53 | __CAN_SAVE__,//入力待ち状態。アイテムが存在するならEVENTBUYにBOUGHT、そうでなければ@USERSHOPにRESULTを渡す
		Shop_CallEventBuy = 0x54 | __CAN_BEGIN__ | __CAN_SAVE__,//@USERSHOPまた@EVENTBUYはの呼び出し中

		SaveGame_Begin = 0x100,//SAVEGAMEから
		SaveGame_WaitInput = 0x101,//入力待ち
		SaveGame_WaitInputOverwrite = 0x102,//上書きの許可待ち
		SaveGame_CallSaveInfo = 0x103,//@SAVEINFO呼び出し中。20回。
		LoadGame_Begin = 0x110,//LOADGAMEから
		LoadGame_WaitInput = 0x111,//入力待ち
		LoadGameOpenning_Begin = 0x120,//最初に[1]を選択したとき。
		LoadGameOpenning_WaitInput = 0x121,//入力待ち


		//AutoSave_Begin = 0x200,
		AutoSave_CallSaveInfo = 0x201,
		AutoSave_CallUniqueAutosave = 0x202,
		AutoSave_Skipped = 0x203,

		LoadData_DataLoaded = 0x210,//データロード直後
		LoadData_CallSystemLoad = 0x211 | __CAN_BEGIN__,//データロード直後
		LoadData_CallEventLoad = 0x212 | __CAN_BEGIN__,//@EVENTLOADの呼び出し中。スキップ可能

		Openning_TitleLoadgame = 0x220,

		System_Reloaderb = 0x230,
		First_Begin = 0x240,

		Normal = 0xFFFF | __CAN_BEGIN__ | __CAN_SAVE__,//特に何でもないとき。ScriptEndに達したらエラー
	}

	//難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
	[global::System.Reflection.Obfuscation(Exclude = false)]
	internal enum BeginType
	{
		NULL = 0,
		SHOP = 2,
		TRAIN = 3,
		AFTERTRAIN = 4,
		ABLUP = 5,
		TURNEND = 6,
		FIRST = 7,
		TITLE = 8,
	}

	internal sealed class ProcessState
	{
		public ProcessState(EmueraConsole console)
		{
			if (Program.DebugMode)//DebugModeでなければ知らなくて良い
				this.console = console;
		}
		readonly EmueraConsole console = null;
		readonly List<CalledFunction> functionList = new List<CalledFunction>();
		readonly Stack<ExecutionContext> contextStack = new Stack<ExecutionContext>();
		private Stack<ExecutionContext> savedContextStack;
		// LOCAL/ARG 解析缓存按此代际失效：每次上下文栈变化（入栈/出栈，
		// 以及 variadic 重分配导致的 Arg 数组替换）自增。进程级单调递增，
		// 避免跨 ProcessState（含调试求值克隆体）代际复用导致脏缓存命中。
		internal static long ContextStackGeneration;
		private LogicalLine currentLine;
		//private LogicalLine nextLine;
		public int lineCount = 0;
        public int currentMin = 0;
        //private bool sequential;

		private string pendingThrowMessage;
		private bool inBeforeError;
		private bool skipBeforeError;
		private bool inBeforeThrow;
		private InstructionLine pendingThrowLine;
		private Exception pendingErrorException;
		private LogicalLine pendingErrorCurrentLine;
		private bool pendingErrorSystemProc;

		public bool ScriptEnd
		{
			get
			{
                return functionList.Count == currentMin;
            }
		}

        public int functionCount
        {
            get
            {
                return functionList.Count;
            }
        }

		public ExecutionContext CurrentContext
		{
			get { return contextStack.Count > 0 ? contextStack.Peek() : savedContextStack != null && savedContextStack.Count > 0 ? savedContextStack.Peek() : null; }
		}

		public IEnumerable<ExecutionContext> ContextStack
		{
			get { return contextStack.Count > 0 ? contextStack : savedContextStack ?? contextStack; }
		}

		public ExecutionContext FindContextByLabel(string labelName)
		{
			Stack<ExecutionContext> stack = contextStack.Count > 0 ? contextStack : savedContextStack;
			if (stack != null)
			{
				foreach (ExecutionContext context in stack)
				{
					if (context.Function != null && context.Function.LabelName == labelName)
						return context;
				}
			}
			return null;
		}

		public void PushContext(ExecutionContext context)
		{
			contextStack.Push(context);
			ContextStackGeneration++;
		}

		public ExecutionContext PopContext()
		{
			if (contextStack.Count == 0)
				return null;
			ContextStackGeneration++;
			return contextStack.Pop();
		}

		public int ContextStackCount
		{
			get { return contextStack.Count; }
		}

		public (int funcCount, int ctxCount, LogicalLine currentLine) CaptureCallState()
		{
			return (functionList.Count, contextStack.Count, currentLine);
		}

		public void RollbackToState(int targetFuncCount, int targetCtxCount, LogicalLine targetCurrentLine)
		{
			while (functionList.Count > targetFuncCount)
			{
				CalledFunction called = functionList[functionList.Count - 1];
				if (called.CurrentLabel.hasPrivDynamicVar)
					called.CurrentLabel.Out();
				functionList.RemoveAt(functionList.Count - 1);
			}
			while (contextStack.Count > targetCtxCount)
			{
				ExecutionContext context = contextStack.Pop();
				ContextStackGeneration++;
				context?.Dispose();
			}
			currentLine = targetCurrentLine;
		}

		SystemStateCode sysStateCode = SystemStateCode.Title_Begin;
		BeginType begintype = BeginType.NULL;
		public bool isBegun { get { return (begintype != BeginType.NULL) ? true : false; } }

        public LogicalLine CurrentLine { get { return currentLine; } set { currentLine = value; } }
        public LogicalLine ErrorLine
		{
			get
			{
				//if (RunningLine != null)
				//	return RunningLine;
				return currentLine;
			}
		}

		//IF文中でELSEIF文の中身をチェックするなどCurrentLineと作業中のLineが違う時にセットする
		//public LogicalLine RunningLine { get; set; }
		//1755a 呼び出し元消滅
		//public bool Sequential { get { return sequential; } }
		public CalledFunction CurrentCalled
		{
			get
			{
				//実行関数なしの状態は一部のシステムINPUT以外では存在しないのでGOTO系の処理でしかここに来ない関係上、前提を満たしようがない
				//if (functionList.Count == 0)
				//    throw new ExeEE("実行中関数がない");
				return functionList[functionList.Count - 1];
			}
		}

		public int CurrentVariadicArgCount
		{
			get
			{
				if (functionList.Count == 0)
					return 0;
				return functionList[functionList.Count - 1].VariadicArgCount;
			}
		}

		public string PendingThrowMessage { get { return pendingThrowMessage; } set { pendingThrowMessage = value; } }
		public bool HasPendingThrow { get { return pendingThrowMessage != null; } }
		public void ClearPendingThrow() { pendingThrowMessage = null; }
		public InstructionLine PendingThrowLine { get { return pendingThrowLine; } set { pendingThrowLine = value; } }

		public bool InBeforeError { get { return inBeforeError; } set { inBeforeError = value; } }
		public bool SkipBeforeError { get { return skipBeforeError; } set { skipBeforeError = value; } }
		public bool InBeforeThrow { get { return inBeforeThrow; } set { inBeforeThrow = value; } }
		public bool IsInBeforeThrow
		{
			get
			{
				foreach (CalledFunction called in functionList)
				{
					if (called.IsEvent && called.FunctionName == "BEFORE_THROW")
						return true;
				}
				return false;
			}
		}

		public Exception PendingErrorException { get { return pendingErrorException; } set { pendingErrorException = value; } }
		public LogicalLine PendingErrorCurrentLine { get { return pendingErrorCurrentLine; } set { pendingErrorCurrentLine = value; } }
		public bool PendingErrorSystemProc { get { return pendingErrorSystemProc; } set { pendingErrorSystemProc = value; } }

		public SystemStateCode SystemState
		{
			get { return sysStateCode; }
			set { sysStateCode = value; }
		}

		public void ShiftNextLine()
		{
            currentLine = currentLine.NextLine;
            //nextLine = nextLine.NextLine;
            //RunningLine = null;
            //sequential = true;
			//GlobalStatic.Process.lineCount++;
			lineCount++;
		}

		/// <summary>
		/// 関数内の移動。JUMPではなくGOTOやIF文など
		/// </summary>
		/// <param name="line"></param>
		public void JumpTo(LogicalLine line)
		{
            currentLine = line;
            lineCount++;
            //sequential = false;
			//ShfitNextLine();
		}

		public void SetBegin(string keyword)
		{
			SetBegin(keyword, false);
		}

		public void SetBegin(string keyword, bool force)
		{//TrimとToUpper済みのはず
			switch (keyword)
			{
				case "SHOP":
					SetBegin(BeginType.SHOP, force); return;
				case "TRAIN":
					SetBegin(BeginType.TRAIN, force); return;
				case "AFTERTRAIN":
					SetBegin(BeginType.AFTERTRAIN, force); return;
				case "ABLUP":
					SetBegin(BeginType.ABLUP, force); return;
				case "TURNEND":
					SetBegin(BeginType.TURNEND, force); return;
				case "FIRST":
					SetBegin(BeginType.FIRST, force); return;
				case "TITLE":
					SetBegin(BeginType.TITLE, force); return;
			}
			throw new CodeEE("BEGINのキーワード\"" + keyword + "\"は未定義です");
		}

		public void SetBegin(BeginType type)
		{
			SetBegin(type, false);
		}

		public void SetBegin(BeginType type, bool force)
		{
			string errmes;
			switch (type)
			{
				case BeginType.SHOP:
				case BeginType.TRAIN:
				case BeginType.AFTERTRAIN:
				case BeginType.ABLUP:
				case BeginType.TURNEND:
				case BeginType.FIRST:
					if (!force && (sysStateCode & SystemStateCode.__CAN_BEGIN__) != SystemStateCode.__CAN_BEGIN__)
					{
						errmes = "BEGIN";
						goto err;
					}
					break;
				//1.729 BEGIN TITLEはどこでも使えるように
				case BeginType.TITLE:
					break;
				//BEGINの処理中でチェック済み
				//default:
				//    throw new ExeEE("不適当なBEGIN呼び出し");
			}
			begintype = type;
			return;
		err:
			CalledFunction func = functionList[0];
			string funcName = func.FunctionName;
			throw new CodeEE("@" + funcName + "中で" + errmes + "命令を実行することはできません");
		}

		public void SaveLoadData(bool saveData)
		{

			if (saveData)
				sysStateCode = SystemStateCode.SaveGame_Begin;
			else
				sysStateCode = SystemStateCode.LoadGame_Begin;
			//ClearFunctionList();
			return;
		}

		public void ClearFunctionList()
		{
			if (Program.DebugMode && !isClone && GlobalStatic.Process.MethodStack() == 0)
				console.DebugClearTraceLog();
			foreach (CalledFunction called in functionList)
                if (called.CurrentLabel.hasPrivDynamicVar)
                    called.CurrentLabel.Out();
			while (contextStack.Count > 0)
			{
				ExecutionContext context = contextStack.Pop();
				ContextStackGeneration++;
				context.Dispose();
			}
			functionList.Clear();
			begintype = BeginType.NULL;
		}

		public void ClearFunctionListPreserveTrace()
		{
			foreach (CalledFunction called in functionList)
				if (called.CurrentLabel.hasPrivDynamicVar)
					called.CurrentLabel.Out();
			while (contextStack.Count > 0)
			{
				ExecutionContext context = contextStack.Pop();
				ContextStackGeneration++;
				context.Dispose();
			}
			functionList.Clear();
			begintype = BeginType.NULL;
		}

		public bool calledWhenNormal = true;
		/// <summary>
		/// BEGIN命令によるプログラム状態の変化
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public void Begin()
		{
			//@EVENTSHOPからの呼び出しは一旦破棄
			if (sysStateCode == SystemStateCode.Shop_CallEventShop)
				return;

			switch (begintype)
			{
				case BeginType.SHOP:
					if (sysStateCode == SystemStateCode.Normal)
						calledWhenNormal = true;
					else
						calledWhenNormal = false;
					sysStateCode = SystemStateCode.Shop_Begin;
					break;
				case BeginType.TRAIN:
					sysStateCode = SystemStateCode.Train_Begin;
					break;
				case BeginType.AFTERTRAIN:
					sysStateCode = SystemStateCode.AfterTrain_Begin;
					break;
				case BeginType.ABLUP:
					sysStateCode = SystemStateCode.Ablup_Begin;
					break;
				case BeginType.TURNEND:
					sysStateCode = SystemStateCode.Turnend_Begin;
					break;
				case BeginType.FIRST:
					sysStateCode = SystemStateCode.First_Begin;
					break;
				case BeginType.TITLE:
					sysStateCode = SystemStateCode.Title_Begin;
					break;
				//セット時に判定してるので、ここには来ないはず
				//default:
				//    throw new ExeEE("不適当なBEGIN呼び出し");
			}
			if (Program.DebugMode)
			{
				console.DebugClearTraceLog();
				console.DebugAddTraceLog("BEGIN:" + begintype.ToString());
			}
			foreach (CalledFunction called in functionList)
                if (called.CurrentLabel.hasPrivDynamicVar)
                    called.CurrentLabel.Out();
			while (contextStack.Count > 0)
			{
				ExecutionContext context = contextStack.Pop();
				ContextStackGeneration++;
				context.Dispose();
			}
			functionList.Clear();
			begintype = BeginType.NULL;
			return;
		}

		/// <summary>
		/// システムによる強制的なBEGIN
		/// </summary>
		/// <param name="type"></param>
		public void Begin(BeginType type)
		{
			begintype = type;
			sysStateCode = SystemStateCode.Title_Begin;
			Begin();
		}

		public LogicalLine GetCurrentReturnAddress
		{
			get
			{
                if (functionList.Count == currentMin)
                    return null;
				return functionList[functionList.Count - 1].ReturnAddress;
			}
		}

        public LogicalLine GetReturnAddressSequensial(int curerntDepth)
        {
            if (functionList.Count == currentMin)
                return null;
            return functionList[functionList.Count - curerntDepth - 1].ReturnAddress;
        }

		public string Scope
		{
			get
			{
				//スクリプトの実行中処理からしか呼び出されないので、ここはない…はず
				//if (functionList.Count == 0)
				//{
				//    throw new ExeEE("実行中の関数が存在しません");
				//}
				if (functionList.Count == 0)
					return null;//1756 デバッグコマンドから呼び出されるようになったので
				return functionList[functionList.Count - 1].FunctionName;
			}
		}

		public void Return(Int64 ret)
		{
			CalledFunction called = functionList[functionList.Count - 1];
			// #FUNCTION/#FUNCTIONS 的隐式 RETURN 必须只看当前栈顶。
			// 普通 CALL 可能发生在外层表达式函数求值期间，不能被外层 currentMin 误判成 RETURNF。
			if (IsCurrentFunctionMethod && !(called.IsEvent && (called.FunctionName == "BEFORE_THROW" || called.FunctionName == "BEFORE_ERROR")))
			{
				ReturnF(null);
				return;
			}
			TryFinalizeEraFlGMapLoad(called);
			if (TryRecoverEraFlQuestStartRoom(called, ret, out long recoveredRoomIndex))
			{
				ret = recoveredRoomIndex;
				// RETURN 指令在进入 ProcessState 前已经写入 RESULT；同步回写，确保调用方读到恢复后的下标。
				if (GlobalStatic.VEvaluator != null)
					GlobalStatic.VEvaluator.RESULT = recoveredRoomIndex;
			}
			//sequential = false;//いずれにしろ順列ではない。
			//呼び出し元は全部スクリプト処理
			//if (functionList.Count == 0)
			//{
			//    throw new ExeEE("実行中の関数が存在しません");
			//}
			if (called.IsJump)
			{//JUMPした場合。即座にRETURN RESULTする。
                if (called.TopLabel.hasPrivDynamicVar)
                    called.TopLabel.Out();
				ExecutionContext context = PopContext();
				context?.Dispose();
				functionList.Remove(called);
				if (Program.DebugMode)
					console.DebugRemoveTraceLog();
				Return(ret);
				return;
			}
			if (!called.IsEvent)
			{
                if (called.TopLabel.hasPrivDynamicVar)
                    called.TopLabel.Out();
				ExecutionContext context = PopContext();
				context?.Dispose();
                currentLine = null;
            }
			else
			{
                if (called.CurrentLabel.hasPrivDynamicVar)
                    called.CurrentLabel.Out();
				ExecutionContext context = PopContext();
				context?.Dispose();
				//#Singleフラグ付き関数で1が返された。
				//1752 非0ではなく1と等価であることを見るように修正
				//1756 全てを終了ではなく#PRIや#LATERのグループごとに修正
                if (called.IsOnly)
                    called.FinishEvent();
				else if ((called.HasSingleFlag) && (ret == 1))
					called.ShiftNextGroup();
				else
                    called.ShiftNext();//次の同名関数に進む。
                currentLine = called.CurrentLabel;//関数の始点(@～～)へ移動。呼ぶべき関数が無ければnull
                if (called.CurrentLabel != null)
                {
                    lineCount++;
                    if (called.CurrentLabel.hasPrivDynamicVar)
                        called.CurrentLabel.In();
					PushContext(new ExecutionContext(called.CurrentLabel, CurrentContext));
                }
            }
			if (Program.DebugMode)
				console.DebugRemoveTraceLog();
			if (currentLine == null && called.IsEvent && called.FunctionName == "BEFORE_THROW" && pendingThrowMessage != null)
			{
				string msg = pendingThrowMessage;
				functionList.RemoveAt(functionList.Count - 1);
				pendingThrowMessage = null;
				inBeforeThrow = false;
				skipBeforeError = true;
				throw new CodeEE(msg, pendingThrowLine != null ? pendingThrowLine.Position : null);
			}
			if (currentLine == null && called.IsEvent && called.FunctionName == "BEFORE_THROW")
				inBeforeThrow = false;
			if (currentLine == null && called.IsEvent && called.FunctionName == "BEFORE_ERROR" && pendingErrorException != null)
			{
				Exception ec = pendingErrorException;
				ScriptPosition pos = null;
				if (ec is EmueraException ee && ee.Position != null)
					pos = ee.Position;
				else if (pendingErrorCurrentLine != null)
					pos = pendingErrorCurrentLine.Position;
				functionList.RemoveAt(functionList.Count - 1);
				pendingErrorException = null;
				inBeforeError = true;
				throw new CodeEE(ec.Message, pos);
			}
			//関数終了
            if (currentLine == null)
            {
                currentLine = called.ReturnAddress;
                functionList.RemoveAt(functionList.Count - 1);
				if (currentLine == null)
				{
					//この時点でfunctionListは空のはず
					//functionList.Clear();//全て終了。stateEndProcessに処理を返す
					if (begintype != BeginType.NULL)//BEGIN XXが行なわれていれば
					{
						Begin();
					}
					return;
				}
                lineCount++;
                //ShfitNextLine();
                return;
			}
			else if (Program.DebugMode)
			{
				FunctionLabelLine label = called.CurrentLabel;
				console.DebugAddTraceLog("CALL :@" + label.LabelName + ":" + label.Position.ToString() + "行目");
			}
            lineCount++;
            //ShfitNextLine();
			return;
		}

		/// <summary>
		/// eraFL 的任务脚本会先以标签查找起点，再把返回的房间下标交给地图读写函数。
		/// 当标签查询异常返回 -1、但当前任务地图仍有唯一 [ROOM_ID:200] 起点时，在
		/// 这里恢复正确下标，避免把 -1 推入后续二维数组访问。只接受默认查找模式，
		/// 不影响显式随机、存档数据查找或其它 profile 的普通“找不到标签”语义。
		/// </summary>
		private bool TryRecoverEraFlQuestStartRoom(
			CalledFunction called,
			Int64 returnedRoomIndex,
			out Int64 recoveredRoomIndex)
		{
			IEraFlCompatibilityPolicy eraFl = Program.Compatibility.EraFl;
			recoveredRoomIndex = returnedRoomIndex;
			if (!eraFl.IsEnabled || called == null || called.IsEvent || called.IsJump
				|| returnedRoomIndex != -1 || called.TopLabel == null
				|| !IsEraFlFunction(
					called,
					eraFl.TaskStartRoomLookupFunction))
			{
				return false;
			}

			// RETURN 仍位于当前函数的私有参数出栈之前，直接读取活动调用帧最可靠。
			// 进入函数时保存的快照只作为异常路径后备，避免预解析模板、嵌套调用或
			// 旧调用入口没有完成捕获时，让精确的 eraFL 起点恢复静默失效。
			EraFlQuestStartLookupContext lookup;
			if (!TryReadEraFlQuestStartLookupFromActiveFrame(called, out lookup))
				lookup = called.EraFlQuestStartLookup;
			if (!lookup.IsCaptured || lookup.Random != 0 || lookup.FromSavedata != 0)
			{
				return false;
			}

			// GMAP 必须先从已导入 DT 补完 node 数据；普通 MAP 则直接在运行时
			// 二维数组中确认唯一的 ROOM_ID:200。
			string[,] mapData = TryGetEraFlMapDataArray();

			if (string.Equals(
				lookup.QuestType,
				eraFl.GMapQuestType,
				StringComparison.Ordinal))
			{
				Int64 mapCount = 1;
				if (!TryReadGlobalInteger("QST_MAPNUM", out mapCount) || mapCount <= 0)
					mapCount = 1;
				if (!TryPopulateEraFlGMapRoomData(lookup.MapId, mapCount, mapData))
					TryPopulateEraFlGMapRoomDataFromFiles(lookup.MapId, mapData);
			}

			return eraFl.TryRecoverQuestStartRoomIndex(
				called.FunctionName,
				returnedRoomIndex,
				lookup.RequestedRoomTag,
				lookup.MapId,
				lookup.QuestType,
				mapData,
				out recoveredRoomIndex);
		}

		/// <summary>
		/// eraFL 的 QST_LOAD_MAPDATA 返回后，游戏马上按标签查找任务起点。
		/// 在这个明确边界完成 GMAP 实体化，使正常 ERB 查询自行成功；RETURN 处的
		/// -1 恢复只保留为最后防线。DT 桥为空时才读取同一份 schema/XML 文件回退。
		/// </summary>
		private void TryFinalizeEraFlGMapLoad(CalledFunction called)
		{
			IEraFlCompatibilityPolicy eraFl = Program.Compatibility.EraFl;
			if (!eraFl.IsEnabled || called == null || called.IsEvent || called.IsJump
				|| !IsEraFlFunction(called, "QST_LOAD_MAPDATA"))
			{
				return;
			}

			string questType;
			if (!TryReadEraFlQuestTypeFromCaller(out questType))
				TryReadGlobalString("QST_QUEST_TYPE", out questType);
			if (!string.Equals(
				questType,
				eraFl.GMapQuestType,
				StringComparison.Ordinal))
			{
				return;
			}

			string[,] mapData = TryGetEraFlMapDataArray();
			if (mapData == null)
			{
				console.PrintError("[ERAFL_COMPAT] GMAP实体化失败: _DIC_HO_MAPDATA不可用");
				return;
			}

			Int64 mapCount = 1;
			if (!TryReadGlobalInteger("QST_MAPNUM", out mapCount) || mapCount <= 0)
				mapCount = 1;
			mapCount = Math.Min(mapCount, mapData.GetLength(0));
			for (Int64 mapId = 0; mapId < mapCount; mapId++)
			{
				if (TryPopulateEraFlGMapRoomData(mapId, mapCount, mapData)
					|| TryPopulateEraFlGMapRoomDataFromFiles(mapId, mapData))
				{
					continue;
				}
				console.PrintError("[ERAFL_COMPAT] GMAP实体化失败: map=" + mapId.ToString()
					+ "，DT与schema/XML均未提供完整节点");
			}
		}

		private static bool IsEraFlFunction(CalledFunction called, string functionName)
		{
			return called != null && (string.Equals(called.FunctionName, functionName, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(called.TopLabel?.LabelName, functionName, StringComparison.OrdinalIgnoreCase));
		}

		private static string[,] TryGetEraFlMapDataArray()
		{
			VariableToken token = GlobalStatic.IdentifierDictionary?
				.GetVariableToken("_DIC_HO_MAPDATA", null, false);
			if (token == null || !token.IsString || !token.IsArray2D
				|| token.IsReference || token.IsCharacterData)
			{
				return null;
			}
			return token.GetArray() as string[,];
		}

		/// <summary>
		/// 从仍处于活动状态的 HO_FIND_ROOM_BY_TAG 调用帧读取实际参数。
		/// MAP_ID 在函数体内已把默认 -1 解析为当前地图；任务类型优先取外层
		/// QST_INIT_QUEST_DATA 的实参，避免同名全局变量被清理或覆盖后误判为普通 MAP。
		/// </summary>
		private bool TryReadEraFlQuestStartLookupFromActiveFrame(
			CalledFunction called,
			out EraFlQuestStartLookupContext lookup)
		{
			lookup = default;
			VariableTerm[] arguments = called?.TopLabel?.Arg;
			if (arguments == null || arguments.Length < 4
				|| arguments[0] == null || !arguments[0].IsString
				|| arguments[1] == null || !arguments[1].IsInteger
				|| arguments[2] == null || !arguments[2].IsInteger
				|| arguments[3] == null || !arguments[3].IsInteger)
			{
				return false;
			}

			try
			{
				string requestedRoomTag = arguments[0].GetStrValue(GlobalStatic.EMediator);
				Int64 random = arguments[1].GetIntValue(GlobalStatic.EMediator);
				Int64 mapId = arguments[2].GetIntValue(GlobalStatic.EMediator);
				Int64 fromSavedata = arguments[3].GetIntValue(GlobalStatic.EMediator);
				if (mapId == -1 && !TryReadGlobalInteger("HO_有効マップID", out mapId))
					return false;

				string questType;
				if (!TryReadEraFlQuestTypeFromCaller(out questType))
					TryReadGlobalString("QST_QUEST_TYPE", out questType);

				lookup = new EraFlQuestStartLookupContext(
					requestedRoomTag,
					random,
					mapId,
					fromSavedata,
					questType);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private bool TryReadEraFlQuestTypeFromCaller(out string questType)
		{
			questType = null;
			for (int i = functionList.Count - 2; i >= 0; i--)
			{
				CalledFunction caller = functionList[i];
				if (caller == null || caller.TopLabel == null
					|| !string.Equals(caller.FunctionName, "QST_INIT_QUEST_DATA", StringComparison.Ordinal)
					|| caller.TopLabel.Arg == null || caller.TopLabel.Arg.Length < 2
					|| caller.TopLabel.Arg[1] == null || !caller.TopLabel.Arg[1].IsString)
				{
					continue;
				}

				try
				{
					questType = caller.TopLabel.Arg[1].GetStrValue(GlobalStatic.EMediator);
					return !string.IsNullOrEmpty(questType);
				}
				catch (Exception)
				{
					return false;
				}
			}
			return false;
		}

		/// <summary>
		/// eraFL 的 GMAP 脚本先把 XML 导入 DataTable，再通过 ERB 循环回填房间字典。
		/// 单地图绘制固定读取 GMAPDATA，多地图绘制读取 GMAPDATA_&lt;mapId&gt;；因此不能
		/// 只补房间字典。这里会从当前可信表复制并注册两个名字，文件读取仍由回退处理。
		/// </summary>
		private static bool TryPopulateEraFlGMapRoomData(Int64 mapId, Int64 mapCount, string[,] mapData)
		{
			if (mapData == null || mapId < 0 || mapId >= mapData.GetLength(0))
				return false;

			System.Data.DataTable table = null;
			string perMapTableName = "GMAPDATA_" + mapId.ToString();
			// 多地图加载完成后，GMAPDATA 只保留最后一次导入内容，不能拿它补其它 mapId。
			// 单地图则优先信绘图函数实际读取的全局表，不完整时才使用分表恢复。
			if (mapCount <= 1)
			{
				RuntimeDataStore.DataTables.TryGetValue("GMAPDATA", out table);
				if (!TryReadEraFlGMapNodes(table, out _))
					RuntimeDataStore.DataTables.TryGetValue(perMapTableName, out table);
			}
			else
			{
				RuntimeDataStore.DataTables.TryGetValue(perMapTableName, out table);
			}

			if (!TryReadEraFlGMapNodes(table, out IReadOnlyList<EraFlGMapNode> nodes))
			{
				return false;
			}
			return TryMaterializeEraFlGMapRuntimeData(mapId, mapData, table, nodes);
		}

		/// <summary>
		/// 校验地图绘制和房间实体化共同需要的列，并读取 Core 兼容模块消费的精简节点。
		/// id/POS_X/POS_Y 虽不写入房间字典，却是 DT_SELECT 与节点按钮定位的硬依赖。
		/// </summary>
		private static bool TryReadEraFlGMapNodes(
			System.Data.DataTable table,
			out IReadOnlyList<EraFlGMapNode> nodes)
		{
			nodes = Array.Empty<EraFlGMapNode>();
			if (table == null || table.Rows.Count == 0
				|| !table.Columns.Contains("id")
				|| !table.Columns.Contains("NODE_ID")
				|| !table.Columns.Contains("NODE_NAME")
				|| !table.Columns.Contains("POS_X")
				|| !table.Columns.Contains("POS_Y")
				|| !table.Columns.Contains("PATH_LIST"))
			{
				return false;
			}

			var parsed = new List<EraFlGMapNode>(table.Rows.Count);
			try
			{
				foreach (System.Data.DataRow row in table.Rows)
				{
					if (row == null || row["id"] == DBNull.Value || row["NODE_ID"] == DBNull.Value
						|| row["POS_X"] == DBNull.Value || row["POS_Y"] == DBNull.Value)
					{
						return false;
					}
					_ = Convert.ToInt64(row["id"]);
					_ = Convert.ToInt64(row["POS_X"]);
					_ = Convert.ToInt64(row["POS_Y"]);
					parsed.Add(new EraFlGMapNode(
						Convert.ToInt64(row["NODE_ID"]),
						row["NODE_NAME"] == DBNull.Value ? "" : Convert.ToString(row["NODE_NAME"]),
						row["PATH_LIST"] == DBNull.Value ? "" : Convert.ToString(row["PATH_LIST"])));
				}
			}
			catch (Exception)
			{
				return false;
			}

			nodes = parsed.AsReadOnly();
			return true;
		}

		/// <summary>
		/// 先准备完整的独立表副本，再原子补房间字典并发布两个 DT 名称。
		/// 两个名称不能共享同一实例，否则后续 DT_CLEAR 全局临时表会同时清空分表。
		/// </summary>
		private static bool TryMaterializeEraFlGMapRuntimeData(
			Int64 mapId,
			string[,] mapData,
			System.Data.DataTable sourceTable,
			IReadOnlyList<EraFlGMapNode> nodes)
		{
			System.Data.DataTable globalTable = null;
			System.Data.DataTable perMapTable = null;
			try
			{
				globalTable = sourceTable.Copy();
				globalTable.TableName = "GMAPDATA";
				RuntimeDataStore.NormalizeDataTable(globalTable);
				perMapTable = sourceTable.Copy();
				perMapTable.TableName = "GMAPDATA_" + mapId.ToString();
				RuntimeDataStore.NormalizeDataTable(perMapTable);
			}
			catch (Exception)
			{
				globalTable?.Dispose();
				perMapTable?.Dispose();
				return false;
			}

			if (!Program.Compatibility.EraFl.TryPopulateGMapRoomData(
				mapId,
				mapData,
				nodes))
			{
				globalTable.Dispose();
				perMapTable.Dispose();
				return false;
			}

			RuntimeDataStore.DataTables["GMAPDATA"] = globalTable;
			RuntimeDataStore.DataTables[perMapTable.TableName] = perMapTable;
			return true;
		}

		/// <summary>
		/// 仅在游戏自己的 DT 桥没有留下可用节点时回退。路径取自 eraFL 已设置的
		/// QST_GMAPDATA_PATH/FILENAME，解析规则仍由 game.erafl Core 模块负责。
		/// </summary>
		private static bool TryPopulateEraFlGMapRoomDataFromFiles(Int64 mapId, string[,] mapData)
		{
			if (mapData == null || mapId < 0 || mapId >= mapData.GetLength(0)
				|| !TryReadGlobalString("QST_GMAPDATA_PATH", 0, out string relativeDirectory)
				|| string.IsNullOrWhiteSpace(relativeDirectory)
				|| !TryReadGlobalString("QST_GMAPDATA_FILENAME", mapId, out string fileName))
			{
				return false;
			}
			if (string.IsNullOrWhiteSpace(fileName)
				&& !TryReadGlobalString("QST_GMAPDATA_FILENAME", 0, out fileName))
			{
				return false;
			}

			string xmlName = fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
				? fileName
				: fileName + ".xml";
			if (!TryResolveEraFlGameFile(System.IO.Path.Combine(relativeDirectory, xmlName), out string dataPath)
				|| !TryResolveEraFlGameFile(System.IO.Path.Combine("XML", "mapdata_schema.xml"), out string schemaPath))
			{
				return false;
			}

			try
			{
				string schemaXml = System.IO.File.ReadAllText(schemaPath, System.Text.Encoding.UTF8);
				string dataXml = System.IO.File.ReadAllText(dataPath, System.Text.Encoding.UTF8);
				if (!Program.Compatibility.EraFl.TryParseGMapDataTableFromXml(
					schemaXml,
					dataXml,
					out System.Data.DataTable table,
					out IReadOnlyList<EraFlGMapNode> nodes))
				{
					return false;
				}
				try
				{
					return TryMaterializeEraFlGMapRuntimeData(mapId, mapData, table, nodes);
				}
				finally
				{
					table.Dispose();
				}
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static bool TryResolveEraFlGameFile(string relativePath, out string fullPath)
		{
			fullPath = null;
			if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(Program.ExeDir))
				return false;
			try
			{
				string baseDirectory = System.IO.Path.GetFullPath(Program.ExeDir);
				if (!baseDirectory.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
					baseDirectory += System.IO.Path.DirectorySeparatorChar;
				string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, relativePath));
				if (!candidate.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase)
					|| !System.IO.File.Exists(candidate))
				{
					return false;
				}
				fullPath = candidate;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void CaptureEraFlQuestStartLookup(
			CalledFunction call,
			UserDefinedFunctionArgument srcArgs)
		{
			IEraFlCompatibilityPolicy eraFl = Program.Compatibility.EraFl;
			if (!eraFl.IsEnabled || call == null || srcArgs == null || call.IsEvent || call.IsJump
				|| !string.Equals(
					call.FunctionName,
					eraFl.TaskStartRoomLookupFunction,
					StringComparison.Ordinal)
				|| srcArgs.Arguments.Length < 4)
			{
				return;
			}

			Int64 mapId = srcArgs.TransporterInt[2];
			if (mapId == -1 && !TryReadGlobalInteger("HO_有効マップID", out mapId))
				return;
			TryReadGlobalString("QST_QUEST_TYPE", out string questType);

			call.EraFlQuestStartLookup = new EraFlQuestStartLookupContext(
				srcArgs.TransporterStr[0],
				srcArgs.TransporterInt[1],
				mapId,
				srcArgs.TransporterInt[3],
				questType);
		}

		private static bool TryReadGlobalInteger(string variableName, out Int64 value)
		{
			value = 0;
			VariableToken token = GlobalStatic.IdentifierDictionary?
				.GetVariableToken(variableName, null, false);
			if (token == null || !token.IsInteger || !token.IsArray1D
				|| token.IsReference || token.IsCharacterData)
			{
				return false;
			}
			value = token.GetIntValue(GlobalStatic.EMediator, new Int64[] { 0 });
			return true;
		}

		private static bool TryReadGlobalString(string variableName, out string value)
		{
			return TryReadGlobalString(variableName, 0, out value);
		}

		private static bool TryReadGlobalString(string variableName, Int64 index, out string value)
		{
			value = null;
			VariableToken token = GlobalStatic.IdentifierDictionary?
				.GetVariableToken(variableName, null, false);
			if (token == null || !token.IsString || !token.IsArray1D
				|| token.IsReference || token.IsCharacterData
				|| index < 0 || index >= token.GetLength())
			{
				return false;
			}
			value = token.GetStrValue(GlobalStatic.EMediator, new Int64[] { index });
			return true;
		}

		public void IntoFunction(CalledFunction call, UserDefinedFunctionArgument srcArgs, ExpressionMediator exm)
		{

			if (call.IsEvent)
			{
				bool isBeforeEvent = call.FunctionName == "BEFORE_THROW" || call.FunctionName == "BEFORE_ERROR";
				if (!isBeforeEvent)
				{
					foreach (CalledFunction called in functionList)
					{
						if (called.IsEvent)
							throw new CodeEE("EVENT関数の解決前にCALLEVENT命令が行われました");
					}
				}
			}
			if (Program.DebugMode)
			{
				FunctionLabelLine label = call.CurrentLabel;
				if (call.IsJump)
					console.DebugAddTraceLog("JUMP :@" + label.LabelName + ":" + label.Position.ToString() + "行目");
				else
					console.DebugAddTraceLog("CALL :@" + label.LabelName + ":" + label.Position.ToString() + "行目");
			}
            if (srcArgs != null)
            {
                //引数の値を確定させる
                srcArgs.SetTransporter(exm);
				CaptureEraFlQuestStartLookup(call, srcArgs);
            }
			ExecutionContext context = new ExecutionContext(call.TopLabel, CurrentContext);
			PushContext(context);
            if (srcArgs != null)
            {
				if (call.TopLabel.VariadicArgIndex >= 0)
				{
					VariadicArgTerm variadicArg = srcArgs.Arguments[call.TopLabel.VariadicArgIndex] as VariadicArgTerm;
					if (variadicArg != null)
					{
						VariableTerm destArg = call.TopLabel.Arg[call.TopLabel.VariadicArgIndex];
						int requiredSize = destArg.getEl1forArg + variadicArg.Count;
						bool replaced = false;
						if (destArg.Identifier.Code == VariableCode.ARG && requiredSize > context.ArgIntegers.Length)
						{
							long[] newArray = new long[requiredSize];
							Array.Copy(context.ArgIntegers, newArray, context.ArgIntegers.Length);
							context.ArgIntegers = newArray;
							replaced = true;
						}
						else if (destArg.Identifier.Code == VariableCode.ARGS && requiredSize > context.ArgStrings.Length)
						{
							string[] newArray = new string[requiredSize];
							Array.Copy(context.ArgStrings, newArray, context.ArgStrings.Length);
							context.ArgStrings = newArray;
							replaced = true;
						}
						else if (destArg.Identifier.Code == VariableCode.ARGF && requiredSize > context.ArgFloats.Length)
						{
							double[] newArray = new double[requiredSize];
							Array.Copy(context.ArgFloats, newArray, context.ArgFloats.Length);
							context.ArgFloats = newArray;
							replaced = true;
						}
						// Arg 数组引用被替换，必须推进代际使已解析缓存失效。
						if (replaced)
							ContextStackGeneration++;
					}
				}
                //プライベート変数更新
                if (call.TopLabel.hasPrivDynamicVar)
                    call.TopLabel.In();
                //更新した変数へ引数を代入
                for (int i = 0; i < call.TopLabel.Arg.Length; i++)
                {
                    if (srcArgs.Arguments[i] != null)
                    {
						if (call.TopLabel.Arg[i].Identifier.IsReference)
						{
							ReferenceToken refToken = (ReferenceToken)call.TopLabel.Arg[i].Identifier;
							if (!srcArgs.TransporterElementRef[i].IsNull)
								refToken.SetRef(srcArgs.TransporterElementRef[i]);
							else if (srcArgs.TransporterRef[i] != null)
								refToken.SetRef(srcArgs.TransporterRef[i]);
							else if (refToken.IsOut)
								refToken.SetNullRef();
						}
						else if (srcArgs.Arguments[i] is VariadicArgTerm variadic)
						{
							int baseIndex = call.TopLabel.Arg[i].getEl1forArg;
							int requiredSize = baseIndex + variadic.Count;
							if (requiredSize > call.TopLabel.Arg[i].Identifier.GetLength())
							{
								if (call.TopLabel.Arg[i].Identifier.Code == VariableCode.ARG)
									GlobalStatic.IdentifierDictionary.resizeLocalVars("ARG", call.TopLabel.LabelName, requiredSize);
								else if (call.TopLabel.Arg[i].Identifier.Code == VariableCode.ARGS)
									GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGS", call.TopLabel.LabelName, requiredSize);
								else if (call.TopLabel.Arg[i].Identifier.Code == VariableCode.ARGF)
									GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGF", call.TopLabel.LabelName, requiredSize);
							}
							call.VariadicArgCount = variadic.Count;
							for (int j = 0; j < variadic.Count; j++)
							{
								IOperandTerm value = variadic[j];
								if (value == null)
									continue;
								long[] index = new long[] { baseIndex + j };
								bool targetIsFloat = call.TopLabel.Arg[i].GetEraType() == EraType.Float;
								EraType valueType = value.GetEraType();
								if (targetIsFloat && valueType == EraType.Integer)
									call.TopLabel.Arg[i].Identifier.SetValue((double)value.GetIntValue(exm), index);
								else if (valueType == EraType.Integer)
									call.TopLabel.Arg[i].Identifier.SetValue(value.GetIntValue(exm), index);
								else if (valueType == EraType.Float)
									call.TopLabel.Arg[i].Identifier.SetValue(value.GetFloatValue(exm), index);
								else
									call.TopLabel.Arg[i].Identifier.SetValue(value.GetStrValue(exm), index);
							}
						}
                        else if (call.TopLabel.Arg[i].GetEraType() == EraType.Float)
                        {
                            if (srcArgs.Arguments[i].GetEraType() == EraType.Integer)
                                call.TopLabel.Arg[i].SetValue((double)srcArgs.TransporterInt[i], exm);
                            else
                                call.TopLabel.Arg[i].SetValue(srcArgs.TransporterFloat[i], exm);
                        }
                        else if (call.TopLabel.Arg[i].GetEraType() == EraType.Integer)
                            call.TopLabel.Arg[i].SetValue(srcArgs.TransporterInt[i], exm);
                        else
                            call.TopLabel.Arg[i].SetValue(srcArgs.TransporterStr[i], exm);
                    }
                }
            }
            else//こっちに来るのはシステムからの呼び出し=引数は存在しない関数のみ ifネストの外に出していい気もしないでもないがはてさて
            {
                //プライベート変数更新
                if (call.TopLabel.hasPrivDynamicVar)
                    call.TopLabel.In();
            }
			functionList.Add(call);
			//sequential = false;
            currentLine = call.CurrentLabel;
            lineCount++;
            //ShfitNextLine();
        }

		#region userdifinedmethod
		public bool IsFunctionMethod
		{
			get
			{
				if (functionList.Count <= currentMin)
					return false;
                return functionList[currentMin].TopLabel.IsMethod;
            }
		}

		public bool IsCurrentFunctionMethod
		{
			get
			{
				if (functionList.Count <= currentMin)
					return false;
				return functionList[functionList.Count - 1].TopLabel.IsMethod;
			}
		}

		public SingleTerm MethodReturnValue = null;

		public void ReturnF(SingleTerm ret)
		{
			//読み込み時のチェック済みのはず
			//if (!IsFunctionMethod)
			//    throw new ExeEE("ReturnFと#FUNCTIONのチェックがおかしい");
			//sequential = false;//いずれにしろ順列ではない。
			//呼び出し元はRETURNFコマンドか関数終了時のみ
			//if (functionList.Count == 0)
			//    throw new ExeEE("実行中の関数が存在しません");
			//非イベント呼び出しなので、これは起こりえない
			//else if (functionList.Count != 1)
			//    throw new ExeEE("関数が複数ある");
			if (Program.DebugMode)
			{
				console.DebugRemoveTraceLog();
			}
			//OutはGetValue側で行う
			//functionList[0].TopLabel.Out();
            currentLine = functionList[functionList.Count - 1].ReturnAddress;
			ExecutionContext context = PopContext();
			context?.Dispose();
            functionList.RemoveAt(functionList.Count - 1);
            //nextLine = null;
            MethodReturnValue = ret;
            return;
		}

		#endregion

		bool isClone = false;
        public bool IsClone { get { return isClone; } set { isClone = value; } }

		// functionListのコピーを必要とする呼び出し元が無かったのでコピーしないことにする。
		public ProcessState Clone()
		{
			ProcessState ret = new ProcessState(console);
			ret.isClone = true;
			//どうせ消すからコピー不要
			//foreach (CalledFunction func in functionList)
			//	ret.functionList.Add(func.Clone());
			ret.currentLine = this.currentLine;
            //ret.nextLine = this.nextLine;
            //ret.sequential = this.sequential;
			ret.sysStateCode = this.sysStateCode;
			ret.begintype = this.begintype;
			// 调试窗口求值会克隆 ProcessState。克隆体不执行原调用栈，但 LOCAL@FUNCNAME
			// 仍需要读取原栈上下文，否则监视表达式中的 LOCAL/ARG 会退回空数组。
			ret.savedContextStack = this.contextStack;
			//ret.MethodReturnValue = this.MethodReturnValue;
			return ret;

		}
		//public ProcessState CloneForFunctionMethod()
		//{
		//    ProcessState ret = new ProcessState(console);
		//    ret.isClone = true;

		//    //どうせ消すからコピー不要
		//    //foreach (CalledFunction func in functionList)
		//    //	ret.functionList.Add(func.Clone());
		//    ret.currentLine = this.currentLine;
		//    ret.nextLine = this.nextLine;
		//    //ret.sequential = this.sequential;
		//    ret.sysStateCode = this.sysStateCode;
		//    ret.begintype = this.begintype;
		//    //ret.MethodReturnValue = this.MethodReturnValue;
		//    return ret;
		//}
	}
}
