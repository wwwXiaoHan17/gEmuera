using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using uEmuera.Drawing;

namespace MinorShift.Emuera.Content
{
	static class AppContents
	{
		static AppContents()
		{
			gList = new Dictionary<int, GraphicsImage>();
		}
		static readonly Dictionary<string, AContentFile> resourceDic = new Dictionary<string, AContentFile>();
		static readonly Dictionary<string, ASprite> imageDictionary = new Dictionary<string, ASprite>(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<string, LazySpriteDefinition> lazyImageDictionary = new Dictionary<string, LazySpriteDefinition>(StringComparer.OrdinalIgnoreCase);
		static readonly HashSet<string> csvSpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<string, Point> spriteBasePositions = new Dictionary<string, Point>();
		static readonly Dictionary<string, string> resolvedExistingResourcePathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<int, GraphicsImage> gList;
		const int LazySpriteSlowRealizeThresholdMs = 50;

		private sealed class LazySpriteDefinition
		{
			public string RawCsvLine;
			public string SpriteName;
			public string Directory;
			public ScriptPosition Position;
			public bool IsAnime;
			public bool IsOptional;
			public List<LazySpriteDefinition> Frames;
		}

		//static public T GetContent<T>(string name)where T :AContentItem
		//{
		//	if (name == null)
		//		return null;
		//	name = name.ToUpper();
		//	if (!itemDic.ContainsKey(name))
		//		return null;
		//	return itemDic[name] as T;
		//}
		static public GraphicsImage GetGraphics(int i)
		{
            GraphicsImage gi;
            gList.TryGetValue(i, out gi);
            if(gi != null)
				return gi;
			GraphicsImage g =  new GraphicsImage(i);
			gList[i] = g;
			return g;
		}

		static public ASprite GetSprite(string name)
		{
			if (name == null)
				return null;
			ASprite result = null;
			if (imageDictionary.TryGetValue(name, out result))
				return result;
			if (lazyImageDictionary.TryGetValue(name, out var definition))
			{
				result = RealizeLazySprite(name, definition);
				if (result != null)
					return result;
			}
			if (name.StartsWith("CUTIN", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(5), out int graphicsId))
			{
				GraphicsImage g;
				if (gList.TryGetValue(graphicsId, out g) && g != null && g.IsCreated)
				{
					result = new SpriteG(name, g, new Rectangle(0, 0, g.Width, g.Height));
					imageDictionary[name] = result;
				}
			}
			return result;
		}
		static public bool SpriteExists(string name)
		{
			if (name == null)
				return false;
			if (imageDictionary.TryGetValue(name, out var existing))
				return existing != null && existing.IsCreated;
			if (lazyImageDictionary.TryGetValue(name, out var definition))
			{
				// 企业级说明：懒加载索引只能证明 CSV 中声明过资源，不能证明 Android 外部目录中的实际文件可读。
				// SPRITECREATED 会被脚本用于决定是否输出 <img>，因此这里必须完成一次轻量实体化校验，
				// 避免把缺失/路径大小写不匹配的资源当成存在，最终在手机端只生成空白 div。
				ASprite realized = RealizeLazySprite(name, definition);
				if (realized != null && realized.IsCreated)
					return true;
				lazyImageDictionary.Remove(name);
				return false;
			}
			if (name.StartsWith("CUTIN", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(5), out int graphicsId))
				return gList.TryGetValue(graphicsId, out var g) && g != null && g.IsCreated;
			return false;
		}
		static public bool IsCsvSpriteName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return false;
			// csvSpriteNames 已是 OrdinalIgnoreCase，无需 ToUpper。
			return csvSpriteNames.Contains(name.Trim());
		}
		static void RegisterCsvSpriteName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return;
			csvSpriteNames.Add(name.Trim().ToUpper());
		}

		static public void SetSpriteBasePosition(string name, Point position)
		{
			if (name == null)
				return;
			name = name.ToUpper();
			spriteBasePositions[name] = position;
		}

