using System;
using System.Collections.Generic;

namespace MinorShift.Emuera
{
	/// <summary>
	/// ConfigData 的检查器（Inspector）部分：只给游戏内设置面板（OptionWindow 的
	/// 「高级：emuera.config」调试区）提供最小、受控的只读/编辑入口。
	///
	/// 设计约束（故意保持很小）：
	///   1. 不把 configArray / replaceArray / debugArray 暴露给调用方，面板只能拿到
	///      顺序快照与单值读/写方法，避免绕过 Fixed 语义或改动数组容量；
	///   2. 语义沿用原有配置文件读取逻辑：文本是否合法由 AConfigItem.TryParse 决定，
	///      Fixed 项（来自 fixed.config）一律拒绝写入；写入后必须由 UI 显式调用 Save()
	///      才落盘，本文件自身不产生任何文件写入；
	///   3. 线程：本文件所有成员都只在 Godot 主线程（UI）调用。解释器线程可能同时
	///      读取静态 Config 字段，所以运行期修改配置属于调试行为，不承诺运行中的系统
	///      立即全部使用新值（部分配置项需重新开始游戏才生效）。
	/// </summary>
	internal sealed partial class ConfigData
	{
		/// <summary>
		/// 按 configArray 的原始顺序返回所有非 null 配置项的只读快照。
		/// 快照固定“有哪些项、什么顺序”，项内值仍是 ConfigData 的实时值（需要最新值
		/// 请再次调用；不要跨 LoadConfig/ReLoadConfig 缓存返回的列表）。仅主线程调用。
		/// </summary>
		internal IReadOnlyList<AConfigItem> GetInspectorItems()
		{
			var items = new List<AConfigItem>();
			for (int i = 0; i < configArray.Length; i++)
			{
				AConfigItem item = configArray[i];
				if (item != null)
					items.Add(item);
			}
			return items.AsReadOnly();
		}

		/// <summary>
		/// 该配置项是否为布尔开关（决定面板用 CheckButton 还是文本输入框展示）。
		/// 仅主线程调用。
		/// </summary>
		internal bool IsBooleanItem(AConfigItem item)
		{
			return item is ConfigItem<bool>;
		}

		/// <summary>
		/// 读取布尔配置项的当前值。item 为 null 或不是布尔项时返回 false，且 value 置为 false。
		/// 仅主线程调用（与 UI 读取同一份 ConfigData 值）。
		/// </summary>
		internal bool TryGetBoolean(AConfigItem item, out bool value)
		{
			value = false;
			ConfigItem<bool> boolItem = item as ConfigItem<bool>;
			if (boolItem == null)
				return false;
			value = boolItem.Value;
			return true;
		}

		/// <summary>
		/// 用 emuera.config 的文本形式写入一个配置项（仅供面板单值编辑使用）。
		/// 语义与从 emuera.config 读取时一致：Fixed 项拒绝写入并返回 false；其余交给
		/// AConfigItem.TryParse 判定，解析失败（含它内部抛出的 CodeEE）返回 false。
		/// 成功后只改内存值，不写文件——调用方需在用户显式操作后自行调用 Save()。
		/// 仅主线程调用。
		/// </summary>
		internal bool TrySetItemFromText(AConfigItem item, string text)
		{
			if (item == null || item.Fixed)
				return false;
			try
			{
				return item.TryParse(text);
			}
			catch (Exception)
			{
				// TryParse 对非法数字/颜色/枚举会抛 CodeEE；面板按“解析失败”提示即可，
				// 不让异常穿过 UI 事件回调。
				return false;
			}
		}

		/// <summary>
		/// 返回配置项当前值的文本形式（即 emuera.config 的写法：布尔为 YES/NO，
		/// 颜色为 R,G,B）。item 为 null、ValueToString 抛异常或返回 null 时返回
		/// string.Empty，保证面板不会因为个别项异常而崩溃。仅主线程调用。
		/// </summary>
		internal string DescribeItemValue(AConfigItem item)
		{
			if (item == null)
				return string.Empty;
			try
			{
				return item.ValueToString() ?? string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}

		/// <summary>
		/// 把当前 configArray 全量写回 Program.ExeDir + "emuera.config"（沿用 SaveConfig
		/// 的既有行为：Fixed 项也会写出，读取时仍以 fixed.config 为准）。
		/// 只在用户显式操作（开关/回车/重载按钮）后由 UI 调用；失败返回 false，不抛异常。
		/// 仅主线程调用。
		/// </summary>
		internal bool Save()
		{
			try
			{
				return SaveConfig();
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// 重新读取 _default.config / emuera.config / _fixed.config，并刷新静态 Config。
		/// ReLoadConfig 内部即 LoadConfig + Config.SetConfig，这里不重复调用 SetConfig。
		/// 运行中的部分系统可能已经持有旧值或缓存，需重新开始游戏才完全生效。
		/// 仅主线程调用。
		/// </summary>
		internal bool Reload()
		{
			try
			{
				return ReLoadConfig();
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
