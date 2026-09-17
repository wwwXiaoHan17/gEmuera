#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;

namespace MinorShift.Emuera.Compatibility
{
	/// <summary>
	/// 兼容包宿主接线（设计 §5/§6）：在 legacy 会话计划绑定前，把显式启用的包集合
	/// 加载（显式发现禁扫描）→ gameIdentity 比对 → 组装进会话计划。任何失败按降级
	/// 不变量回退纯 v24 基线并记 [LOAD] 错误日志，绝不半加载。
	/// 启用清单来源：进程环境变量 <see cref="EnabledPacksEnvironmentVariable"/>（分号分隔的
	/// 包程序集路径）。launcher（FirstWindow）按游戏从 launcher.cfg [compat_packs] 节读取
	/// 选择并在启动场景切换前注入该变量——launcher 与引擎同进程，环境变量即进程内传递通道，
	/// 加载管线不变；外部显式设置（诊断/联调）优先于 launcher 注入。
	/// </summary>
	internal static class CompatPackHost
	{
		/// <summary>启用包清单的进程内传递通道（launcher 注入 / 外部诊断覆盖）。</summary>
		internal const string EnabledPacksEnvironmentVariable = "GEMUERA_COMPAT_PACKS";
		// 与 tools/legacy-runner/profiles.generated.json 的 profileIds 一致；
		// capability 词汇表 = 六 profile 声明的能力并集（引擎已收录的全部 quirk id）。
		static readonly string[] AllProfileIds = { "v24pure", "v18", "snake", "erafl", "erablue", "megaten" };

		// 内置方言模块保留名（与 LegacyCompatibilityModuleCatalog.modules 的模块 id 同源）；
		// packId 撞保留名在校验段拒载，绝不让它走到 Compose 的白名单校验（降级不变量）。
		static readonly string[] ReservedModuleIds =
		{
			"gemuera.v24", "game.snake", "game.erafl", "gemuera.v18", "game.erablue", "game.megaten",
		};

		// v1 内置变体组合表与名录：由引擎 BuiltinCompatPackVariants 注册表导出（唯一事实源），
		// 包声明的 builtin:* 变体经注册表解析后真实替换引擎 handler。
		static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> BuiltinVariantInstructions =
			BuiltinCompatPackVariants.SupportedSelections();

		static readonly IReadOnlySet<string> BuiltinVariantNames = new HashSet<string>(
			BuiltinVariantInstructions.Keys, StringComparer.Ordinal);

		/// <summary>读取启用包路径清单；未设置/全空 = 未启用任何包。</summary>
		internal static IReadOnlyList<string> ReadEnabledPackPaths()
		{
			var raw = Environment.GetEnvironmentVariable(EnabledPacksEnvironmentVariable) ?? "";
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
				new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
				new HashSet<string>(ReservedModuleIds, StringComparer.Ordinal),
				BuiltinVariantInstructions,
				BuildEngineInstructionHandlerNames(),
				BuildEngineFunctionHandlerNames());
		}

		/// <summary>
		/// 引擎指令 handler 名全集（register 对账基准）。真实注册表（FunctionIdentifier.funcDic）
		/// 的静态构造器不可在此触发：BuildValidationContext 运行于游戏配置装载前，funcDic 的
		/// 比较器初始化读取 Config.ICVariable，提前触发会把比较器钉在装载前的默认值。故基准
		/// 取 Core 侧生成清单的六 profile 并集——清单由生成器从引擎真实投影导出（单一事实源），
		/// 并集 = 引擎在任何会话可暴露的全部指令名，且 ⊇ 内置模块可 Declare 名单
		///（snake/erafl/erablue 的声明名均在各自增量清单内），包 add 的合法名字不会被误拒。
		/// 在所有会话均被刻意隐藏的 port 内部名（如 OUTPUTLOG 指令形态）不在并集内——它们
		/// 不是任何方言契约的一部分，包试图暴露时按"无 handler"拒载（fail-at-load）。
		/// </summary>
		static HashSet<string> BuildEngineInstructionHandlerNames()
		{
			var union = new HashSet<string>(StringComparer.Ordinal);
			union.UnionWith(LegacyDialectInventories.V24InstructionNames);
			union.UnionWith(LegacyDialectInventories.SnakeDeltaInstructionNames);
			union.UnionWith(LegacyDialectInventories.EraFlDeltaInstructionNames);
			union.UnionWith(LegacyDialectInventories.EraBlueDeltaInstructionNames);
			union.UnionWith(LegacyDialectInventories.MegatenDeltaInstructionNames);
			union.UnionWith(LegacyDialectInventories.V18InstructionNames);
			return union;
		}

