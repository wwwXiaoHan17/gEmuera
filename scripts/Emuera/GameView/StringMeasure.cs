using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.Sub;
using MinorShift._Library;
using uEmuera.Drawing;
using SkiaSharp;
using MinorShift.Emuera.Content;

namespace MinorShift.Emuera.GameView
{
	/// <summary>
	/// 文本宽度测量
	/// 采用 XEmuera 方案：基于 SkiaSharp 字体测量 + 缓存优化
	/// </summary>
	internal sealed class StringMeasure : IDisposable
	{
		// PERFORMANCE: 字体测量缓存，基于 XEmuera 实现
		private static FontMeasureCache measureCache = new FontMeasureCache();
		private static bool useFontMeasurement = true; // 启用字体测量（使用 C# 嵌入资源）

		readonly TextDrawingMode textDrawingMode;

		public StringMeasure()
		{
			textDrawingMode = Config.TextDrawingMode;

			// 初始化字体系统（使用 C# 嵌入资源，兼容 Android）
			try
			{
				GenericUtils.Info("StringMeasure 初始化开始", EmueraLogCategory.Load);

				if (!FontModel.IsInitialized)
				{
					FontModel.Initialize();
				}

				// 验证字体是否真正可用
				var testTypeface = FontModel.GetDefaultTypeface();
				if (testTypeface == null)
				{
					GenericUtils.Warn("字体初始化返回 null，降级到规则判断", EmueraLogCategory.Load);
					useFontMeasurement = false;
				}
				else
				{
					GenericUtils.Info($"字体测量已启用 (FamilyName={testTypeface.FamilyName})", EmueraLogCategory.Load);
					useFontMeasurement = true;
				}
			}
			catch (Exception ex)
			{
				GenericUtils.Error($"StringMeasure 初始化字体失败: {ex.Message}", EmueraLogCategory.Load);
				GenericUtils.Error($"堆栈跟踪: {ex.StackTrace}", EmueraLogCategory.Load);
				// 禁用字体测量，降级到规则判断
				useFontMeasurement = false;
				GenericUtils.Warn("已禁用字体测量，使用规则判断方案", EmueraLogCategory.Load);
			}
		}

		/// <summary>
		/// 获取字符串显示长度（像素）
		/// 优先使用字体测量，失败时降级到规则判断
		/// </summary>
		public int GetDisplayLength(string s, Font font)
		{
			if (string.IsNullOrEmpty(s))
				return 0;

			if (s.IndexOf('\t') >= 0)
			{
				// eraFL 的 TAG_PRINT 多行字符串会把源码缩进 tab 带进按钮片段。
				// 原生绘制不会把这些缩进扩成固定 8 个空格；这里改用固定网格规则，
				// 让测量宽度与 Godot 侧逐格绘制保持一致，避免按钮被撑宽后换行散开。
				return GetDisplayLengthByRules(s, font);
			}

			// 如果字体测量未启用或初始化失败，降级到规则判断
			if (!useFontMeasurement || !FontModel.IsInitialized)
			{
				return GetDisplayLengthByRules(s, font);
			}

			try
			{
				return GetDisplayLengthByFont(s, (int)font.Size);
			}
			catch (Exception ex)
			{
				// 字体测量失败，降级到规则判断
				GenericUtils.Warn($"字体测量异常，降级到规则判断: {ex.Message}", EmueraLogCategory.Performance);
				return GetDisplayLengthByRules(s, font);
			}
		}

		/// <summary>
		/// 基于字体测量的长度计算（XEmuera 方案）
		/// </summary>
		private int GetDisplayLengthByFont(string s, int fontSize)
		{
			var typeface = FontModel.GetDefaultTypeface();

			if (typeface == null)
			{
				throw new Exception("字体 typeface 为 null");
			}

			using (var paint = new SKPaint())
			{
				paint.Typeface = typeface;
				paint.TextSize = fontSize;
				paint.IsAntialias = true;

				float totalWidth = 0;

				foreach (char c in s)
				{
					// 尝试从缓存获取
					if (!measureCache.TryGetWidth(c, fontSize, out float width))
					{
						// 缓存未命中，进行测量
						width = paint.MeasureText(c.ToString());
						measureCache.Add(c, fontSize, width);
					}

					totalWidth += width;
				}

				return (int)Math.Round(totalWidth);
			}
		}

		/// <summary>
		/// 基于规则判断的长度计算（降级方案）
		/// </summary>
		private int GetDisplayLengthByRules(string s, Font font)
		{
			// Godot 的真实字形测量会随平台字体 fallback 改变；控制台布局必须与
			// STRLEN/半角全角单元格一致，否则中文地图和箱线字符在 Android 上列错位。
			return uEmuera.Utils.GetDisplayLength(s, font);
		}

		public void Dispose()
		{
			// 字体资源由 FontModel 统一管理，这里无需释放
		}
	}
}
