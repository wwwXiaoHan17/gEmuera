using System;
using System.Collections.Generic;
using System.Text;

namespace MinorShift.Emuera.GameProc
{
	enum InputType
	{
		EnterKey = 1,//Enterキーかクリック
		AnyKey = 2,//なんでもいいから入力
		IntValue = 3,//整数値。OneInputかどうかは別の変数で
		StrValue = 4,//文字列。
		Void = 5,//入力不能。待つしかない→スキップ中orマクロ中ならなかったことになる
		AnyValue = 6,//数値or文字列
		IntButton = 7,
		StrButton = 8,

		//1823
		PrimitiveMouseKey = 11,

	}
	

	// 1819追加 入力・表示系とData、Process系の結合を弱くしよう計画の一つ
	// できるだけ間にクッションをおいていきたい。最終的には別スレッドに

	//クラスを毎回使い捨てるのはどうなんだろう 使いまわすべきか
	internal sealed class InputRequest
	{
		public InputRequest()
		{
			ID = LastRequestID++;
		}
		public readonly Int64 ID;
		public InputType InputType;
		public bool NeedValue
		{ 
			get 
			{ 
				return (InputType == InputType.IntValue || InputType == InputType.StrValue
					|| InputType == InputType.IntButton || InputType == InputType.StrButton
					|| InputType == InputType.AnyValue || InputType == InputType.PrimitiveMouseKey); 
			} 
		}
		public bool OneInput = false;
		public bool NoFocus = false;
		public bool StopMesskip = false;
		public bool IsSystemInput = false;
		// eraFL 的 INPUTS ,1：真实指针提交时同时提供 RESULTS:0/1，并隐藏内部按钮值回显。
		public bool EnablePointerInputMetadata = false;

		public bool HasDefValue = false;
		public long DefIntValue;
		public string DefStrValue;

		// EE_INPUT 機能拡張：TINPUT 第5参数（鼠标输入开关）。
		public bool MouseInput = false;

		public long Timelimit = -1;
		public bool DisplayTime;
		public string TimeUpMes;

		static Int64 LastRequestID = 0;
	}
}
