using Godot;
using System.IO;

public partial class FirstWindow : Control
{
	public static string SelectedGamePath { get; private set; }

	ItemList gameList;
	Button startButton;
	Label statusLabel;
	bool permissionGranted = false;

	public override void _Ready()
	{
		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(LayoutPreset.Center);
		vbox.GrowHorizontal = GrowDirection.Both;
		vbox.GrowVertical = GrowDirection.Both;
		AddChild(vbox);

		var title = new Label();
		title.Text = MultiLanguage.Get("FirstWindow.Title", "uEmuera for Godot");
		title.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(title);

		gameList = new ItemList();
		gameList.SizeFlagsVertical = SizeFlags.ExpandFill;
		gameList.CustomMinimumSize = new Vector2(400, 200);
		gameList.ItemSelected += OnGameSelected;
		gameList.ItemActivated += OnGameActivated;
		vbox.AddChild(gameList);

		startButton = new Button();
		startButton.Text = MultiLanguage.Get("FirstWindow.Start", "Start");
		startButton.Disabled = true;
		startButton.Pressed += OnStartPressed;
		vbox.AddChild(startButton);

		statusLabel = new Label();
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vbox.AddChild(statusLabel);

		if (OS.GetName() == "Android")
		{
			GetTree().OnRequestPermissionsResult += OnPermissionsResult;
			statusLabel.Text = "正在请求文件权限...\nRequesting file permissions...";
			OS.RequestPermissions();
		}
		else
		{
			ScanGames();
		}
	}

	void OnPermissionsResult(string permission, bool granted)
	{
		if (permissionGranted)
			return;
		permissionGranted = true;
		statusLabel.Text = "";

		// Check if we can actually access the target directory
		bool canAccess = false;
		using (var dir = DirAccess.Open("/storage/emulated/0"))
		{
			canAccess = dir != null;
		}

		if (!canAccess)
		{
			// MANAGE_EXTERNAL_STORAGE not granted — guide user to settings
			statusLabel.Text = "需要\"所有文件访问\"权限。请在系统设置中开启后返回此应用。\n"
				+ "\"All files access\" permission required. Please enable in system settings.";
			// Still try to scan — user:// and app-specific dirs don't need this permission
		}

		// Try to create the emuera directory
		using (var dir = DirAccess.Open("/storage/emulated/0"))
		{
			if (dir != null && !dir.DirExists("emuera"))
				dir.MakeDir("emuera");
		}

		ScanGames();
	}

	void ScanGames()
	{
		var roots = new System.Collections.Generic.List<string>();

		if (OS.GetName() == "Android")
		{
			roots.Add("/storage/emulated/0/emuera");
			string appDir = OS.GetUserDataDir();
			if (!string.IsNullOrEmpty(appDir))
				roots.Add(appDir);
		}
		else
		{
			string exeDir = OS.GetExecutablePath().GetBaseDir();
			if (!string.IsNullOrEmpty(exeDir))
				roots.Add(exeDir);

			if (OS.HasFeature("editor"))
			{
				string resDir = ProjectSettings.GlobalizePath("res://");
				if (!string.IsNullOrEmpty(resDir))
					roots.Add(resDir);
			}
		}

		string userDir = ProjectSettings.GlobalizePath("user://");
		if (!string.IsNullOrEmpty(userDir))
			roots.Add(userDir);

		foreach (var root in roots)
		{
			// Use Godot's DirAccess for Android compatibility
			using var dir = DirAccess.Open(root);
			if (dir == null)
				continue;

			foreach (string entry in dir.GetDirectories())
			{
				if (entry.StartsWith("era", System.StringComparison.OrdinalIgnoreCase))
				{
					string fullPath = root.TrimEnd('/') + "/" + entry;
					bool exists = false;
					for (int i = 0; i < gameList.ItemCount; i++)
					{
						if (gameList.GetItemMetadata(i).As<string>() == fullPath)
						{
							exists = true;
							break;
						}
					}
					if (!exists)
					{
						gameList.AddItem(entry);
						gameList.SetItemMetadata(gameList.ItemCount - 1, fullPath);
					}
				}
			}
		}

		if (gameList.ItemCount == 0)
		{
			gameList.AddItem(MultiLanguage.Get("FirstWindow.NoGames", "No era games found"));
			gameList.SetItemDisabled(0, true);
			if (string.IsNullOrEmpty(statusLabel.Text))
				statusLabel.Text = "请将 era 游戏文件夹放入以下路径:\n" + string.Join("\n", roots);
		}
	}

	void OnGameSelected(long index)
	{
		startButton.Disabled = false;
	}

	void OnGameActivated(long index)
	{
		if (gameList.IsItemDisabled((int)index))
			return;
		SelectedGamePath = gameList.GetItemMetadata((int)index).As<string>();
		GetTree().ChangeSceneToFile("res://main.tscn");
	}

	void OnStartPressed()
	{
		var selected = gameList.GetSelectedItems();
		if (selected.Length == 0)
			return;
		SelectedGamePath = gameList.GetItemMetadata(selected[0]).As<string>();
		GetTree().ChangeSceneToFile("res://main.tscn");
	}
}
