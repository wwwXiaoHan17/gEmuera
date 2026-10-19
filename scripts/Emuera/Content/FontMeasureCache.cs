using System.Collections.Generic;

namespace MinorShift.Emuera.Content
{
	/// <summary>
	/// 字体测量缓存
	/// 基于 XEmuera 的实现，使用 4096 条目缓存提升性能
	/// </summary>
	public class FontMeasureCache
	{
		private struct CacheKey
		{
			public char Char;
			public int FontSize;

			public override int GetHashCode()
			{
				// 高效的哈希计算
				return (Char << 16) | (FontSize & 0xFFFF);
			}

			public override bool Equals(object obj)
			{
				if (!(obj is CacheKey))
					return false;

				CacheKey other = (CacheKey)obj;
				return Char == other.Char && FontSize == other.FontSize;
			}
		}

		private Dictionary<CacheKey, float> cache = new Dictionary<CacheKey, float>(4096);
		private const int MaxCacheSize = 4096;

		/// <summary>
		/// 尝试从缓存获取字符宽度
		/// </summary>
		public bool TryGetWidth(char c, int fontSize, out float width)
		{
			var key = new CacheKey { Char = c, FontSize = fontSize };
			return cache.TryGetValue(key, out width);
		}

		/// <summary>
		/// 添加字符宽度到缓存
		/// </summary>
		public void Add(char c, int fontSize, float width)
		{
			// 简单的 FIFO 策略：缓存满时清空
			if (cache.Count >= MaxCacheSize)
			{
				cache.Clear();
				GenericUtils.Debug($"字体测量缓存已满，清空重建", EmueraLogCategory.Performance);
			}

			var key = new CacheKey { Char = c, FontSize = fontSize };
			cache[key] = width;
		}

		/// <summary>
		/// 获取当前缓存条目数
		/// </summary>
		public int Count => cache.Count;

		/// <summary>
		/// 清空缓存
		/// </summary>
		public void Clear()
		{
			cache.Clear();
		}
	}
}
