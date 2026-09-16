// 最小 Godot API stub：仅为 tools/diagnostics-config-smoke 编译宿主内的诊断配置源文件。
// 只覆盖这些文件实际触达的 API（GD.PushWarning / FileAccess.FileExists+Open+GetOpenError），
// 行为与真实引擎不同（FileAccess 恒为 null/不存在），因此本工程只测不经过 I/O 的纯逻辑
// （BuildToml / ExtractForeignSections / RuntimeTomlParser.Parse / Loader.BuildConfig 路径）。
// 禁止在本工程内调用 SaveUserConfig/Load 等真实 I/O 入口。
namespace Godot
{
    public static class GD
    {
        public static void PushWarning(string message) { }
    }

    // 真实 FileAccess 是实例对象（using var file = ...）；stub 用密封类 + 恒 null 工厂。
    public sealed class FileAccess : IDisposable
    {
        public enum ModeFlags { Read, Write, ReadWrite }

        FileAccess() { }

        public static bool FileExists(string path) => false;

        public static FileAccess Open(string path, ModeFlags mode) => null;

        public static Error GetOpenError() => Error.Unavailable;

        public string GetAsText() => "";

        public void StoreString(string text) { }

        public void Dispose() { }
    }

    public enum Error : int
    {
        Unavailable = 30,
    }
}

// 宿主 GenericUtils.cs 中的全局枚举（Scripts/GenericUtils.cs:14/24）；
// 值域必须与宿主一致，Loader/Config 的等级比较依赖其数值顺序。
namespace gEmuera.Diagnostics
{
    public enum EmueraLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        None = 4
    }

    [Flags]
    public enum EmueraLogCategory
    {
        None = 0,
        General = 1 << 0,
        Sprite = 1 << 1,
        Audio = 1 << 2,
        Input = 1 << 3,
        Script = 1 << 4,
        UI = 1 << 5,
        FileSystem = 1 << 6,
        Load = 1 << 7,
        Save = 1 << 8,
        Config = 1 << 9,
        Performance = 1 << 10,
        Touch = 1 << 11,
        StatementRecognition = 1 << 12,
        All = int.MaxValue
    }
}
