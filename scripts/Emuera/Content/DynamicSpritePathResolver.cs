#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera.Content
{
	/// <summary>
	/// SPRITECREATEFROMFILE 第 3 参（isRelative）的路径解析（参考
	/// Skiav17.2 Creator.Method.cs:8059-8106 SpriteCreateFromFileMethod）：
	/// 绝对路径原样使用；相对路径下 isRelative=true 走"相对路径"、false 走 ContentDir。
	/// 我方 2 参调用保持平台适配（游戏根优先、再 resources），该差异是有意保留项。
	/// 纯字符串判定，宿主与 xUnit 直链同一份源码。
	/// </summary>
	internal enum DynamicSpritePathMode
	{
		LegacyGameRootThenContent,
		GameRoot,
		ContentDir,
	}

	internal static class DynamicSpritePathResolver
	{
		/// <summary>isRelative 实参（null = 未提供，即 2 参调用）对应的解析模式。</summary>
		internal static DynamicSpritePathMode ResolveMode(bool? isRelative)
		{
			if (!isRelative.HasValue)
				return DynamicSpritePathMode.LegacyGameRootThenContent;
			return isRelative.Value ? DynamicSpritePathMode.GameRoot : DynamicSpritePathMode.ContentDir;
		}

		/// <summary>
		/// 按模式给出候选路径（按优先级排序）。空/根路径返回单一元素；调用方负责
		/// 逐个做 FileExists/ResolveExistingFilePath，并在全不存在时用最后一个候选报错。
		/// </summary>
		internal static string[] BuildCandidates(string normalizedFilepath, DynamicSpritePathMode mode, string gameRoot, string contentDir)
		{
			if (string.IsNullOrEmpty(normalizedFilepath))
				return Array.Empty<string>();
			if (Path.IsPathRooted(normalizedFilepath) || normalizedFilepath.Contains("://"))
				return new[] { normalizedFilepath };

			string contentRelative = StripResourcesPrefix(normalizedFilepath);
			switch (mode)
			{
				case DynamicSpritePathMode.GameRoot:
					return new[] { Path.Combine(gameRoot ?? "", normalizedFilepath) };
				case DynamicSpritePathMode.ContentDir:
					return new[] { Path.Combine(contentDir ?? "", contentRelative) };
				default:
					return new[]
					{
						Path.Combine(gameRoot ?? "", normalizedFilepath),
						Path.Combine(contentDir ?? "", contentRelative),
					};
			}
		}

		internal static string StripResourcesPrefix(string filepath)
		{
			const string resourcesPrefix = "resources/";
			if (filepath != null && filepath.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
				return filepath.Substring(resourcesPrefix.Length);
			return filepath;
		}
	}
}