		static public bool TryGetSpriteBasePosition(string name, out Point position)
		{
			position = Point.Empty;
			if (name == null)
				return false;
			name = name.ToUpper();
			return spriteBasePositions.TryGetValue(name, out position);
		}

		static public void SpriteDispose(string name)
		{
			if (name == null)
				return;
			name = name.ToUpper();

            ASprite sprite = null;
            if(imageDictionary.TryGetValue(name, out sprite))
            {
                sprite.Dispose();
                imageDictionary.Remove(name);
            }
			spriteBasePositions.Remove(name);
		}

		static public long SpriteDisposeAll(bool delCsvImage)
		{
			// delCsvImage=false 是 emuera 约定的“只清动态创建的 sprite”。eraFL 会在进出事件时调用
			// SPRITEDISPOSEALL 0；如果连 CSV sprite 一起清掉，后续 BG01/立绘名会掉进裸文件同名搜索。
			var removeNames = imageDictionary.Keys
				.Where(name => delCsvImage || !csvSpriteNames.Contains(name))
				.ToList();
			long count = removeNames.Count;
			foreach (string name in removeNames)
			{
				var sprite = imageDictionary[name];
				sprite.Dispose();
				imageDictionary.Remove(name);
				spriteBasePositions.Remove(name);
			}
			if (delCsvImage)
				spriteBasePositions.Clear();
			return count;
		}

		static public bool CreateSpriteFromFileDynamic(string imgName, string filepath)
		{
			if (string.IsNullOrEmpty(imgName) || string.IsNullOrEmpty(filepath))
				return false;
			string resolvedFilepath = ResolveDynamicSpriteFilePath(filepath);
			if (!uEmuera.Utils.FileExists(resolvedFilepath))
				return false;
			imgName = imgName.ToUpper();
			if (imageDictionary.ContainsKey(imgName))
				return false;

			BitmapTexture bmp = new BitmapTexture(resolvedFilepath);
			if (bmp.Width <= 0 || bmp.Height <= 0)
				return false;
			ConstImage img = new ConstImage(imgName + "_DYN");
			img.CreateFrom(bmp, false);
			if (!img.IsCreated)
				return false;
			imageDictionary[imgName] = new SpriteF(imgName, img, new Rectangle(0, 0, bmp.Width, bmp.Height), Point.Empty);
			return true;
		}

