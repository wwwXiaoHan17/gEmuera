using System;
using System.Collections.Generic;
using GEmuera.Core.Compatibility.Packs;
using gEmuera.GodotHost;

// FirstWindow 的兼容包自愈 partial（2026-11-24）：
//   引擎表面一变（例如新增 capability 后重钉 baseSurfaceHash），此前启用的第一方包就必然被拒载。
//   真实加载要到游戏启动时才在日志里留一条错误、界面无感，游戏静默回退 v24 基线。
//   这里在启动器把启用清单注入引擎之前做两件事：
//   - 过期第一方包 → 改用 App 内置的同名可用包（路径不同才换），并把改绑写回 launcher.cfg；
//   - 换不了的（社区包、内置包也不可用、或选中的就是被覆盖成旧版的内置路径）→ 状态栏明示。
//   判定、改绑规则、文案都在 Core（CompatPackManifestPeek / CompatPackStaleSelectionRebinder），
//   本文件只做宿主事实注入（引擎头部值、文件读取、内置包路径）与存储/界面接线。
//   同一路径上「用户手放的旧包挡住内置包」的情形由 FirstPartyPackBootstrap 的释放器例外处理，
//   先于这里的扫描发生；这里看到的内置路径因此已经是可用的。
public partial class FirstWindow
{
	// 一次改绑在本次启动器会话内的记忆：改绑常发生在启动早期（ResolveStartupGamePath，界面尚未建好），
	// 等状态栏计算提示时包已正常——不记下来用户就完全看不到自愈发生过。
	// Logged = 这条改绑的日志是否已经真的写出去（legacy-runner 的首次注入早于日志系统初始化，
	// 那一次的 Warn 会被直接丢弃，要等之后的刷新补上）。
	sealed record CompatPackSelfHealMemory(string OldPath, string NewPath, string Message)
	{
		public bool Logged { get; set; }
	}

	static readonly Dictionary<string, List<CompatPackSelfHealMemory>> compatPackSelfHealByGame =
		new(CompatPackLauncherConfig.GameKeyComparer);
	// 自愈产生的状态栏提示，拼进 compatPackMappingWarning。
	static string compatPackSelectionWarning = "";
	// 进程内累计写回 launcher.cfg 的改绑次数：界面据此判断勾选列表是否需要按存储重载。
	static int compatPackPersistedRebinds;
	// 诊断会话（legacy-runner / 进程内换局）在本进程里配置过之后，自愈一律不再写回 launcher.cfg。
	// 那条链路的契约是只读配置（ConfigureLegacyRunnerSession），而它之后主场景还会经
	// ResolveStartupGamePath 再注入一次——靠调用点逐个传参挡不住，只能在进程级关掉。
	static bool compatPackSelfHealPersistenceSuppressed;

	/// <summary>由 <see cref="ConfigureLegacyRunnerSession"/> 调用：本进程此后不因自愈改写 launcher.cfg。</summary>
	static void SuppressCompatPackSelfHealPersistence() => compatPackSelfHealPersistenceSuppressed = true;

