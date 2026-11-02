#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Emuera.Compatibility.Packs
{
	/// <summary>
	/// 兼容包版本轴（contractAbi / engineApi）的不可变值：十进制 major.minor。
	/// 比较语义只回答"包能否在引擎上运行"：major 必须相等，包 minor 不得高于引擎 minor
	/// （包可能用到引擎缺失的增量契约/能力；引擎更新不会让旧包失效）。
	/// 解析严格按 ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$：不接受前导零、不接受 x.y.z、
	/// 不做隐式 Trim——清单字段是精确契约，同一版本不接受多种写法。
	/// </summary>
	public readonly record struct CompatVersion(int Major, int Minor)
	{
		static readonly Regex Pattern = new(
			"^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

		/// <summary>解析 "major.minor"。null/空串/前导零/三段式/负数/溢出 Int32 均返回 false。</summary>
		public static bool TryParse(string? value, out CompatVersion version)
		{
			version = default;
			if (string.IsNullOrEmpty(value) || !Pattern.IsMatch(value))
				return false;

			int separator = value.IndexOf('.');
			// 数值超出 Int32 即不合法：版本号是 int，截断会静默改变版本语义。
			if (!int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int major)
				|| !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
			{
				return false;
			}

			version = new CompatVersion(major, minor);
			return true;
		}

		/// <summary>本版本能否运行在 <paramref name="engineVersion"/> 上：major 相等且 minor 不高于引擎。</summary>
		public bool CanRunOn(CompatVersion engineVersion)
			=> Major == engineVersion.Major && Minor <= engineVersion.Minor;

		/// <summary>规范形态 "major.minor"（manifest 字段与诊断文案共用）。</summary>
		public override string ToString() => Major + "." + Minor;
	}
}
