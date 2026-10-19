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

internal static CompatPackValidationContext BuildValidationContext(string baselineProfileId)
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
                        BuildEngineFunctionHandlerNames(),
                        BuildEngineFunctionReturnTypes(),
                        baselineProfileId,
                        // v24 基线表面快照哈希（baseSurfaceHash 精确对账基准，E2-R6）：与
                        // BaselineInstructions/BaselineFunctions 同源（生成清单），显式传入而非
                        // 依赖缺省——宿主是语义校验输入的唯一提供方，缺省仅留给测试场景。
                        LegacySurfaceHash.ComputeV24SurfaceHash());
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
		/// <summary>
		/// 引擎表达式函数名 → 真实返回类型，供 manifest-only 数据包生成 descriptor；
		/// 与 handler 名全集同源（六 profile 生成清单并集）。名字以 Trim+Upper 规范化，
		/// 与 CompatPackRules 录制回放的函数名规范化一致；同一名字返回类型冲突即宿主配置错误。
		/// </summary>
		static IReadOnlyDictionary<string, string> BuildEngineFunctionReturnTypes()
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			AddFunctionReturnTypes(map, LegacyDialectInventories.V24Functions);
			AddFunctionReturnTypes(map, LegacyDialectInventories.SnakeDeltaFunctions);
			AddFunctionReturnTypes(map, LegacyDialectInventories.EraFlDeltaFunctions);
			AddFunctionReturnTypes(map, LegacyDialectInventories.EraBlueDeltaFunctions);
			AddFunctionReturnTypes(map, LegacyDialectInventories.MegatenDeltaFunctions);
			AddFunctionReturnTypes(map, LegacyDialectInventories.V18Functions);
			return map;
		}

		static void AddFunctionReturnTypes(
			Dictionary<string, string> map,
			IEnumerable<LegacyFunctionInventoryEntry> entries)
		{
			foreach (LegacyFunctionInventoryEntry entry in entries)
			{
				string key = entry.Name.Trim().ToUpperInvariant();
				if (map.TryGetValue(key, out string? existing)
					&& !string.Equals(existing, entry.ReturnType, StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						$"Engine function return type conflict for '{key}': '{existing}' vs '{entry.ReturnType}'.");
				}
				map[key] = entry.ReturnType;
			}
		}


		/// <summary>引擎包 API 版本（v1 = 1；破坏性变更时递增并拒载旧包）。</summary>
		internal const int EngineModuleApiVersion = 1;

		/// <summary>
		/// 是否显式启用了兼容包（存在启用路径）。Program.Main 早绑定路径的换绑预判：
		/// 无包时整段跳过，保持与旧路径逐字节等价；有包时才清绑→加载→重绑。
		/// </summary>
		internal static bool HasEnabledPacks()
		{
			return ReadEnabledPackPaths().Count > 0;
		}

	/// <summary>
	/// 启动期配置结果：组装/回退计划 + 会话持有的包租约（无包或失败路径 Session 为 null）。
	/// 调用方必须把两者一起交给 <see cref="Program.ConfigureCompatibilityPlan"/>——session
	/// 的生命周期由 Program 的 Stop/Restart/退出清理路径（ClearCompatibilityPlan）确定性回收。
	/// </summary>
	/// <param name="Plan">本会话绑定的计划（成功 = 组装计划；无包/失败 = 基线）。</param>
	/// <param name="Session">成功加载的包集合所有者；null = 本会话无包程序集需要回收。</param>
	internal sealed record CompatPackLaunchResult(CompatibilityPlan Plan, CompatPackSession? Session);

	/// <summary>
	/// 启动期配置：无包原样返回基线；有包则加载→身份比对→组装。任何失败回退基线
	/// （降级不变量）并记错误日志。成功路径把已加载句柄集合包装为 <see cref="CompatPackSession"/>
	/// 一并返回——plan 是纯数据，但包程序集（ALC/入口实例）的存活由会话绑定对象确定性
	/// 回收（Stop/替换时 UnloadAll，设计 §5.4）。
	/// </summary>
internal static CompatPackLaunchResult ConfigureForLaunch(CompatibilityPlan baselinePlan, string gameRoot)
        {
                IReadOnlyList<string> paths = ReadEnabledPackPaths();
                if (paths.Count == 0)
                        return new CompatPackLaunchResult(baselinePlan, null);

                // 顶层兜底：包路径上的任何异常（清单 getter、贡献回放、宿主自身缺陷……）一律
                // 降级回基线 + 错误日志，绝不让异常逃逸到 Program.Main（降级不变量）。
                CompatPackSet? loadedSet = null;
                try
                {
                        var context = BuildValidationContext(baselinePlan.ProfileId);
                        if (!CompatPackLoader.TryLoadSet(paths, context, out CompatPackSet? set, out IReadOnlyList<string> loadErrors))
                        {
                                global::GenericUtils.Error("[LOAD] CompatPack disabled (load rejected): " + string.Join("; ", loadErrors));
                                return new CompatPackLaunchResult(baselinePlan, null);
                        }
                        loadedSet = set;

                        if (!VerifyGameIdentity(set!, gameRoot, out List<string> identityErrors))
                        {
                                set!.UnloadAll();
                                global::GenericUtils.Error("[LOAD] CompatPack disabled (game identity mismatch): " + string.Join("; ", identityErrors));
                                return new CompatPackLaunchResult(baselinePlan, null);
                        }

                        if (!CompatPackPlanAssembler.TryAssemble(baselinePlan, set!.Handles, out CompatibilityPlan assembled, out IReadOnlyList<string> assemblyErrors))
                        {
                                set!.UnloadAll();
                                global::GenericUtils.Error("[LOAD] CompatPack disabled (assembly rejected): " + string.Join("; ", assemblyErrors));
                                return new CompatPackLaunchResult(baselinePlan, null);
                        }

                        foreach (CompatPackHandle handle in set!.Handles)
                        {
                                global::GenericUtils.Info(
                                        $"[LOAD] CompatPack enabled: id={handle.Manifest.PackId} version={handle.Manifest.PackVersion} "
                                        + $"engineApi={handle.Manifest.TargetEngineApi} sha256={handle.PackSha256}");
                        }
                        // 计划自身已携带 pack 模块白名单与 variantSelections；包程序集的存活
                        // 移交 CompatPackSession（随计划绑定会话，Stop/替换时确定性 UnloadAll）。
                        return new CompatPackLaunchResult(assembled, new CompatPackSession(assembled, set.Handles));
                }
                catch (Exception exception)
                {
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
                        return new CompatPackLaunchResult(baselinePlan, null);
                }
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
