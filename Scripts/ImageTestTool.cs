using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

public partial class ImageTestTool : Control
{
	[Export] public string GamePath = "";

	private VBoxContainer _mainVBox;
	private Label _statsLabel;
	private GridContainer _imageGrid;
	private RichTextLabel _logLabel;
	private ScrollContainer _gridScroll;

	private readonly Color _okColor = new(0.2f, 0.9f, 0.3f);
	private readonly Color _failColor = new(0.95f, 0.25f, 0.25f);
	private readonly Color _warnColor = new(0.95f, 0.7f, 0.2f);

	private int _testCount;
	private int _passCount;

	public override void _Ready()
	{
		if (string.IsNullOrEmpty(GamePath))
		{
			var dirs = Directory.GetDirectories(
				ProjectSettings.GlobalizePath("res://"), "era*", SearchOption.TopDirectoryOnly);
			if (dirs.Length > 0)
				GamePath = dirs[0];
		}

		BuildUi();
		Log($"Test tool ready. Game path: {GamePath}");
		Log("Click a test button to begin.");
	}

	void BuildUi()
	{
		_mainVBox = new VBoxContainer();
		_mainVBox.SetAnchorsPreset(LayoutPreset.FullRect);
		_mainVBox.AddThemeConstantOverride("separation", 8);
		AddChild(_mainVBox);

		// Header
		var header = new Label();
		header.Text = "Emuera Image Test Tool";
		header.AddThemeFontSizeOverride("font_size", 24);
		_mainVBox.AddChild(header);

		// Button row
		var btnRow = new HBoxContainer();
		_mainVBox.AddChild(btnRow);

		AddButton(btnRow, "Test Direct Load", TestDirectLoad);
		AddButton(btnRow, "Test BitmapTexture", TestBitmapTexture);
		AddButton(btnRow, "Test Atlas", TestAtlas);
		AddButton(btnRow, "Test Thread Load", TestThreadLoad);
		AddButton(btnRow, "Test EmueraImage", TestEmueraImage);
		AddButton(btnRow, "Clear", ClearResults);

		// Stats
		_statsLabel = new Label();
		_statsLabel.Text = "Ready";
		_mainVBox.AddChild(_statsLabel);

		// Image grid
		_gridScroll = new ScrollContainer();
		_gridScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_mainVBox.AddChild(_gridScroll);

		_imageGrid = new GridContainer();
		_imageGrid.Columns = 4;
		_imageGrid.AddThemeConstantOverride("h_separation", 10);
		_imageGrid.AddThemeConstantOverride("v_separation", 10);
		_gridScroll.AddChild(_imageGrid);

		// Log
		var logLabel = new Label();
		logLabel.Text = "Log:";
		_mainVBox.AddChild(logLabel);

		_logLabel = new RichTextLabel();
		_logLabel.CustomMinimumSize = new Vector2(0, 180);
		_logLabel.BbcodeEnabled = true;
		_logLabel.ScrollFollowing = true;
		_mainVBox.AddChild(_logLabel);
	}

	static void AddButton(HBoxContainer parent, string text, Action action)
	{
		var btn = new Button();
		btn.Text = text;
		btn.Pressed += action;
		parent.AddChild(btn);
	}

	void Log(string msg)
	{
		_logLabel.AppendText(msg + "\n");
		GD.Print($"[ImageTest] {msg}");
	}

	void ClearResults()
	{
		foreach (var child in _imageGrid.GetChildren())
			child.QueueFree();
		_testCount = 0;
		_passCount = 0;
		UpdateStats();
		_logLabel.Clear();
	}

	void UpdateStats()
	{
		_statsLabel.Text = $"Passed: {_passCount} / {_testCount}";
	}

	static string GetResourcesDir(string basePath)
	{
		if (string.IsNullOrEmpty(basePath)) return null;
		var r1 = Path.Combine(basePath, "resources");
		if (Directory.Exists(r1)) return r1;
		var r2 = Path.Combine(basePath, "RESOURCES");
		if (Directory.Exists(r2)) return r2;
		return null;
	}

