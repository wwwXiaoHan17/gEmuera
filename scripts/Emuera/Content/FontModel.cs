using SkiaSharp;
using System;
using System.IO;
using Godot;

namespace MinorShift.Emuera.Content
{
	/// <summary>
	/// 字体管理系统
	/// 负责加载和管理内嵌字体，基于 XEmuera 的实现
	/// 方案2：首次运行时从 res:// 复制到 user://，然后从 user:// 加载
	/// </summary>
	public static class FontModel
	{
		private const string DefaultFontSourcePath = "res://assets/fonts/MS Gothic.ttf";
		private const string FallbackFontSourcePath = "res://assets/fonts/Microsoft YaHei.ttf";
		private const string DefaultFontUserPath = "user://MS_Gothic.ttf";
		private const string FallbackFontUserPath = "user://Microsoft_YaHei.ttf";

		private static SKTypeface defaultTypeface;
		private static SKTypeface fallbackTypeface;
		private static bool initialized = false;

		/// <summary>
		/// 初始化字体系统
		/// 方案2：从 res:// 复制到 user://，然后加载
		/// 优点：Android 可以访问 user:// 路径，绕过 PCK 限制
		/// </summary>
		public static void Initialize()
		{
			if (initialized)
				return;

			try
			{
				GenericUtils.Info("FontModel 初始化开始（方案2: res:// → user://）", EmueraLogCategory.Load);

				// 确保字体文件在 user:// 目录
				EnsureFontFilesInUserDir();

				// 从 user:// 加载字体
				defaultTypeface = LoadFontFromUserDir(DefaultFontUserPath, "MS Gothic");
				fallbackTypeface = LoadFontFromUserDir(FallbackFontUserPath, "Microsoft YaHei");

				initialized = true;

				if (defaultTypeface != null)
					GenericUtils.Info("字体测量已启用（MS Gothic 从 user:// 加载）", EmueraLogCategory.Load);
				else
					GenericUtils.Warn("字体测量将使用系统默认字体", EmueraLogCategory.Load);
			}
			catch (Exception ex)
			{
				GenericUtils.Error($"FontModel 初始化失败: {ex.Message}", EmueraLogCategory.Load);
				GenericUtils.Error($"堆栈跟踪: {ex.StackTrace}", EmueraLogCategory.Load);

				// 失败时标记为已初始化，但字体为 null
				// StringMeasure 会检测并降级到规则判断
				defaultTypeface = null;
				fallbackTypeface = null;
				initialized = true;

				GenericUtils.Warn("字体加载失败，降级到规则判断方案", EmueraLogCategory.Load);
			}
		}

		/// <summary>
		/// 确保字体文件存在于 user:// 目录
		/// 如果不存在，从 res:// 复制
		/// </summary>
		private static void EnsureFontFilesInUserDir()
		{
			EnsureFontFile(DefaultFontSourcePath, DefaultFontUserPath, "MS Gothic");
			EnsureFontFile(FallbackFontSourcePath, FallbackFontUserPath, "Microsoft YaHei");
		}

		/// <summary>
		/// 确保单个字体文件存在
		/// </summary>
		private static void EnsureFontFile(string sourcePath, string userPath, string displayName)
		{
			try
			{
				// 检查 user:// 是否已存在
				if (Godot.FileAccess.FileExists(userPath))
				{
					GenericUtils.Debug($"字体已存在: {userPath}", EmueraLogCategory.Load);
					return;
				}

				GenericUtils.Info($"正在复制字体: {displayName} ({sourcePath} → {userPath})", EmueraLogCategory.Load);

				// 从 res:// 读取
				using (var source = Godot.FileAccess.Open(sourcePath, Godot.FileAccess.ModeFlags.Read))
				{
					if (source == null)
					{
						var error = Godot.FileAccess.GetOpenError();
						GenericUtils.Warn($"无法打开源字体文件: {sourcePath}, 错误: {error}", EmueraLogCategory.Load);
						return;
					}

					byte[] fontData = source.GetBuffer((long)source.GetLength());
					GenericUtils.Debug($"读取字体数据: {fontData.Length} 字节", EmueraLogCategory.Load);

					// 写入 user://
					using (var dest = Godot.FileAccess.Open(userPath, Godot.FileAccess.ModeFlags.Write))
					{
						if (dest == null)
						{
							var error = Godot.FileAccess.GetOpenError();
							GenericUtils.Warn($"无法创建目标字体文件: {userPath}, 错误: {error}", EmueraLogCategory.Load);
							return;
						}

						dest.StoreBuffer(fontData);
						GenericUtils.Info($"✓ 字体复制成功: {displayName} → {userPath}", EmueraLogCategory.Load);
					}
				}
			}
			catch (Exception ex)
			{
				GenericUtils.Warn($"复制字体文件失败 ({displayName}): {ex.Message}", EmueraLogCategory.Load);
			}
		}

		/// <summary>
		/// 从 user:// 目录加载字体
		/// </summary>
		private static SKTypeface LoadFontFromUserDir(string userPath, string displayName)
		{
			try
			{
				if (!Godot.FileAccess.FileExists(userPath))
				{
					GenericUtils.Warn($"字体文件不存在: {userPath}", EmueraLogCategory.Load);
					return null;
				}

				// 转换为系统绝对路径
				string absolutePath = ProjectSettings.GlobalizePath(userPath);
				GenericUtils.Debug($"正在加载字体: {absolutePath}", EmueraLogCategory.Load);

				// 使用 System.IO.File 读取（绕过 Godot FileAccess 限制）
				byte[] fontData = File.ReadAllBytes(absolutePath);
				GenericUtils.Debug($"字体数据大小: {fontData.Length} 字节", EmueraLogCategory.Load);

				// 从字节数组创建 SKTypeface
				using (var stream = new MemoryStream(fontData))
				{
					var typeface = SKTypeface.FromStream(stream);

					if (typeface == null)
					{
						GenericUtils.Warn($"SKTypeface.FromStream 返回 null: {displayName}", EmueraLogCategory.Load);
						return null;
					}

					GenericUtils.Info($"✓ 字体加载成功: {displayName} (FamilyName={typeface.FamilyName})", EmueraLogCategory.Load);
					return typeface;
				}
			}
			catch (Exception ex)
			{
				GenericUtils.Warn($"加载字体失败 ({displayName}): {ex.Message}", EmueraLogCategory.Load);
				return null;
			}
		}

		/// <summary>
		/// 获取默认字体（MS Gothic）
		/// </summary>
		public static SKTypeface GetDefaultTypeface()
		{
			if (!initialized)
				Initialize();
			return defaultTypeface;
		}

		/// <summary>
		/// 获取后备字体（Microsoft YaHei）
		/// </summary>
		public static SKTypeface GetFallbackTypeface()
		{
			if (!initialized)
				Initialize();
			return fallbackTypeface;
		}

		/// <summary>
		/// 检查字体系统是否已初始化
		/// </summary>
		public static bool IsInitialized => initialized;
	}
}
