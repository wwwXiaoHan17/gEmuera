using System.Collections.Generic;

namespace Properties
{
    public static class ResourceManager
    {
        static Dictionary<string, string> dict = new Dictionary<string, string>
        {
            { "RuntimeErrMesMethodCIMGCreateOutOfRange0","{0}関数:画像の範囲外が指定されています"},
            { "RuntimeErrMesMethodColorARGB0","{0}関数:ColorARGB引数に不適切な値(0x{1:X8})が指定されました"},
            { "RuntimeErrMesMethodDefaultArgumentOutOfRange0","{0}関数:第{2}引数に不適切な値({1})が指定されました"},
            { "RuntimeErrMesMethodGColorMatrix0","{0}関数:ColorMatrixの指定された要素({1}, {2})が不適切であるか5x5に足りていません"},
            { "RuntimeErrMesMethodGDIPLUSOnly","{0}関数:描画オプションがWINAPIの時には使用できません"},
            { "RuntimeErrMesMethodGHeight0","{0}関数:GraphicsのHeightに0以下の値({1})が指定されました"},
            { "RuntimeErrMesMethodGHeight1","{0}関数:GraphicsのHeightに{2}以上の値({1})が指定されました"},
            { "RuntimeErrMesMethodGraphicsID0","{0}関数:GraphicsIDに負の値({1})が指定されました"},
            { "RuntimeErrMesMethodGraphicsID1","{0}関数:GraphicsIDの値({1})が大きすぎます"},
            { "RuntimeErrMesMethodGWidth0","{0}関数:GraphicsのWidthに0以下の値({1})が指定されました"},
            { "RuntimeErrMesMethodGWidth1","{0}関数:GraphicsのWidthに{2}以上の値({1})が指定されました"},
            { "SyntaxErrMesMethodDefaultArgumentNotNullable0","{0}関数:第{1}引数は省略できません"},
            { "SyntaxErrMesMethodDefaultArgumentNum0","{0}関数:引数の数が間違っています"},
            { "SyntaxErrMesMethodDefaultArgumentNum1","{0}関数:少なくとも{1}個の引数が必要です"},
            { "SyntaxErrMesMethodDefaultArgumentNum2","{0}関数:引数の数が多すぎます"},
            { "SyntaxErrMesMethodDefaultArgumentType0","{0}関数:第{1}引数の型が間違っています"},
            { "SyntaxErrMesMethodGraphicsColorMatrix0","{0}関数:ColorMatrixに5x5以上の二次元数値型配列変数でない引数が指定されました"},
            //多签名参数机制（argumentTypeArrayEx）错误消息——文案对照 v24/snake Lang.cs 的 trerror 键。
            { "SyntaxErrMesMethodArgsNotFitExpr0","{0}関数: 引数の数({1})が{2}+{3}nではありません"},
            { "SyntaxErrMesMethodArgIsNotCharacterVar0","{0}関数: 第{1}引数の変数がキャラクタ変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotStr0","{0}関数: 第{1}引数は文字列ではありません"},
            { "SyntaxErrMesMethodArgIsNotInt0","{0}関数: 第{1}引数は整数ではありません"},
            { "SyntaxErrMesMethodArgIsNotVar0","{0}関数: 第{1}引数は変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotStrVar0","{0}関数: 第{1}引数は文字列型変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotIntVar0","{0}関数: 第{1}引数は整数型変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotArray0","{0}関数: 第{1}引数は配列変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotStrArray0","{0}関数: 第{1}引数は文字列型配列変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotIntArray0","{0}関数: 第{1}引数は整数型配列変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotNDArray0","{0}関数: 第{1}引数は{2}次元配列変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotNDStrArray0","{0}関数: 第{1}引数は文字列型{2}次元配列変数ではありません"},
            { "SyntaxErrMesMethodArgIsNotNDIntArray0","{0}関数: 第{1}引数は整数型{2}次元配列変数ではありません"},
            { "SyntaxErrMesMethodTooManyFuncArgs0","{0}関数: 引数が多すぎます"},
            { "SyntaxErrMesMethodNotEnoughArgs0","{0}関数: 少なくとも{1}つの引数が必要です"},
            { "SyntaxErrMesMethodArgsCountNotMatches0","{0}関数: {1}つの引数が必要ですが，{2}つが与えられています"},
            { "SyntaxErrMesMethodArgsNotNeeded0","{0}関数: 引数の必要がありません"},
            { "SyntaxErrMesMethodNotValidArgs0","{0}関数: 引数がどの書式にも合わせていません | {1}"},
            { "SyntaxErrMesMethodNotValidArgsReason0","書式{0}: {1}"},
        };

        public static string GetString(string key)
        {
            string s;
            dict.TryGetValue(key, out s);
            return s;
        }
    }

    public static class Resources
    {