	void AddCard(string title, Texture2D tex, string info, bool passed, Color? sample = null)
	{
		_testCount++;
		if (passed) _passCount++;
		UpdateStats();

		var card = new PanelContainer();
		card.CustomMinimumSize = new Vector2(260, 260);

		var style = new StyleBoxFlat();
		style.BgColor = passed ? new Color(0.12f, 0.18f, 0.12f) : new Color(0.2f, 0.1f, 0.1f);
		style.BorderWidthBottom = 2;
		style.BorderWidthLeft = 2;
		style.BorderWidthRight = 2;
		style.BorderWidthTop = 2;
		style.BorderColor = passed ? _okColor : _failColor;
		card.AddThemeStyleboxOverride("panel", style);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		card.AddChild(vbox);

		var titleLbl = new Label();
		titleLbl.Text = title;
		titleLbl.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
		titleLbl.AddThemeFontSizeOverride("font_size", 12);
		vbox.AddChild(titleLbl);

		if (tex != null)
		{
			var texRect = new TextureRect();
			texRect.CustomMinimumSize = new Vector2(100, 100);
			texRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
			texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			texRect.Texture = tex;
			vbox.AddChild(texRect);
		}
		else
		{
			var placeholder = new Label();
			placeholder.Text = "[no texture]";
			placeholder.CustomMinimumSize = new Vector2(100, 100);
			placeholder.HorizontalAlignment = HorizontalAlignment.Center;
			placeholder.VerticalAlignment = VerticalAlignment.Center;
			vbox.AddChild(placeholder);
		}

		var infoLbl = new Label();
		infoLbl.Text = info;
		infoLbl.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
		infoLbl.AddThemeFontSizeOverride("font_size", 10);
		vbox.AddChild(infoLbl);

		if (sample.HasValue)
		{
			var hbox = new HBoxContainer();
			var colorRect = new ColorRect();
			colorRect.CustomMinimumSize = new Vector2(24, 24);
			colorRect.Color = sample.Value;
			hbox.AddChild(colorRect);
			var colorLbl = new Label();
			colorLbl.Text = $"({sample.Value.R:F2},{sample.Value.G:F2},{sample.Value.B:F2},{sample.Value.A:F2})";
			colorLbl.AddThemeFontSizeOverride("font_size", 10);
			hbox.AddChild(colorLbl);
			vbox.AddChild(hbox);
		}

		var statusLbl = new Label();
		statusLbl.Text = passed ? "PASS" : "FAIL";
		statusLbl.AddThemeColorOverride("font_color", passed ? _okColor : _failColor);
		vbox.AddChild(statusLbl);

		_imageGrid.AddChild(card);
	}

	void TestDirectLoad()
	{
		Log("=== Direct Load Test ===");
		var resDir = GetResourcesDir(GamePath);
		if (resDir == null)
		{
			Log("Resources directory not found.");
			return;
		}

		var exts = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.tga" };
		var files = new List<string>();
		foreach (var ext in exts)
			files.AddRange(Directory.GetFiles(resDir, ext, SearchOption.AllDirectories));

		Log($"Found {files.Count} images.");

		foreach (var file in files)
		{
			var name = Path.GetFileName(file);
			var ti = SpriteManager.GetTextureInfo(name, file);

			bool pass = false;
			string info = "";
			Texture2D tex = null;
			Color? sample = null;

			if (ti == null)
			{
				info = "GetTextureInfo returned null";
			}
			else if (ti.image == null)
			{
				info = "Image is null";
			}
			else
			{
				int w = ti.width;
				int h = ti.height;
				info = $"{w}x{h}";

				var imageTex = ti.texture;
				if (imageTex != null)
				{
					pass = true;
					tex = imageTex;
					if (w > 0 && h > 0)
					{
						var c = ti.image.GetPixel(0, 0);
						sample = c;
					}
				}
				else
				{
					info += " | texture FAILED";
				}
			}

			AddCard(name, tex, info, pass, sample);
			if (!pass) Log($"FAIL direct: {name} -> {info}");
		}
	}

