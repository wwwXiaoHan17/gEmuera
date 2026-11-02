namespace CompatPackTypeLoadDependency;

/// <summary>
/// 类型加载失败夹具的"坏依赖"：被测包入口继承本类型。部署目录里该依赖缺失或字节损坏时，
/// Assembly.GetTypes 无法解析入口类型的基类，加载器必须按 fail-closed 拒载
///（不得把部分类型枚举当成"零入口数据包"降级）。本类型只被夹具工程引用。
/// </summary>
public abstract class TypeLoadDependencyBase
{
    public string DependencyMarker => "compatpack.type-load-dependency";
}
