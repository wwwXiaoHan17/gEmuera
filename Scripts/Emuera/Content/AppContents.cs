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
		static readonly Dictionary<string, ASprite> imageDictionary = new Dictionary<string, ASprite>();
		static readonly Dictionary<int, GraphicsImage> gList;

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

	            name = name.ToUpper();
	            ASprite result = null;
	            if (imageDictionary.TryGetValue(name, out result))
	                return result;
	            if (name.IndexOf("褐", StringComparison.Ordinal) >= 0 || name.IndexOf("CIP正面", StringComparison.Ordinal) >= 0 && name.IndexOf("色", StringComparison.Ordinal) >= 0)
	                Godot.GD.Print($"[GetSprite] NOT FOUND: '{name}' (len={name.Length}, bytes={BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(name), 0, Math.Min(40, System.Text.Encoding.UTF8.GetByteCount(name)))})");
	            if (name.StartsWith("CUTIN") && int.TryParse(name.Substring(5), out int graphicsId))
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
			if (!uEmuera.Utils.DirectoryExists(Program.ContentDir))
				return true;
			try
			{
				//resourcesフォルダ内の全てのcsvファイルを探索する
				List<string> csvFiles = uEmuera.Utils.GetFilePaths(Program.ContentDir, "*.csv", SearchOption.AllDirectories);
                Godot.GD.Print($"[AppContents] Found {csvFiles.Count} CSV files in {Program.ContentDir}");
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
						string str = line.Trim();
						if (str.Length == 0 || str.StartsWith(";"))
							continue;
						string[] tokens = str.Split(',');
						//AContentItem item = CreateFromCsv(tokens);
						ScriptPosition sp = new ScriptPosition(filename, lineNo);
						ASprite item = CreateFromCsv(tokens, directory, currentAnime, sp) as ASprite;
						if (item != null)
						{
							currentAnime = item as SpriteAnime;
							if (!imageDictionary.ContainsKey(item.Name))
                            {
								imageDictionary.Add(item.Name, item);
                                loadedCount++;
                                if (item.Name.IndexOf("褐", StringComparison.Ordinal) >= 0)
                                    Godot.GD.Print($"[AppContents] LOADED sprite with 褐: '{item.Name}' (len={item.Name.Length}, bytes={BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(item.Name), 0, Math.Min(40, System.Text.Encoding.UTF8.GetByteCount(item.Name)))})");
                            }
							else
							{
								ParserMediator.Warn("同名のリソースがすでに作成されています:"+item.Name, sp, 0);
								item.Dispose();
							}
						}
					}
                    if (loadedCount > 0 || filename.Contains("ボディ") || filename.Contains("ボデ"))
                        Godot.GD.Print($"[AppContents] CSV '{filename}' dir='{directory}' loaded {loadedCount} sprites (lines={linecount})");
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
			imageDictionary.Clear();
			foreach (var graph in gList.Values)
				graph.GDispose();
			gList.Clear();
		}

		//タイトルに戻る時用（コードの変更はないので、動的に作られた分だけ削除）
		static public void UnloadGraphicList()
		{
			foreach (var graph in gList.Values)
				graph.GDispose();
			gList.Clear();
		}

		/// <summary>
		/// resourcesフォルダ中のcsvの1行を読んで新しいリソースを作る(or既存のアニメーションスプライトに1フレーム追加する)
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
			string arg2 = tokens[1].Trim();//画像ファイル名
			if (name.Length == 0 || arg2.Length == 0)
				return null;
			//アニメーションスプライト宣言
			if (arg2.Equals("ANIME", StringComparison.OrdinalIgnoreCase))
			{
				if (tokens.Length < 4)
				{
					ParserMediator.Warn("アニメーションスプライトのサイズが宣言されていません", sp, 1);
					return null;
				}
				//w,h
				int[] sizeValue = new int[2];
				bool sccs = true;
				for (int i = 0; i < 2; i++)
					sccs &= int.TryParse(tokens[i + 2], out sizeValue[i]);
				if (!sccs || sizeValue[0] <= 0 || sizeValue[1] <= 0 || sizeValue[0] > AbstractImage.MAX_IMAGESIZE || sizeValue[1] > AbstractImage.MAX_IMAGESIZE)
				{
					ParserMediator.Warn("アニメーションスプライトのサイズの指定が適切ではありません", sp, 1);
					return null;
				}
				SpriteAnime anime = new SpriteAnime(name, new Size(sizeValue[0],sizeValue[1]));
				return anime;
			}
			//アニメ宣言以外（アニメ用フレーム含む

			if(arg2.IndexOf('.') < 0)
			{
				ParserMediator.Warn("第二引数に拡張子がありません:" + arg2, sp, 1);
				return null;
			}
			string parentName = dir + arg2;
						string filepath = uEmuera.Utils.ResolveExistingFilePath(parentName);

			//親画像のロードConstImage
			if (!resourceDic.ContainsKey(parentName))
			{
				if (!uEmuera.Utils.FileExists(filepath))
				{
					ParserMediator.Warn("指定された画像ファイルが見つかりませんでした:" + arg2, sp, 1);
					return null;
				}
				// BitmapTexture preloads via SpriteManager and sets size automatically.
				BitmapTexture bmp = new BitmapTexture(filepath);
                bmp.name = name;
				// Fallback dimension load if SpriteManager failed to read the image.
				if (bmp.size.Width == 0 || bmp.size.Height == 0)
				{
					var tempImg = new Godot.Image();
					var err = tempImg.Load(filepath);
					if (err == Godot.Error.Ok)
					{
						bmp.size.Width = tempImg.GetWidth();
						bmp.size.Height = tempImg.GetHeight();
					}
					tempImg.Dispose();
				}
				if (bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
				{
					//1824-2 すでに8192以上の幅を持つ画像を利用したバリアントが存在してしまっていたため、警告しつつ許容するように変更
					//	bmp.Dispose();
					ParserMediator.Warn("指定された画像ファイルの大きさが大きすぎます(幅及び高さを"+ AbstractImage.MAX_IMAGESIZE.ToString()+ "以下にすることを強く推奨します):" + arg2, sp, 1);
					//return null;
				}
				ConstImage img = new ConstImage(parentName);
				img.CreateFrom(bmp, Config.TextDrawingMode == TextDrawingMode.WINAPI);
				if (!img.IsCreated)
				{
					ParserMediator.Warn("画像リソースの作成に失敗しました:" + arg2, sp, 1);
					return null;
				}
				resourceDic.Add(parentName, img);
			}
			ConstImage parentImage = resourceDic[parentName] as ConstImage;
			if (parentImage == null || !parentImage.IsCreated)
			{
				ParserMediator.Warn("作成に失敗したリソースを元にスプライトを作成しようとしました:" + arg2, sp, 1);
				return null;
			}
			Rectangle rect = new Rectangle(new Point(0, 0), parentImage.Bitmap.Size);
			Point pos = new Point();
			int delay = 1000;
			//name,parentname, x,y,w,h ,offset_x,offset_y, delayTime
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
						ParserMediator.Warn("スプライトの高さ又は幅には正の値のみ指定できます:" + name, sp, 1);
						return null;
					}
                    //uEmuera在此时尚未获取图片尺寸
					//if (!rect.IntersectsWith(new Rectangle(0,0,parentImage.Bitmap.Width, parentImage.Bitmap.Height)))
					//{
					//	ParserMediator.Warn("親画像の範囲外を参照しています:" + name, sp, 1);
					//	return null;
					//}
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
							ParserMediator.Warn("フレーム表示時間には正の値のみ指定できます:" + name, sp, 1);
							return null;
						}
					}
				}
			}
			//既存のスプライトに対するフレーム追加
			if (currentAnime != null && currentAnime.Name == name)
			{
				if(!currentAnime.AddFrame(parentImage, rect, pos, delay))
				{
					ParserMediator.Warn("アニメーションスプライトのフレームの追加に失敗しました:" + arg2, sp, 1);
					return null;
				}
				return null;
			}

			//新規スプライト定義
			ASprite image = new SpriteF(name, parentImage, rect, pos);
			return image;
		}



	}
}