	/// <summary>
	/// 在合并语义（P0 按游戏选择 &gt; P1 外部环境变量 &gt; P2 映射）得出启用清单之后、写入环境变量之前调用。
	/// 返回真正应注入的清单（分号串）；没有改绑时原样返回 <paramref name="merged"/>。
	/// 只有交互启动链上「按游戏保存的非空选择」会被写回存储——外部环境变量与映射默认值不归启动器持久化，
	/// 诊断会话只读配置（见 <see cref="compatPackSelfHealPersistenceSuppressed"/>）。
	/// 任何失败都退回原清单（等同于自愈之前的行为），绝不阻断启动。
	/// </summary>
	static string ResolveStaleFirstPartyPacks(string gameRoot, bool fromStoredSelection, string merged)
	{
		compatPackSelectionWarning = "";
		IReadOnlyList<string> paths = CompatPackLauncherConfig.ParseSelection(merged);
		if (paths.Count == 0)
			return merged;
		CompatPackEngineHeader engine = FirstPartyPackBootstrap.TryGetEngineHeader();
		if (engine is null)
			return merged;
		string gameKey = CompatPackLauncherConfig.NormalizeGameKey(gameRoot);

		CompatPackSelectionResolution resolution;
		try
		{
			resolution = CompatPackStaleSelectionRebinder.Resolve(
				paths,
				PeekCompatPackFile,
				packId => FirstPartyPackBootstrap.TryGetBundledPackPath(packId, out string bundled) ? bundled : null,
				CompatPackLauncherConfig.GameKeyComparer);
		}
		catch (Exception exception)
		{
			// 与 FirstPartyPackBootstrap 的释放摘要同一种写法：不指定类别（默认配置只放行未指定类别的非 Error 日志），
			// 靠 [LOAD] 前缀在通过开关后归入加载类别。
			GenericUtils.Warn("[LOAD] CompatPack 过期包自愈失败（按原选择注入）：" + exception.Message);
			return merged;
		}

		if (resolution.Rebinds.Count > 0)
		{
			if (!compatPackSelfHealByGame.TryGetValue(gameKey, out List<CompatPackSelfHealMemory> memory))
				compatPackSelfHealByGame[gameKey] = memory = new List<CompatPackSelfHealMemory>();
				foreach (CompatPackRebindNotice notice in resolution.Rebinds)
				{
					// 按游戏保存的选择写回后，下次预检不再改绑。环境变量和映射默认值不写回，
					// 每次刷新状态栏都会再次得到同一对路径——同一对只记一次，否则提示会叠成好几句。
					CompatPackSelfHealMemory entry = FindRemembered(memory, notice);
					if (entry is null)
					{
						entry = new CompatPackSelfHealMemory(notice.OldPath, notice.NewPath, notice.Message);
						memory.Add(entry);
					}
					// 日志只打一次，但要真的打出来：legacy-runner 的第一次注入发生在日志系统初始化之前，
					// 那时的 Warn 会被直接丢弃——记下「尚未打出」，等之后的刷新（日志已可用）再补，
					// 否则诊断回放里发生了替换却不留痕迹。
					if (entry.Logged || !GenericUtils.IsLogEnabled(EmueraLogLevel.Warn))
						continue;
					// 与引擎侧 [LOAD] CompatPack … 同族。不指定日志类别：默认配置下显式类别的非 Error 日志
					// 会被类别开关挡掉，靠 [LOAD] 前缀在通过开关后归入加载类别（写法同 FirstPartyPackBootstrap）。
					// 路径走既有的脱敏函数（按用户的路径截断配置处理，与启动器其它日志同一写法）。不能只取末几级：
					// 一包一目录意味着新旧副本的末三级往往同名（compat_packs/<packId>/<packId>.dll），差别只在更上层的目录。
					GenericUtils.Warn("[LOAD] CompatPack 自愈：" + notice.Message
						+ "（" + gEmuera.Diagnostics.DiagnosticLogRouter.RedactPath(notice.OldPath)
						+ " → " + gEmuera.Diagnostics.DiagnosticLogRouter.RedactPath(notice.NewPath) + "）");
					entry.Logged = true;
				}
			if (fromStoredSelection)
			{
				try
				{
					LauncherSettingsStore.SaveCompatPackSelection(
						gameKey, CompatPackLauncherConfig.SerializeSelection(resolution.Paths));
					compatPackPersistedRebinds++;
				}
				catch (Exception exception)
				{
					// 写回失败不影响本次启动（注入的已是改绑后的清单），下次启动会再次自愈。
					GenericUtils.Warn("[LOAD] CompatPack 自愈结果写回 launcher.cfg 失败：" + exception.Message);
				}
			}
		}

		// 状态栏：本会话里对这个游戏做过的改绑（只要改绑结果仍是当前选择）+ 这次换不了的警告。
		var messages = new List<string>();
		if (compatPackSelfHealByGame.TryGetValue(gameKey, out List<CompatPackSelfHealMemory> remembered))
		{
			foreach (CompatPackSelfHealMemory heal in remembered)
			{
				bool stillHealed = ContainsPackPath(resolution.Paths, heal.NewPath)
					&& !ContainsPackPath(resolution.Paths, heal.OldPath);
				if (stillHealed)
					messages.Add(heal.Message);
			}
		}
		messages.AddRange(resolution.Warnings);
		if (messages.Count > 0)
		{
			compatPackSelectionWarning = "；" + string.Join("；", messages);
			compatPackMappingWarning += compatPackSelectionWarning;
		}
		return resolution.Rebinds.Count > 0
			? CompatPackLauncherConfig.SerializeSelection(resolution.Paths)
			: merged;
	}

	static bool ContainsPackPath(IReadOnlyList<string> paths, string candidate)
	{
		foreach (string path in paths)
		{
			if (SamePackPath(path, candidate))
				return true;
		}
		return false;
	}

	static CompatPackSelfHealMemory FindRemembered(List<CompatPackSelfHealMemory> memory, CompatPackRebindNotice notice)
	{
		foreach (CompatPackSelfHealMemory existing in memory)
		{
			if (SamePackPath(existing.OldPath, notice.OldPath) && SamePackPath(existing.NewPath, notice.NewPath))
				return existing;
		}
		return null;
	}

	static bool SamePackPath(string left, string right)
	{
		return CompatPackLauncherConfig.GameKeyComparer.Equals(
			CompatPackLauncherConfig.NormalizePackPath(left),
			CompatPackLauncherConfig.NormalizePackPath(right));
	}

	/// <summary>预检一个包文件：读不出（不存在/为空/无权限）按「不可用」处理，原因里保留文件缺失的事实。</summary>
	static CompatPackPeekResult PeekCompatPackFile(string path)
	{
		byte[] bytes = FirstPartyPackBootstrap.TryReadPackBytes(path);
		if (bytes is null)
			return CompatPackManifestPeek.CreateUnreadable("包文件不存在或无法读取。");
		return CompatPackManifestPeek.Assess(bytes, FirstPartyPackBootstrap.TryGetEngineHeader());
	}

	/// <summary>
	/// 为当前选中的游戏预检启用清单并刷新状态栏所依赖的提示：选游戏时就把过期的第一方包改绑、写回，
	/// 用户在点「开始」之前就能看到发生了什么。勾选列表若因写回而过期，按存储重载。
	/// </summary>
	void RefreshCompatPackSelectionHealth(LauncherGameEntry entry)
	{
		int rebindsBefore = compatPackPersistedRebinds;
		// 提示要对应「这个游戏」的档案与选择：显式传入它的 launcher 档案名，
		// 不用 SelectedCoreProfileName（那要到点「开始」才更新）。
		ApplyCompatPackEnvironment(entry.GameRoot, GetSelectedCoreProfileName(entry));
		if (compatPackPersistedRebinds == rebindsBefore)
			return;
		LoadCompatPackEditForGame(entry);
	}
}