        /// <summary>
        ///   {0}関数:画像の範囲外が指定されています に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodCIMGCreateOutOfRange0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodCIMGCreateOutOfRange0");
            }
        }

        /// <summary>
        ///   {0}関数:ColorARGB引数に不適切な値(0x{1:X8})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodColorARGB0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodColorARGB0");
            }
        }

        /// <summary>
        ///   {0}関数:第{2}引数に不適切な値({1})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodDefaultArgumentOutOfRange0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodDefaultArgumentOutOfRange0");
            }
        }

        /// <summary>
        ///   {0}関数:ColorMatrixの指定された要素({1}, {2})が不適切であるか5x5に足りていません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGColorMatrix0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGColorMatrix0");
            }
        }

        /// <summary>
        ///   {0}関数:描画オプションがWINAPIの時には使用できません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGDIPLUSOnly
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGDIPLUSOnly");
            }
        }

        /// <summary>
        ///   {0}関数:GraphicsのHeightに0以下の値({1})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGHeight0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGHeight0");
            }
        }

        /// <summary>
        ///   {0}関数:GraphicsのHeightに{2}以上の値({1})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGHeight1
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGHeight1");
            }
        }

        /// <summary>
        ///   {0}関数:GraphicsIDに負の値({1})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGraphicsID0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGraphicsID0");
            }
        }

        /// <summary>
        ///   {0}関数:GraphicsIDの値({1})が大きすぎます に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGraphicsID1
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGraphicsID1");
            }
        }

        /// <summary>
        ///   {0}関数:GraphicsのWidthに0以下の値({1})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGWidth0
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGWidth0");
            }
        }

        /// <summary>
        ///   {0}関数:GraphicsのWidthに{2}以上の値({1})が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string RuntimeErrMesMethodGWidth1
        {
            get
            {
                return ResourceManager.GetString("RuntimeErrMesMethodGWidth1");
            }
        }

        /// <summary>
        ///   {0}関数:第{1}引数は省略できません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodDefaultArgumentNotNullable0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodDefaultArgumentNotNullable0");
            }
        }

        /// <summary>
        ///   {0}関数:引数の数が間違っています に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodDefaultArgumentNum0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodDefaultArgumentNum0");
            }
        }

        /// <summary>
        ///   {0}関数:少なくとも{1}個の引数が必要です に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodDefaultArgumentNum1
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodDefaultArgumentNum1");
            }
        }

        /// <summary>
        ///   {0}関数:引数の数が多すぎます に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodDefaultArgumentNum2
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodDefaultArgumentNum2");
            }
        }

        /// <summary>
        ///   {0}関数:第{1}引数の型が間違っています に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodDefaultArgumentType0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodDefaultArgumentType0");
            }
        }

        /// <summary>
        ///   {0}関数:ColorMatrixに5x5以上の二次元数値型配列変数でない引数が指定されました に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodGraphicsColorMatrix0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodGraphicsColorMatrix0");
            }
        }

        /// <summary>
        ///   {0}関数: 引数の数({1})が{2}+{3}nではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgsNotFitExpr0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgsNotFitExpr0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数の変数がキャラクタ変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotCharacterVar0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotCharacterVar0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は文字列ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotStr0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotStr0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は整数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotInt0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotInt0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotVar0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotVar0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は文字列型変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotStrVar0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotStrVar0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は整数型変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotIntVar0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotIntVar0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は配列変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotArray0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotArray0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は文字列型配列変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotStrArray0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotStrArray0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は整数型配列変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotIntArray0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotIntArray0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は{2}次元配列変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotNDArray0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotNDArray0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は文字列型{2}次元配列変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotNDStrArray0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotNDStrArray0");
            }
        }

        /// <summary>
        ///   {0}関数: 第{1}引数は整数型{2}次元配列変数ではありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgIsNotNDIntArray0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgIsNotNDIntArray0");
            }
        }

        /// <summary>
        ///   {0}関数: 引数が多すぎます に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodTooManyFuncArgs0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodTooManyFuncArgs0");
            }
        }

        /// <summary>
        ///   {0}関数: 少なくとも{1}つの引数が必要です に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodNotEnoughArgs0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodNotEnoughArgs0");
            }
        }

        /// <summary>
        ///   {0}関数: {1}つの引数が必要ですが，{2}つが与えられています に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgsCountNotMatches0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgsCountNotMatches0");
            }
        }

        /// <summary>
        ///   {0}関数: 引数の必要がありません に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodArgsNotNeeded0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodArgsNotNeeded0");
            }
        }

        /// <summary>
        ///   {0}関数: 引数がどの書式にも合わせていません | {1} に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodNotValidArgs0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodNotValidArgs0");
            }
        }

        /// <summary>
        ///   書式{0}: {1} に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SyntaxErrMesMethodNotValidArgsReason0
        {
            get
            {
                return ResourceManager.GetString("SyntaxErrMesMethodNotValidArgsReason0");
            }
        }
    }
}