		static string ResolveDynamicSpriteFilePath(string filepath)
		{
			// 企业级说明：SPRITECREATEFROMFILE 的相对路径按 emuera 约定以游戏目录为基准。
			// eraFL 等脚本会传 portrait/prt_FIX/*.webp，而真实资源在 resources 下；因此先查游戏根目录，
			// 再查 resources 目录，同时避免把显式 resources/ 前缀拼成 resources/resources。
			string resolved = uEmuera.Utils.NormalizePath(filepath.Trim());
			if (Path.IsPathRooted(resolved) || resolved.Contains("://"))
				return uEmuera.Utils.ResolveExistingFilePath(resolved);

			string gameCandidate = Path.Combine(Program.ExeDir ?? "", resolved);
			string gameResolved = uEmuera.Utils.ResolveExistingFilePath(gameCandidate);
			if (uEmuera.Utils.FileExists(gameResolved))
				return gameResolved;

			const string resourcesPrefix = "resources/";
			string contentRelative = resolved.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase)
				? resolved.Substring(resourcesPrefix.Length)
				: resolved;
			resolved = Path.Combine(Program.ContentDir ?? "", contentRelative);
			return uEmuera.Utils.ResolveExistingFilePath(resolved);
		}

		static public void CreateSpriteG(string imgName, GraphicsImage parent,Rectangle rect)
		{
			if (string.IsNullOrEmpty(imgName))
				throw new ArgumentOutOfRangeException();
			imgName = imgName.ToUpper();
			SpriteG newCImg = new SpriteG(imgName, parent, rect);
			imageDictionary[imgName] = newCImg;
		}

		internal static void CreateSpriteAnime(string imgName, int w, int h)
		{
			if (string.IsNullOrEmpty(imgName))
				throw new ArgumentOutOfRangeException();
			imgName = imgName.ToUpper();
			SpriteAnime newCImg = new SpriteAnime(imgName, new Size(w, h));
			imageDictionary[imgName] = newCImg;
		}
		static public bool LoadContents()
		{
			resolvedExistingResourcePathCache.Clear();
			csvSpriteNames.Clear();
			if (!uEmuera.Utils.DirectoryExists(Program.ContentDir))
				return true;
			try
			{
				List<string> csvFiles = uEmuera.Utils.GetFilePaths(Program.ContentDir, "*.csv", SearchOption.AllDirectories);
                if (UseLazyResourceIndex)
                {
                    BuildLazyResourceIndex(csvFiles);
                    return true;
                }
                var count = csvFiles.Count;
                for(var i=0; i<count; ++i)
				{
                    var filepath = csvFiles[i];
					SpriteAnime currentAnime = null;
					string directory = Path.GetDirectoryName(filepath) + "/";
					string filename = Path.GetFileName(filepath);
                    //string[] lines = File.ReadAllLines(filepath, Config.Encode);
                    string[] lines = uEmuera.Utils.GetResourceCSVLines(filepath, Config.Encode);
					int lineNo = 0;
                    var linecount = lines.Length;
                    int loadedCount = 0;
                    for (var l=0; l<linecount; ++l)
					{
                        var line = lines[l];
						lineNo++;
						if (line.Length == 0)
							continue;
						string str = NormalizeResourceCsvLine(line, directory);
						if (str.Length == 0 || str.StartsWith(";"))
							continue;
						string[] tokens = SplitRawCsvLine(str);
						//AContentItem item = CreateFromCsv(tokens);
						ScriptPosition sp = new ScriptPosition(filename, lineNo);
						ASprite item = CreateFromCsv(tokens, directory, currentAnime, sp) as ASprite;
						if (item != null)
						{
							currentAnime = item as SpriteAnime;
							if (!imageDictionary.ContainsKey(item.Name))
                            {
								imageDictionary.Add(item.Name, item);
								RegisterCsvSpriteName(item.Name);
                                loadedCount++;
                            }
							else
							{
								ParserMediator.Warn("同名のリソースが既に作成されています: " + item.Name, sp, 0);
								RegisterCsvSpriteName(item.Name);
								item.Dispose();
							}
						}
					}
				}
			}
			catch(Exception )
			{
				return false;
				//throw new CodeEE("リソースファイルのロード中にエラーが発生しました");
			}
			return true;
		}

		static public void UnloadContents()
		{
            var iter = resourceDic.Values.GetEnumerator();
            while(iter.MoveNext())
				iter.Current.Dispose();
			resourceDic.Clear();
			var sprites = imageDictionary.Values.GetEnumerator();
			while (sprites.MoveNext())
				sprites.Current.Dispose();
			imageDictionary.Clear();
			lazyImageDictionary.Clear();
			csvSpriteNames.Clear();
			spriteBasePositions.Clear();
			resolvedExistingResourcePathCache.Clear();
			foreach (var graph in gList.Values)
				graph.GDispose();
			gList.Clear();
		}

		//タイトルに戻る時用。コードの変更はないので、動的に作られた分だけ削除する。
		static public void UnloadGraphicList()
		{
			foreach (var graph in gList.Values)
				graph.GDispose();
			gList.Clear();
		}

		/// <summary>
		/// resourcesフォルダ中のcsvの1行を読んで新しいリソースを作る。
		/// 既存のアニメーションスプライトに対しては1フレーム追加する。
		/// </summary>
		/// <param name="tokens"></param>
		/// <param name="dir"></param>
		/// <param name="currentAnime"></param>
		/// <param name="sp"></param>
		/// <returns></returns>
		static private AContentItem CreateFromCsv(string[] tokens, string dir, SpriteAnime currentAnime, ScriptPosition sp)
		{
			if(tokens.Length < 2)
				return null;
			string name = tokens[0].Trim().ToUpper();//
			// 参考侧第 2 列不 Trim（带空格的文件名按原文匹配，自然因文件不存在而跳过）
			string arg2 = tokens[1];
			if (name.Length == 0 || arg2.Length == 0)
				return null;
			// アニメーションスプライト宣言
			if (arg2.Equals("ANIME", StringComparison.OrdinalIgnoreCase))
			{
				if (tokens.Length < 4)
				{
					ParserMediator.Warn("ANIME sprite size is not defined", sp, 1);
					return null;
				}
				//w,h
				int[] sizeValue = new int[2];
				bool sccs = true;
				for (int i = 0; i < 2; i++)
					sccs &= int.TryParse(tokens[i + 2], out sizeValue[i]);
				if (!sccs || sizeValue[0] <= 0 || sizeValue[1] <= 0 || sizeValue[0] > AbstractImage.MAX_IMAGESIZE || sizeValue[1] > AbstractImage.MAX_IMAGESIZE)
				{
					ParserMediator.Warn("ANIME sprite size is invalid", sp, 1);
					return null;
				}
				SpriteAnime anime = new SpriteAnime(name, new Size(sizeValue[0],sizeValue[1]));
				return anime;
			}
			// アニメ宣言以外。アニメ用フレームを含む。

			if(arg2.IndexOf('.') < 0)
			{
				ParserMediator.Warn("第2引数に拡張子がありません: " + arg2, sp, 1);
				return null;
			}
			string parentName = dir + arg2;

			// 親画像のロード ConstImage
			if (!resourceDic.ContainsKey(parentName))
			{
				if (!TryResolveExistingResourcePath(parentName, out string filepath))
				{
					ParserMediator.Warn("指定された画像ファイルが見つかりません: " + arg2, sp, 1);
					return null;
				}
				// BitmapTexture only reads image dimensions here. Actual decoding is lazy so
				// Android exports do not load every CSV-referenced texture at startup.
				BitmapTexture bmp = new BitmapTexture(filepath);
                bmp.name = name;
				if (bmp.Width <= 0 || bmp.Height <= 0)
				{
					// 企业级说明：文件存在不等于图片资源可用，尤其在 Android 外部存储路径、大小写回退或格式兼容异常时，
					// 若继续注册 0x0 基础图，SPRITECREATED 会误判成功并让 HTML 层生成空白图片占位。
					ParserMediator.Warn("指定された画像ファイルのサイズを取得できません: " + arg2, sp, 1);
					return null;
				}
				if (bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
				{
					// 1824-2: 8192px以上の画像を使うバリアントがあるため、警告しつつ許容する。
					//	bmp.Dispose();
					ParserMediator.Warn("指定された画像ファイルのサイズが大きすぎます(幅と高さは" + AbstractImage.MAX_IMAGESIZE.ToString() + "以下を推奨): " + arg2, sp, 1);
					//return null;
				}
				ConstImage img = new ConstImage(parentName);
				img.CreateFrom(bmp, Config.TextDrawingMode == TextDrawingMode.WINAPI);
				if (!img.IsCreated)
				{
					ParserMediator.Warn("画像リソースの作成に失敗しました: " + arg2, sp, 1);
					return null;
				}
				resourceDic.Add(parentName, img);
			}
			ConstImage parentImage = resourceDic[parentName] as ConstImage;
			if (parentImage == null || !parentImage.IsCreated)
			{
				ParserMediator.Warn("作成に失敗したリソースを元にスプライトを作成しようとしました: " + arg2, sp, 1);
				return null;
			}
			Rectangle rect = new Rectangle(new Point(0, 0), parentImage.Bitmap.Size);
			Point pos = new Point();
			Size destSize = rect.Size;
			int delay = 1000;
			//name,parentname, x,y,w,h ,offset_x,offset_y, delayTime, dest_w,dest_h
			if(tokens.Length >= 6)//x,y,w,h
			{
				int[] rectValue = new int[4];
				bool sccs = true;
				for (int i = 0; i < 4; i++)
					sccs &= int.TryParse(tokens[i + 2], out rectValue[i]);
				if (sccs)
				{
					rect = new Rectangle(rectValue[0], rectValue[1], rectValue[2], rectValue[3]);

                    if (rect.Width <= 0 || rect.Height <= 0)
					{
						ParserMediator.Warn("スプライトの高さまたは幅には正の値のみ指定できます: " + name, sp, 1);
						return null;
					}
					// 参考侧：裁剪矩形越出父图像时警告并拒绝（lazy 实体化路径共用此处，Bitmap 尺寸可得）
					if (!rect.IntersectsWith(new Rectangle(0, 0, parentImage.Bitmap.Width, parentImage.Bitmap.Height)))
					{
						ParserMediator.Warn("親画像の範囲外を参照しています: " + name, sp, 1);
						return null;
					}
					destSize = rect.Size;
				}
				if(tokens.Length >= 8)
				{
					sccs = true;
					for (int i = 0; i < 2; i++)
						sccs &= int.TryParse(tokens[i + 6], out rectValue[i]);
					if (sccs)
						pos = new Point(rectValue[0], rectValue[1]);
					if (tokens.Length >= 9)
					{
						sccs = int.TryParse(tokens[8], out delay);
						if (sccs && delay <= 0)
						{
							ParserMediator.Warn("フレーム表示時間には正の値のみ指定できます: " + name, sp, 1);
							return null;
						}
					}
				}
				if (tokens.Length >= 11)
				{
					int destWidth;
					int destHeight;
					// 参考侧：dest_w/dest_h 解析成功即采用（负值由 ASprite 构造取绝对值实现放大语义），仅 0 值跳过
					if (int.TryParse(tokens[9], out destWidth) && int.TryParse(tokens[10], out destHeight) && destWidth != 0 && destHeight != 0)
						destSize = new Size(destWidth, destHeight);
				}
			}
			// 既存のスプライトに対するフレーム追加
			if (currentAnime != null && currentAnime.Name == name)
			{
				if(!currentAnime.AddFrame(parentImage, rect, pos, delay))
				{
					ParserMediator.Warn("アニメーションスプライトのフレーム追加に失敗しました: " + arg2, sp, 1);
					return null;
				}
				return null;
			}

			ASprite image = new SpriteF(name, parentImage, rect, pos, destSize);
			return image;
		}

		private static bool UseLazyResourceIndex
		{
			get { return Program.Compatibility.UsesLazyResourceIndex; }
		}

		private static void BuildLazyResourceIndex(List<string> csvFiles)
		{
			lazyImageDictionary.Clear();
			int indexedCount = 0;
			for (int i = 0; i < csvFiles.Count; i++)
			{
				string filepath = csvFiles[i];
				string directory = Path.GetDirectoryName(filepath) + "/";
				string filename = Path.GetFileName(filepath);
				string[] lines = uEmuera.Utils.GetResourceCSVLines(filepath, Config.Encode);
				LazySpriteDefinition currentAnime = null;
				for (int l = 0; l < lines.Length; l++)
				{
					bool isOptional;
					string str = NormalizeResourceCsvLineForIndex(lines[l], directory, out isOptional);
					if (str.Length == 0 || str.StartsWith(";"))
						continue;
					int tokenCount = ReadRawCsvHeadFields(str, out string token0, out string token1, out _, out _);
					if (tokenCount < 2)
						continue;
					string spriteName = token0.Trim().ToUpper();
					string arg2 = token1.Trim();
					if (spriteName.Length == 0 || arg2.Length == 0)
						continue;

					var definition = new LazySpriteDefinition
					{
						RawCsvLine = str,
						SpriteName = spriteName,
						Directory = directory,
						Position = new ScriptPosition(filename, l + 1),
						IsAnime = arg2.Equals("ANIME", StringComparison.OrdinalIgnoreCase),
						IsOptional = isOptional
					};

					if (definition.IsAnime)
					{
						LazySpriteDefinition existing;
						if (!lazyImageDictionary.TryGetValue(spriteName, out existing) || (existing.IsOptional && !definition.IsOptional))
						{
							definition.Frames = new List<LazySpriteDefinition>();
							lazyImageDictionary[spriteName] = definition;
							RegisterCsvSpriteName(spriteName);
							if (existing == null)
								indexedCount++;
							currentAnime = definition;
						}
						else
						{
							// 与 eager 路径一致：同名资源重定义时提示（保留先定义者）。
							ParserMediator.Warn("同名のリソースが既に作成されています: " + spriteName, definition.Position, 0);
							currentAnime = existing;
						}
						continue;
					}

					if (currentAnime != null && string.Equals(currentAnime.SpriteName, spriteName, StringComparison.OrdinalIgnoreCase))
					{
						currentAnime.Frames.Add(definition);
						continue;
					}

					currentAnime = null;
					LazySpriteDefinition existingSprite;
					if (!lazyImageDictionary.TryGetValue(spriteName, out existingSprite) || (existingSprite.IsOptional && !definition.IsOptional))
					{
						lazyImageDictionary[spriteName] = definition;
						RegisterCsvSpriteName(spriteName);
						if (existingSprite == null)
							indexedCount++;
					}
					else
					{
						// 与 eager 路径一致：同名资源重定义时提示（保留先定义者）。
						ParserMediator.Warn("同名のリソースが既に作成されています: " + spriteName, definition.Position, 0);
					}
				}
			}
		}

		private static ASprite RealizeLazySprite(string name, LazySpriteDefinition definition)
		{
			if (definition == null)
				return null;
			int start = System.Environment.TickCount;
			ASprite result = RealizeLazySpriteCore(name, definition);
			int elapsedMs = System.Environment.TickCount - start;
			LogSlowLazySpriteRealize(name, definition, elapsedMs, result != null);
			return result;
		}

		private static ASprite RealizeLazySpriteCore(string name, LazySpriteDefinition definition)
		{
			if (definition.IsAnime)
			{
				SpriteAnime anime = CreateFromCsv(SplitRawCsvLine(definition.RawCsvLine), definition.Directory, null, definition.Position) as SpriteAnime;
				if (anime == null)
					return null;
				if (definition.Frames != null)
				{
					for (int i = 0; i < definition.Frames.Count; i++)
					{
						var frame = definition.Frames[i];
						CreateFromCsv(SplitRawCsvLine(frame.RawCsvLine), frame.Directory, anime, frame.Position);
					}
				}
				imageDictionary[name] = anime;
				return anime;
			}

			ASprite sprite = CreateFromCsv(SplitRawCsvLine(definition.RawCsvLine), definition.Directory, null, definition.Position) as ASprite;
			if (sprite != null)
				imageDictionary[name] = sprite;
			return sprite;
		}

		private static void LogSlowLazySpriteRealize(string name, LazySpriteDefinition definition, int elapsedMs, bool success)
		{
			if (elapsedMs < LazySpriteSlowRealizeThresholdMs)
				return;
			int frameCount = definition != null && definition.Frames != null ? definition.Frames.Count : 0;
			global::GenericUtils.Warn(global::EmueraLogCategory.Sprite, () =>
				$"[RESOURCE] lazy sprite realize slow: name={name}, anime={definition?.IsAnime}, frames={frameCount}, elapsed={elapsedMs}ms, success={success}");
		}

		private static string NormalizeResourceCsvLineForIndex(string line, string directory, out bool isOptional)
		{
			isOptional = false;
			string str = line.Trim();
			if (str.Length == 0)
				return str;
			if (str[0] != ';')
				return str;

			string candidate = str.Substring(1).Trim();
			int tokenCount = ReadRawCsvHeadFields(candidate, out string token0, out string token1, out _, out _);
			if (tokenCount < 2)
				return str;
			string name = token0.Trim();
			string filename = token1.Trim();
			if (name.Length == 0)
				return str;
			if (filename.Equals("ANIME", StringComparison.OrdinalIgnoreCase))
			{
				if (tokenCount < 4)
					return str;
				isOptional = true;
				return candidate;
			}
			if (filename.IndexOf('.') < 0)
				return str;
			// 懒加载索引只接纳磁盘上真实存在的注释候选，避免SPRITECREATED返回真但实际取宽高失败。
			if (!TryResolveExistingResourcePath(directory + filename, out _))
				return str;
			isOptional = true;
			return candidate;
		}

		static private string NormalizeResourceCsvLine(string line, string directory)
		{
			string str = line.Trim();
			if (str.Length == 0)
				return str;
			if (str[0] != ';')
				return str;

			// Some Snake resource packs keep optional sprite definitions behind a leading
			// semicolon while still referencing those names from ERB HTML.
			string candidate = str.Substring(1).Trim();
			int tokenCount = ReadRawCsvHeadFields(candidate, out string token0, out string token1, out _, out _);
			if (tokenCount < 6)
				return str;
			string name = token0.Trim();
			string filename = token1.Trim();
			if (name.Length == 0 || filename.IndexOf('.') < 0)
				return str;
			if (!TryResolveExistingResourcePath(directory + filename, out _))
				return str;
			return candidate;
		}

		private static int ReadRawCsvHeadFields(string line, out string token0, out string token1, out string token2, out string token3)
		{
			token0 = "";
			token1 = "";
			token2 = "";
			token3 = "";
			if (line == null)
				line = "";

			// 资源 CSV 与原核心一致，只按裸逗号切分，不支持引号转义。
			// 索引阶段只需要头字段和字段总数，避免给每行创建完整 string[]。
			int count = 1;
			int fieldIndex = 0;
			int start = 0;
			for (int i = 0; i <= line.Length; i++)
			{
				if (i < line.Length && line[i] != ',')
					continue;
				if (fieldIndex < 4)
				{
					string value = i == start ? "" : line.Substring(start, i - start);
					if (fieldIndex == 0)
						token0 = value;
					else if (fieldIndex == 1)
						token1 = value;
					else if (fieldIndex == 2)
						token2 = value;
					else
						token3 = value;
				}
				fieldIndex++;
				if (i >= line.Length)
					break;
				count++;
				start = i + 1;
			}
			return count;
		}

		private static string[] SplitRawCsvLine(string line)
		{
			if (line == null)
				line = "";

			// 保留 string.Split(',') 的空字段与尾随空字段语义，但用单次扫描减少内部枚举开销。
			int count = 1;
			for (int i = 0; i < line.Length; i++)
			{
				if (line[i] == ',')
					count++;
			}
			string[] tokens = new string[count];
			int fieldIndex = 0;
			int start = 0;
			for (int i = 0; i <= line.Length; i++)
			{
				if (i < line.Length && line[i] != ',')
					continue;
				tokens[fieldIndex++] = i == start ? "" : line.Substring(start, i - start);
				start = i + 1;
			}
			return tokens;
		}

		private static bool TryResolveExistingResourcePath(string candidate, out string resolved)
		{
			resolved = "";
			if (string.IsNullOrWhiteSpace(candidate))
				return false;

			string key = uEmuera.Utils.NormalizePath(candidate);
			if (resolvedExistingResourcePathCache.TryGetValue(key, out resolved))
				return resolved.Length != 0;

			// Resource CSV paths are static during one game session. Cache misses as well
			// to avoid repeated Android case-insensitive directory scans for the same file.
			string actual = uEmuera.Utils.ResolveExistingFilePath(key);
			if (!uEmuera.Utils.FileExists(actual))
			{
				resolvedExistingResourcePathCache[key] = "";
				resolved = "";
				return false;
			}

			resolvedExistingResourcePathCache[key] = actual;
			resolved = actual;
			return true;
		}



	}
}