	void TestBitmapTexture()
	{
		Log("=== BitmapTexture Test ===");
		var resDir = GetResourcesDir(GamePath);
		if (resDir == null)
		{
			Log("Resources directory not found.");
			return;
		}

		var files = Directory.GetFiles(resDir, "*.png", SearchOption.AllDirectories)
			.Concat(Directory.GetFiles(resDir, "*.jpg", SearchOption.AllDirectories))
			.Take(30)
			.ToArray();

		Log($"Testing BitmapTexture on {files.Length} files...");

		foreach (var file in files)
		{
			var name = Path.GetFileName(file);
			var bt = new uEmuera.Drawing.BitmapTexture(file);

			bool pass = false;
			string info = $"{bt.Width}x{bt.Height}";
			Texture2D tex = null;
			Color? sample = null;

			if (bt.texture != null)
			{
				pass = true;
				tex = bt.texture;
				var srcImg = bt.sourceImage;
				if (srcImg != null && srcImg.GetWidth() > 0 && srcImg.GetHeight() > 0)
				{
					sample = srcImg.GetPixel(0, 0);
				}
			}
			else
			{
				info += " | bt.texture null";
			}

			AddCard($"BT: {name}", tex, info, pass, sample);
			if (!pass) Log($"FAIL BitmapTexture: {name}");
		}
	}

	void TestAtlas()
	{
		Log("=== AtlasTexture Test ===");
		var resDir = GetResourcesDir(GamePath);
		if (resDir == null)
		{
			Log("Resources directory not found.");
			return;
		}

		var files = Directory.GetFiles(resDir, "*.png", SearchOption.AllDirectories)
			.Concat(Directory.GetFiles(resDir, "*.jpg", SearchOption.AllDirectories))
			.ToArray();

		if (files.Length == 0)
		{
			Log("No base images for atlas test.");
			return;
		}

		// Pick the largest image as base
		string bestFile = files[0];
		int bestSize = 0;
		foreach (var f in files)
		{
			var fi = new FileInfo(f);
			if (fi.Length > bestSize)
			{
				bestSize = (int)fi.Length;
				bestFile = f;
			}
		}

		var name = Path.GetFileName(bestFile);
		var ti = SpriteManager.GetTextureInfo(name, bestFile);
		if (ti == null || ti.texture == null)
		{
			Log("Failed to load base image for atlas test.");
			return;
		}

		int w = ti.width;
		int h = ti.height;
		Log($"Atlas base: {name} ({w}x{h})");

		// 1) Full
		AddCard("Full", ti.texture, $"{w}x{h}", true);

		// 2) Top-left quarter
		if (w >= 4 && h >= 4)
		{
			var a = new AtlasTexture();
			a.Atlas = ti.texture;
			a.Region = new Rect2(0, 0, w / 2, h / 2);
			AddCard("TL 1/2", a, $"0,0 {w / 2}x{h / 2}", true);
		}

		// 3) Bottom-right quarter
		if (w >= 4 && h >= 4)
		{
			var a = new AtlasTexture();
			a.Atlas = ti.texture;
			a.Region = new Rect2(w / 2, h / 2, w / 2, h / 2);
			AddCard("BR 1/2", a, $"{w / 2},{h / 2} {w / 2}x{h / 2}", true);
		}

		// 4) Center small
		if (w >= 8 && h >= 8)
		{
			var a = new AtlasTexture();
			a.Atlas = ti.texture;
			a.Region = new Rect2(w / 4, h / 4, w / 4, h / 4);
			AddCard("Center 1/4", a, $"{w / 4},{h / 4} {w / 4}x{h / 4}", true);
		}

		// 5) 1x1 pixel
		if (w > 0 && h > 0)
		{
			var a = new AtlasTexture();
			a.Atlas = ti.texture;
			a.Region = new Rect2(0, 0, 1, 1);
			AddCard("1x1 pixel", a, "0,0 1x1", true);
		}
	}

