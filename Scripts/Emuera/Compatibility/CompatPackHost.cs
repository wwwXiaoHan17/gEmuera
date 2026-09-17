#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;

namespace MinorShift.Emuera.Compatibility
{
	/// <summary>
	/// 兼容包宿主接线（设计 §5/§6）：在 legacy 会话计划绑定前，把显式启用的包集合
	/// 加载（显式发现禁扫描）→ gameIdentity 比对 → 组装进会话计划。任何失败按降级
	/// 不变量回退纯 v24 基线并记 [LOAD] 错误日志，绝不半加载。
	/// 启用清单 v1 来源：环境变量 GEMUERA_COMPAT_PACKS（分号分隔的包程序集绝对/相对
	/// 路径；仿 GEMUERA_AUTOSTART_GAME 先例做诊断/联调入口），launcher 按游戏的包选择
	/// UI 属后续增量（届时改读 launcher 配置，管线不变）。
	/// </summary>
	internal static class CompatPackHost
	{
		// 与 tools/legacy-runner/profiles.generated.json 的 profileIds 一致；
		// capability 词汇表 = 六 profile 声明的能力并集（引擎已收录的全部 quirk id）。
		static readonly string[] AllProfileIds = { "v24pure", "v18", "snake", "erafl", "erablue", "megaten" };

		// v1 内置变体名录（与引擎 LegacyInstructionVariant 的内置映射对应；投影接线
		// 增量落地 handler 替换，当前仅账本/哈希语义）。
		static readonly string[] BuiltinVariantNames = { "builtin:v24", "builtin:snake" };

		/// <summary>读取启用包路径清单；未设置/全空 = 未启用任何包。</summary>
		internal static IReadOnlyList<string> ReadEnabledPackPaths()
		{
			var raw = Environment.GetEnvironmentVariable("GEMUERA_COMPAT_PACKS") ?? "";
			var paths = new List<string>();
			foreach (string entry in raw.Split(';'))
			{
				string trimmed = entry.Trim();
				if (trimmed.Length > 0)
					paths.Add(trimmed);
			}
			return paths;
		}

		internal static CompatPackValidationContext BuildValidationContext()
		{
			var profiles = BuiltInDialectCatalog.CreateLegacyProfileCatalog();
			var capabilities = new HashSet<string>(StringComparer.Ordinal);
			foreach (string profileId in AllProfileIds)
				capabilities.UnionWith(profiles.Resolve(profileId).RequiredCapabilityIds);
			return new CompatPackValidationContext(
				EngineModuleApiVersion,
				capabilities,
				new HashSet<string>(BuiltinVariantNames, StringComparer.Ordinal),
				new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
				new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));
		}

		/// <summary>引擎包 API 版本（v1 = 1；破坏性变更时递增并拒载旧包）。</summary>
		internal const int EngineModuleApiVersion = 1;

		/// <summary>
		/// 最近一次成功组装的包模块 id 白名单（供 Program.ConfigureCompatibilityPlan 的
		/// LegacyCompatibilityProfile.Create 透传给 Compose；白名单外的额外模块仍被严格
		/// 拒绝——信任边界）。legacy 会话单计划语义下与 plan 绑定同生命周期。
		/// </summary>
		internal static IReadOnlyCollection<string>? ActivePackModuleIds { get; private set; }

		/// <summary>
		/// 启动期配置：无包原样返回基线；有包则加载→身份比对→组装。任何失败回退基线
		/// （降级不变量）并记错误日志。成功时包 ALC 保持加载到进程结束（v1 策略贡献
		/// 尚无宿主消费，卸载时机随宿主接线深化再收紧）。
		/// </summary>
		internal static CompatibilityPlan ConfigureForLaunch(CompatibilityPlan baselinePlan, string gameRoot)
		{
			IReadOnlyList<string> paths = ReadEnabledPackPaths();
			if (paths.Count == 0)
				return baselinePlan;

			var context = BuildValidationContext();
			if (!CompatPackLoader.TryLoadSet(paths, context, out CompatPackSet? set, out IReadOnlyList<string> loadErrors))
			{
				global::GenericUtils.Error("[LOAD] CompatPack disabled (load rejected): " + string.Join("; ", loadErrors));
				return baselinePlan;
			}

			if (!VerifyGameIdentity(set!, gameRoot, out List<string> identityErrors))
			{
				set!.UnloadAll();
				global::GenericUtils.Error("[LOAD] CompatPack disabled (game identity mismatch): " + string.Join("; ", identityErrors));
				return baselinePlan;
			}

			if (!CompatPackPlanAssembler.TryAssemble(baselinePlan, set!.Handles, out CompatibilityPlan assembled, out IReadOnlyList<string> assemblyErrors))
			{
				set!.UnloadAll();
				global::GenericUtils.Error("[LOAD] CompatPack disabled (assembly rejected): " + string.Join("; ", assemblyErrors));
				return baselinePlan;
			}

			foreach (CompatPackHandle handle in set!.Handles)
			{
				global::GenericUtils.Info(
					$"[LOAD] CompatPack enabled: id={handle.Manifest.PackId} version={handle.Manifest.PackVersion} "
					+ $"engineApi={handle.Manifest.TargetEngineApi} sha256={handle.PackSha256}");
			}
			ActivePackModuleIds = set!.Handles.Select(handle => handle.Manifest.PackId).ToArray();
			return assembled;
		}

		/// <summary>
		/// gameIdentity 比对（设计 §3.3）：声明身份的包与 GameBase.csv 比对，不匹配整体
		/// 回退。CSV 读 UTF-8（v1 限制：shift-jis 游戏的身份声明包会按不匹配处理）。
		/// </summary>
		static bool VerifyGameIdentity(CompatPackSet set, string gameRoot, out List<string> errors)
		{
			errors = new List<string>();
			bool anyIdentity = false;
			foreach (CompatPackHandle handle in set.Handles)
			{
				if (handle.Manifest.GameIdentity is null)
					continue;
				anyIdentity = true;
			}
			if (!anyIdentity)
				return true;

			if (!TryReadGameBaseCsv(gameRoot, out string gameCode, out string version))
			{
				errors.Add("GameBase.csv 未能解析出 コード/バージョン（gameRoot=" + gameRoot + "）。");
				return false;
			}

			bool allMatch = true;
			foreach (CompatPackHandle handle in set.Handles)
			{
				global::Emuera.Compatibility.Packs.CompatPackGameIdentity identity = handle.Manifest.GameIdentity;
				if (identity is null)
					continue;
				if (!CompatPackGameIdentityCheck.Matches(identity, gameCode, version))
				{
					errors.Add($"包 {handle.Manifest.PackId} 声明 gameCode={identity.GameCode} version={identity.Version}({identity.VersionAccept}) 与游戏 {gameCode}/{version} 不匹配。");
					allMatch = false;
				}
			}
			return allMatch;
		}

		static bool TryReadGameBaseCsv(string gameRoot, out string gameCode, out string version)
		{
			gameCode = "";
			version = "";
			string csvPath = System.IO.Path.Combine(gameRoot, "csv", "GameBase.csv");
			if (!global::uEmuera.Utils.FileExists(csvPath))
			{
				csvPath = System.IO.Path.Combine(gameRoot, "CSV", "GameBase.csv");
				if (!global::uEmuera.Utils.FileExists(csvPath))
					return false;
			}
			try
			{
				string text = System.IO.File.ReadAllText(csvPath, System.Text.Encoding.UTF8);
				return CompatPackGameIdentityCheck.TryParseGameBaseCsv(text, out gameCode, out version);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}
	}
}