		/// <summary>函数侧 handler 名全集，语义与 <see cref="BuildEngineInstructionHandlerNames"/> 对称。</summary>
		static HashSet<string> BuildEngineFunctionHandlerNames()
		{
			var union = new HashSet<string>(StringComparer.Ordinal);
			union.UnionWith(LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
			union.UnionWith(LegacyDialectInventories.SnakeDeltaFunctions.Select(entry => entry.Name));
			union.UnionWith(LegacyDialectInventories.EraFlDeltaFunctions.Select(entry => entry.Name));
			union.UnionWith(LegacyDialectInventories.EraBlueDeltaFunctions.Select(entry => entry.Name));
			union.UnionWith(LegacyDialectInventories.MegatenDeltaFunctions.Select(entry => entry.Name));
			union.UnionWith(LegacyDialectInventories.V18Functions.Select(entry => entry.Name));
			return union;
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
		/// 最近一次成功组装的包 builtin:* 变体选择（指令 → 变体名，跨包合并、同键异值已在校验段
		/// 拒载）。与 ActivePackModuleIds 同生命周期，供投影注入（Compose → SubstituteInstruction）。
		/// </summary>
		internal static IReadOnlyDictionary<string, string>? ActiveVariantSelections { get; private set; }

		/// <summary>
		/// 是否显式启用了兼容包（存在启用路径）。Program.Main 早绑定路径的换绑预判：
		/// 无包时整段跳过，保持与旧路径逐字节等价；有包时才清绑→加载→重绑。
		/// </summary>
		internal static bool HasEnabledPacks()
		{
			return ReadEnabledPackPaths().Count > 0;
		}

		/// <summary>
		/// 启动期配置：无包原样返回基线；有包则加载→身份比对→组装。任何失败回退基线
		/// （降级不变量）并记错误日志。成功时包 ALC 保持加载到进程结束（v1 策略贡献
		/// 尚无宿主消费，卸载时机随宿主接线深化再收紧）。
		/// </summary>
		internal static CompatibilityPlan ConfigureForLaunch(CompatibilityPlan baselinePlan, string gameRoot)
		{
			IReadOnlyList<string> paths = ReadEnabledPackPaths();
			if (paths.Count == 0)
			{
				// 零包早退同样复位跨会话静态投影状态：同进程"带包游戏 A → 退回启动器 →
				// 无包游戏 B"时，A 的白名单/变体选择残留会让 B 的计划哈希显示纯基线、
				// 投影却注入 A 的包表面（违反无包路径逐字节等价铁律）。三个失败分支同权复位。
				ActivePackModuleIds = null;
				ActiveVariantSelections = null;
				return baselinePlan;
			}

			// 顶层兜底：包路径上的任何异常（清单 getter、贡献回放、宿主自身缺陷……）一律
			// 降级回纯基线 + 错误日志，绝不让异常逃逸到 Program.Main（降级不变量）。
			CompatPackSet? loadedSet = null;
			try
			{
				var context = BuildValidationContext();
				if (!CompatPackLoader.TryLoadSet(paths, context, out CompatPackSet? set, out IReadOnlyList<string> loadErrors))
				{
					ActivePackModuleIds = null;
					ActiveVariantSelections = null;
					global::GenericUtils.Error("[LOAD] CompatPack disabled (load rejected): " + string.Join("; ", loadErrors));
					return baselinePlan;
				}
				loadedSet = set;

				if (!VerifyGameIdentity(set!, gameRoot, out List<string> identityErrors))
				{
					ActivePackModuleIds = null;
					ActiveVariantSelections = null;
					set!.UnloadAll();
					global::GenericUtils.Error("[LOAD] CompatPack disabled (game identity mismatch): " + string.Join("; ", identityErrors));
					return baselinePlan;
				}

				if (!CompatPackPlanAssembler.TryAssemble(baselinePlan, set!.Handles, out CompatibilityPlan assembled, out IReadOnlyList<string> assemblyErrors))
				{
					ActivePackModuleIds = null;
					ActiveVariantSelections = null;
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
				// 跨包变体选择合并（同键同值幂等、同键异值校验段已拒；组装器哈希同源）。
				var selections = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (CompatPackHandle handle in set!.Handles)
				{
					foreach (KeyValuePair<string, string> selection in handle.Manifest.VariantSelections)
						selections[selection.Key] = selection.Value;
				}
				ActiveVariantSelections = selections.Count > 0 ? selections : null;
				return assembled;
			}
			catch (Exception exception)
			{
				ActivePackModuleIds = null;
				ActiveVariantSelections = null;
				try
				{
					loadedSet?.UnloadAll();
				}
				catch (Exception unloadException)
				{
					global::GenericUtils.Error("[LOAD] CompatPack unload after failure threw: " + unloadException.Message);
				}
				global::GenericUtils.Error("[LOAD] CompatPack disabled (unexpected failure): "
					+ exception.GetType().Name + ": " + exception.Message);
				return baselinePlan;
			}
		}

		/// <summary>
		/// 复位跨会话静态投影（包模块白名单与变体选择）。与计划绑定同生命周期：计划被
		/// 清理/复位时调用，保证早绑定（EmueraMain 显示默认值路径）不会消费上一会话的
		/// 残留投影。ConfigureForLaunch 的零包/失败分支各自同权复位，此处覆盖会话收尾路径。
		/// </summary>
		internal static void ResetActiveSessionProjection()
		{
			ActivePackModuleIds = null;
			ActiveVariantSelections = null;
		}

		/// <summary>
		/// gameIdentity 比对（设计 §3.3）：声明身份的包与 GameBase.csv 比对，不匹配整体
		/// 回退。CSV 读取复用探测器同源的编码链（BOM → 严格 UTF-8 → Shift-JIS 932），
		/// 覆盖日文游戏主流 SJIS 编码；读不出/解析失败按不匹配处理并记日志（fail-closed）。
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
				global::Emuera.Compatibility.Packs.CompatPackGameIdentity? identity = handle.Manifest.GameIdentity;
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
				byte[] bytes = System.IO.File.ReadAllBytes(csvPath);
				string? text = DecodeGameBaseCsvText(bytes, csvPath);
				if (text is null)
					return false;
				return CompatPackGameIdentityCheck.TryParseGameBaseCsv(text, out gameCode, out version);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		/// <summary>
		/// GameBase.csv 的编码探测解码链：BOM（UTF-16 LE / UTF-8）→ 严格 UTF-8 → Shift-JIS 932。
		/// 引擎既有 CSV 路径（EraStreamReader/Preload）走 Config.Encode，本移植固定 UTF-8 且
		/// 仅带 BOM 探测，覆盖不了日文游戏主流的无 BOM SJIS；宿主侧对同一文件的既有探测
		/// （GameContentProbe.Decode）即本链，此处按同语义实现（其入口为 private，无法直接
		/// 复用）。任一环节解码失败返回 null——调用方按身份不匹配处理并记日志（fail-closed）。
		/// </summary>
		static string? DecodeGameBaseCsvText(byte[] bytes, string csvPath)
		{
			string? text = TryDecodeWithEncodingChain(bytes);
			if (text is null)
				global::GenericUtils.Error("[LOAD] CompatPack GameBase.csv decode failed (treated as identity mismatch): " + csvPath);
			return text;
		}

		static string? TryDecodeWithEncodingChain(byte[] bytes)
		{
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
				return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
				return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
			try
			{
				return new UTF8Encoding(false, true).GetString(bytes);
			}
			catch (DecoderFallbackException)
			{
				try
				{
					Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
					return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
						.GetString(bytes);
				}
				catch (Exception)
				{
					return null;
				}
			}
		}
	}
}