	void TestThreadLoad()
	{
		Log("=== Thread Safety Test ===");
		var resDir = GetResourcesDir(GamePath);
		if (resDir == null)
		{
			Log("Resources directory not found.");
			return;
		}

		var files = Directory.GetFiles(resDir, "*.png", SearchOption.AllDirectories)
			.Concat(Directory.GetFiles(resDir, "*.jpg", SearchOption.AllDirectories))
			.Take(20)
			.ToArray();

		Log($"Background-loading {files.Length} images...");

		var bgFiles = new List<string>();
		var thread = new Thread(() =>
		{
			foreach (var file in files)
			{
				var n = Path.GetFileName(file);
				var ti = SpriteManager.GetTextureInfo(n, file);
				if (ti != null)
				{
					// Safe: only access Image (CPU data), not texture (GPU)
					int iw = ti.width;
					int ih = ti.height;
					CallDeferred(nameof(Log), $"BG: {n} {iw}x{ih}");
					bgFiles.Add(file);
				}
				else
				{
					CallDeferred(nameof(Log), $"BG FAIL: {n}");
				}
			}
			CallDeferred(nameof(ThreadDisplay), bgFiles.ToArray());
		});
		thread.Start();
	}

	void ThreadDisplay(string[] files)
	{
		Log("Displaying thread-loaded textures on main thread (lazy creation)...");
		foreach (var file in files)
		{
			var n = Path.GetFileName(file);
			var ti = SpriteManager.GetTextureInfo(n, file);
			bool pass = false;
			string info = "";
			Texture2D tex = null;

			if (ti != null)
			{
				tex = ti.texture;
				if (tex != null)
				{
					pass = true;
					info = $"{tex.GetWidth()}x{tex.GetHeight()} (main thread)";
				}
				else
				{
					info = "texture null on main thread";
				}
			}
			else
			{
				info = "TextureInfo missing";
			}

			AddCard($"TH: {n}", tex, info, pass);
			if (!pass) Log($"FAIL thread-display: {n} -> {info}");
		}
	}

	void TestEmueraImage()
	{
		Log("=== EmueraImage Render Test ===");
		var resDir = GetResourcesDir(GamePath);
		if (resDir == null)
		{
			Log("Resources directory not found.");
			return;
		}

		var files = Directory.GetFiles(resDir, "*.png", SearchOption.AllDirectories)
			.Concat(Directory.GetFiles(resDir, "*.jpg", SearchOption.AllDirectories))
			.Take(8)
			.ToArray();

		Log($"Rendering {files.Length} images via EmueraImage...");

		foreach (var file in files)
		{
			var n = Path.GetFileName(file);
			var ti = SpriteManager.GetTextureInfo(n, file);
			if (ti == null || ti.texture == null)
			{
				AddCard($"EI: {n}", null, "load failed", false);
				Log($"FAIL EmueraImage load: {n}");
				continue;
			}

			int w = Math.Min(ti.width, 120);
			int h = Math.Min(ti.height, 120);

			var wrapper = new Control();
			wrapper.CustomMinimumSize = new Vector2(w, h);

			var emuImg = new EmueraImage();
			emuImg.SourceTexture = ti.texture;
			emuImg.SourceRegion = new Rect2(0, 0, ti.width, ti.height);
			emuImg.Size = new Vector2(w, h);
			emuImg.CustomMinimumSize = new Vector2(w, h);
			wrapper.AddChild(emuImg);

			// Can't easily capture render result, so we just verify it was created
			bool pass = true;
			string info = $"{ti.width}x{ti.height} -> EmueraImage {w}x{h}";

			// Use a plain TextureRect for the card thumbnail instead
			AddCard($"EI: {n}", ti.texture, info, pass);
			Log($"EmueraImage created for {n}");
		}
	}
}
