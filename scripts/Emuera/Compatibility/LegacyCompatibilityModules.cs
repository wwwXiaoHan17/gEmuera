#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GEmuera.Core.Compatibility;

namespace MinorShift.Emuera.Compatibility
{
	/// <summary>
	/// A legacy bridge module owns only the profile-specific surface that can be
	/// projected onto the existing Parser/VM handler implementation.
	/// </summary>
	internal interface ILegacyCompatibilityModule
	{
		string ModuleId { get; }
		void Declare(LegacyCompatibilityProfileBuilder builder);
		void Apply(LegacyCompatibilityProfileBuilder builder);
	}

	/// <summary>
	/// 模块声明的指令 handler 变体选择。同名指令在不同方言上游里可能保持名字但更换文法
	/// （如 FOR 的计数形态、SETBGIMAGE 的双变体）；模块在这里只声明"选哪个变体"这一数据，
	/// 变体的实际构造由 FunctionIdentifier 在会话注册表投影时完成，保持 handler 封装。
	/// SharedTable 表示显式回退到共享 handler 表的原条目（用于子方言覆盖基线模块的替换）。
	/// </summary>
	internal enum LegacyInstructionVariant
	{
		/// <summary>使用共享 handler 表的原条目，不做替换。</summary>
		SharedTable,
		/// <summary>v24 的 FOR 计数文法（REPEAT 内核 + ForNext 实参构造器）。</summary>
		ForCountV24,
		/// <summary>v24 的 SETBGIMAGE。</summary>
		SetBgImageV24,
		/// <summary>snake 的 SETBGIMAGE。</summary>
		SetBgImageSnake,
	}

	/// <summary>
	/// 内置兼容包变体注册表：builtin 名 × 指令 → LegacyInstructionVariant。
	/// 这是包声明 builtin:* 变体选择与引擎 handler 构造知识（FunctionIdentifier.
	/// CreateProfileInstruction）之间的唯一映射层——注册表只列真实存在 handler 变体的
	/// 组合，未注册组合在加载校验段拒载（CompatPackRules 组合级校验吃本表导出的组合集）。
	/// </summary>
	internal static class BuiltinCompatPackVariants
	{
		// 元组键用默认比较器（元素 string 默认 Ordinal，与规范化后的大小写语义一致）。
		static readonly Dictionary<(string Variant, string Instruction), LegacyInstructionVariant> Map =
			new()
			{
				[("builtin:v24", "FOR")] = LegacyInstructionVariant.ForCountV24,
				[("builtin:v24", "SETBGIMAGE")] = LegacyInstructionVariant.SetBgImageV24,
				[("builtin:snake", "FOR")] = LegacyInstructionVariant.SharedTable,
				[("builtin:snake", "SETBGIMAGE")] = LegacyInstructionVariant.SetBgImageSnake,
			};

		public static bool TryResolve(string variantName, string instructionName, out LegacyInstructionVariant variant)
		{
			variant = LegacyInstructionVariant.SharedTable;
			return Map.TryGetValue((variantName, instructionName), out variant);
		}

		/// <summary>组合表导出（加载校验段消费）：变体名 → 可作用指令集（规范化大写）。</summary>
		public static IReadOnlyDictionary<string, IReadOnlySet<string>> SupportedSelections()
		{
			var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
			foreach (KeyValuePair<(string Variant, string Instruction), LegacyInstructionVariant> entry in Map)
			{
				if (!result.TryGetValue(entry.Key.Variant, out HashSet<string>? instructions))
				{
					instructions = new HashSet<string>(StringComparer.Ordinal);
					result[entry.Key.Variant] = instructions;
				}
				instructions.Add(entry.Key.Instruction);
			}
			return result.ToDictionary(
				pair => pair.Key,
				pair => (IReadOnlySet<string>)pair.Value,
				StringComparer.Ordinal);
		}
	}

	/// <summary>
	/// Composes the legacy bridge from the exact frozen Core module closure.
	/// The complete legacy handler tables remain implementation detail; only
	/// these module declarations decide their parser-visible surfaces.
	/// </summary>
	internal static class LegacyCompatibilityModuleCatalog
	{
		internal const string V24ModuleId = "gemuera.v24";
		private static readonly ILegacyCompatibilityModule[] modules =
		{
			new LegacyV24CompatibilityModule(),
		};

		private static readonly IReadOnlyDictionary<string, ILegacyCompatibilityModule> modulesById =
			new ReadOnlyDictionary<string, ILegacyCompatibilityModule>(
				modules.ToDictionary(module => module.ModuleId, StringComparer.Ordinal));

		private static readonly IReadOnlyDictionary<string, ISet<string>> expectedModuleClosures =
			new ReadOnlyDictionary<string, ISet<string>>(
				new Dictionary<string, ISet<string>>(StringComparer.Ordinal)
				{
					["v24pure"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId },
				});

		public static LegacyCompatibilityProfile Compose(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			if (!expectedModuleClosures.ContainsKey(plan.ProfileId))
			{
				throw new InvalidOperationException(
					$"Compatibility profile '{plan.ProfileId}' has no legacy module composition.");
			}
			ISet<string> expectedModules = expectedModuleClosures[plan.ProfileId];

			var selectedModules = new HashSet<string>(
				plan.Dialect.Modules.Select(module => module.ModuleId),
				StringComparer.Ordinal);
			// 包合成模块白名单是 plan 的一等数据（CompatPackPlanAssembler 固化）：
			// 不再依赖宿主进程静态，也不需要在会话结束时手动复位。
			var packModules = new HashSet<string>(plan.Dialect.PackModuleIds, StringComparer.Ordinal);
			if (packModules.Overlaps(modulesById.Keys))
			{
				throw new InvalidOperationException(
					"Pack module whitelist must not contain built-in dialect modules.");
			}
			foreach (string moduleId in selectedModules)
			{
				if (expectedModules.Contains(moduleId) || packModules.Contains(moduleId))
					continue;
				throw new InvalidOperationException(
					$"Legacy profile '{plan.ProfileId}' does not match its exact dialect module closure.");
			}
			foreach (string expected in expectedModules)
			{
				if (!selectedModules.Contains(expected))
				{
					throw new InvalidOperationException(
						$"Legacy profile '{plan.ProfileId}' does not match its exact dialect module closure.");
				}
			}
			var packModuleIdsInClosure = new HashSet<string>(
				selectedModules.Where(id => packModules.Contains(id)), StringComparer.Ordinal);

			var builder = new LegacyCompatibilityProfileBuilder(plan, scopedVariableInstructionsEnabled);
			foreach (ILegacyCompatibilityModule module in modules)
			{
				// 记录每个 Declare 名字的归属模块，供"未选中模块"诊断提示使用。
				builder.SetDeclaringModule(module.ModuleId);
				module.Declare(builder);
			}
			// v24pure 基线隐藏面（原内置 snake 模块 Declare 的职责，P-D 步 5 迁出）：
			// 先隐藏 snake 专有名字，再由选中的包/模块在 Apply/ApplyPackSurface 阶段暴露。
			// 归属记为第一方包（不是已退役的 game.snake 模块）：v24pure 查询这些名字时给出
			// "安装/选择 pack.gemuera.snake" 的可操作提示；包会话内该模块在闭包中则不再提示。
			builder.SetDeclaringModule(SnakeLegacySurfaceNames.OwnerPackId);
			builder.DeclareInstructionNames(SnakeLegacySurfaceNames.SnakeOnlyInstructionNames);
			builder.DeclareFunctionNames(SnakeLegacySurfaceNames.SnakeOnlyFunctionNames);
			foreach (DialectModuleSnapshot selectedModule in plan.Dialect.Modules)
			{
				if (packModuleIdsInClosure.Contains(selectedModule.ModuleId))
					continue; // 包合成模块没有 ILegacyCompatibilityModule；其表面贡献在下方统一回放。
				if (!modulesById.ContainsKey(selectedModule.ModuleId))
				{
					throw new InvalidOperationException(
						$"Legacy profile '{plan.ProfileId}' selected unsupported module '{selectedModule.ModuleId}'.");
				}
				ILegacyCompatibilityModule module = modulesById[selectedModule.ModuleId];
				builder.SetDeclaringModule(module.ModuleId);
				module.Apply(builder);
			}

			// 策略激活与"哪个内置模块被选中"解耦：policy 对象从 plan.CapabilityIds 派生。
			// 内置 profile 仍得到与旧实现相同的布尔组合；社区包在 v24pure 基线上声明同一组
			// capability 即可激活对应行为，不再要求引擎内置模块仍在场（P-D 前提）。
			// snake 内置模块已退役（P-D 步 5）：policy 只由 capability 派生；
			// pack.gemuera.snake 声明 19 个 Wired capability ⇒ IsEnabled 为真。
			builder.SetSnakePolicy(LegacySnakeCompatibilityPolicy.FromCapabilities(
				plan.CapabilityIds, moduleSelected: false));
			// erafl 内置模块已退役（P-D 步 4）：policy 只由 capability 派生；
			// pack.gemuera.erafl 声明 10 个 RequiredWiredCapabilityIds ⇒ IsEnabled 为真。
			builder.SetEraFlPolicy(LegacyEraFlCompatibilityPolicy.FromCapabilities(
				plan.CapabilityIds, moduleSelected: false));
			// megaten 内置模块已退役（P-D 步 2）：policy 只由 capability 派生，
			// pack.gemuera.megaten 声明 3 个 RequiredCapabilities ⇒ IsEnabled 为真（语义等价）。
			builder.SetMegatenPolicy(LegacyMegatenCompatibilityPolicy.FromCapabilities(
				plan.CapabilityIds, moduleSelected: false));

			// 蛇系函数参数契约与 module.Apply 解耦：完整 snake capability 集在场时，
			// 社区包在 v24pure 基线上也能激活同名函数的重载差异契约。
			bool snakeCapabilitiesComplete = GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities
				.RequiredCapabilityIds.All(plan.CapabilityIds.Contains);
			if (snakeCapabilitiesComplete)
			{
				// P-D 步 5：内置 snake 模块已退役；完整 snake capability 集由第一方包声明。
				// 归属模块取计划里的包模块 id（单包会话 = pack.gemuera.snake）。
				builder.SetDeclaringModule(
					plan.Dialect.PackModuleIds.Count > 0 ? plan.Dialect.PackModuleIds[0] : "");
				builder.ActivateDialectFunctionContracts(SnakeLegacySurfaceNames.DivergentFunctionContractNames);
			}

			if (packModuleIdsInClosure.Count > 0)
				ApplyPackSurface(plan, packModuleIdsInClosure, builder);

			IReadOnlyDictionary<string, string> packVariantSelections = plan.Dialect.VariantSelections;
			if (packVariantSelections.Count > 0)
			{
				foreach (KeyValuePair<string, string> selection in packVariantSelections)
				{
					if (!BuiltinCompatPackVariants.TryResolve(selection.Value, selection.Key, out LegacyInstructionVariant variant))
					{
						throw new InvalidOperationException(
							$"内置变体 '{selection.Value}' 未注册指令 '{selection.Key}' 的 handler 变体（组合校验应已在加载段拒载）。");
					}
					builder.SubstituteInstruction(selection.Key, variant);
				}
			}

			return builder.Build();
		}

		/// <summary>
		/// 包表面差量回放：从「组装 plan - 同 profile 无包基线 plan」反推包的 register/hide，
		/// 在模块 Apply 之后生效。注册项按 plan 描述符归属（ModuleId）分组回放；隐藏差量
		/// 单包会话归属到该包、多包会话不记录归属（plan 不携带每包隐藏归属）。
		/// 纯靠 plan 数据重建，不需要包句柄——Create/Compose 签名不变，无包路径零开销。
		/// </summary>
		static void ApplyPackSurface(CompatibilityPlan plan, HashSet<string> packModuleIds, LegacyCompatibilityProfileBuilder builder)
		{
			CompatibilityPlan baseline = GEmuera.Core.Compatibility.BuiltInDialectCatalog.CreateLegacySessionPlan(plan.ProfileId);

			var hiddenInstructions = baseline.Dialect.Instructions.Keys
				.Where(name => !plan.Dialect.Instructions.ContainsKey(name))
				.ToList();
			var hiddenFunctions = baseline.Dialect.Functions.Keys
				.Where(name => !plan.Dialect.Functions.ContainsKey(name))
				.ToList();

			// 防御：相对基线新增的指令名，归属必须恰为包模块（反推口径自证）。
			foreach (var group in plan.Dialect.Instructions.Values
				.Where(descriptor => !baseline.Dialect.Instructions.ContainsKey(descriptor.Name))
				.GroupBy(descriptor => descriptor.ModuleId))
			{
				if (!packModuleIds.Contains(group.Key))
					throw new InvalidOperationException(
						$"Pack surface replay found instruction ownership outside pack modules: '{group.Key}'.");
			}

			// 注册项按 plan 描述符的真实归属（ModuleId = 声明它的包）分组回放：每个包只
			// Expose 自己的注册名。此前对每个包重放全部包的累计名单，SetDeclaringModule
			// 与实际归属不符（多包时名字归属会错记到迭代在前的包）。
			foreach (var group in plan.Dialect.Instructions.Values
				.Where(descriptor => packModuleIds.Contains(descriptor.ModuleId))
				.GroupBy(descriptor => descriptor.ModuleId, descriptor => descriptor.Name))
			{
				builder.SetDeclaringModule(group.Key);
				builder.ExposeInstructionNames(group);
			}
			foreach (var group in plan.Dialect.Functions.Values
				.Where(descriptor => packModuleIds.Contains(descriptor.ModuleId))
				.GroupBy(descriptor => descriptor.ModuleId, descriptor => descriptor.Name))
			{
				builder.SetDeclaringModule(group.Key);
				builder.ExposeFunctionNames(group);
			}

			// 隐藏差量：组装段只保留合并差集（removedInstructions/removedFunctions 不带
			// 每包归属），plan 数据无法反推唯一声明包。单包会话归属精确到该包；多包会话
			// 不记录 owner（宁缺毋错）——TryGetUnselectedModuleHint 对包归属名恒不提示
			//（包模块必在本会话闭包内），省略 owner 不改变可观察行为，只消除错误归属。
			if (hiddenInstructions.Count > 0)
			{
				builder.SetDeclaringModule(packModuleIds.Count == 1 ? FirstPackId(packModuleIds) : "");
				builder.HideInstructionNames(hiddenInstructions);
			}
			if (hiddenFunctions.Count > 0)
			{
				builder.SetDeclaringModule(packModuleIds.Count == 1 ? FirstPackId(packModuleIds) : "");
				builder.HideFunctionNames(hiddenFunctions);
			}
		}

		static string FirstPackId(HashSet<string> packModuleIds)
		{
			// 单包集合的唯一元素；HashSet 单元素枚举即该元素（确定性）。
			foreach (string packId in packModuleIds)
				return packId;
			return "";
		}
	}

	internal sealed class LegacyCompatibilityProfileBuilder
	{
		private readonly CompatibilityPlan plan;
		private readonly HashSet<string> hiddenInstructionNames = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> hiddenFunctionNames = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> scopedInstructionNames = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> methodProjectedFunctionNames = new HashSet<string>(StringComparer.Ordinal);
		// 隐藏名 → 声明它的方言模块 id（Declare/Hide 阶段记录，Expose 移除）。
		// 用于"该标识符属于未选中模块"的诊断提示（如 v24pure 下提示改用 snake）。
		private readonly Dictionary<string, string> hiddenNameOwners = new Dictionary<string, string>(StringComparer.Ordinal);
		// 指令名（大写规范化）→ 模块声明的 handler 变体。Declare/Apply 阶段按注册顺序
		// 后写覆盖（Compose 保证 Apply 晚于全部 Declare，因此选中模块的变体覆盖基线声明）。
		private readonly Dictionary<string, LegacyInstructionVariant> instructionVariants = new Dictionary<string, LegacyInstructionVariant>(StringComparer.Ordinal);
		// 激活"蛇系参数契约"的函数名集合（snake 模块 Apply 声明）：
		// 这些同名函数在 snake 与 v24 参考中重载形态不同，DialectFunctionContracts
		/// 据此选择 CheckArgumentType 包装；checker 实现仍由该类集中持有。
		private readonly HashSet<string> dialectFunctionContractNames = new HashSet<string>(StringComparer.Ordinal);
		private string declaringModuleId = "";
		private ISnakeCompatibilityPolicy snake = DisabledSnakeCompatibilityPolicy.Instance;
		private IEraFlCompatibilityPolicy eraFl = DisabledEraFlCompatibilityPolicy.Instance;
		// megaten 策略默认 Disabled：未选中模块不 Apply 时保持基线行为。
		private IMegatenCompatibilityPolicy megaten = DisabledMegatenCompatibilityPolicy.Instance;

		private readonly bool scopedVariableInstructionsEnabled;

		public LegacyCompatibilityProfileBuilder(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
			this.scopedVariableInstructionsEnabled = scopedVariableInstructionsEnabled;
		}

		/// <summary>
		/// Compose 在每个模块的 Declare/Apply 前设置当前模块 id，使隐藏归属可溯源。
		/// </summary>
		public void SetDeclaringModule(string moduleId)
		{
			declaringModuleId = moduleId ?? "";
		}

		private void RecordHiddenOwner(string name)
		{
			if (!string.IsNullOrEmpty(declaringModuleId) && !hiddenNameOwners.ContainsKey(name))
				hiddenNameOwners[name] = declaringModuleId;
		}

		private void ForgetHiddenOwner(string name)
		{
			hiddenNameOwners.Remove(name);
		}

		public void DeclareInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				hiddenInstructionNames.Add(name);
				RecordHiddenOwner(name);
			}
		}

		public void DeclareFunctionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				hiddenFunctionNames.Add(name);
				RecordHiddenOwner(name);
			}
		}

		public void DeclareScopedInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				scopedInstructionNames.Add(name);
		}

		public void DeclareMethodProjectedFunctionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				methodProjectedFunctionNames.Add(name);
		}

		public void ExposeInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				hiddenInstructionNames.Remove(name);
				ForgetHiddenOwner(name);
			}
		}

		public void ExposeFunctionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				hiddenFunctionNames.Remove(name);
				ForgetHiddenOwner(name);
			}
		}

		/// <summary>
		/// Removes an otherwise inherited expression function from a selected
		/// module surface. This is needed where the Snake fork intentionally
		/// diverges from its v24 dependency rather than taking a pure union.
		/// </summary>
		public void HideFunctionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				hiddenFunctionNames.Add(name);
				RecordHiddenOwner(name);
			}
		}

		/// <summary>
		/// Apply 阶段的指令隐藏：选中本模块的会话把基线可见的指令从表面移除
		///（如 v18 会话隐藏 v24 后增指令）。与 HideFunctionNames 对称。
		/// </summary>
		public void HideInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				hiddenInstructionNames.Add(name);
				RecordHiddenOwner(name);
			}
		}

		public void SetSnakePolicy(ISnakeCompatibilityPolicy policy)
		{
			snake = policy ?? throw new ArgumentNullException(nameof(policy));
		}

		/// <summary>会话计划携带的 quirk capability 清单（模块声明进 profile，policy 据此派生）。</summary>
		public GEmuera.Core.Compatibility.CompatibilityPlan Plan => plan;

		public void SetEraFlPolicy(IEraFlCompatibilityPolicy policy)
		{
			eraFl = policy ?? throw new ArgumentNullException(nameof(policy));
		}

		/// <summary>
		/// 模块的指令替换贡献：声明"该名字在本会话中使用哪个 handler 变体"。
		/// 名字按 IsInstructionVisible 同一语义规范化（Trim + 大写）。
		/// 基线模块（如 v24）在 Apply 注册默认变体，子方言模块在后续 Apply 中
		/// 覆盖为自己的变体或显式回退 SharedTable。
		/// </summary>
		public void SubstituteInstruction(string name, LegacyInstructionVariant variant)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Instruction name must not be empty.", nameof(name));
			instructionVariants[name.Trim().ToUpperInvariant()] = variant;
		}

		/// <summary>
		/// 模块的函数参数契约贡献：声明"本会话对这些同名函数使用蛇系重载契约"。
		/// 名单来自 snake 与 v24 参考注册表的重载差异差集；仅选中该模块的会话激活。
		/// </summary>
		public void ActivateDialectFunctionContracts(IEnumerable<string> names)
		{
			if (names == null)
				throw new ArgumentNullException(nameof(names));
			foreach (string name in names)
			{
				if (string.IsNullOrWhiteSpace(name))
					throw new ArgumentException("Function contract names must not be empty.", nameof(names));
				dialectFunctionContractNames.Add(name.Trim());
			}
		}

		// megaten：策略由 plan.CapabilityIds 派生（P-D 步 2 起由 pack.gemuera.megaten 提供）。
		public void SetMegatenPolicy(IMegatenCompatibilityPolicy policy)
		{
			megaten = policy ?? throw new ArgumentNullException(nameof(policy));
		}

		public LegacyCompatibilityProfile Build()
		{
			return new LegacyCompatibilityProfile(
				plan.ProfileId,
				plan,
				scopedVariableInstructionsEnabled,
				snake,
				eraFl,
				// megaten 策略随 Build 传入 LegacyCompatibilityProfile。
				megaten,
				hiddenInstructionNames,
				hiddenFunctionNames,
				scopedInstructionNames,
				methodProjectedFunctionNames,
				instructionVariants,
				dialectFunctionContractNames,
				hiddenNameOwners);
		}
	}

	internal sealed class LegacyV24CompatibilityModule : ILegacyCompatibilityModule
	{
		// This Godot-port registration is not present in either checked-in v24 or
		// Snake source registry, so it must not leak through either dialect surface.
		private static readonly IReadOnlyCollection<string> PortOnlyInstructionNames =
			Array.AsReadOnly(new[] { "OUTPUTLOG" });

		// The shared handler store retains a Snake UI instruction for this key, but
		// neither upstream exposes that instruction. v24 restores its expression
		// function through the explicit METHOD projection declared below.
		private static readonly IReadOnlyCollection<string> HiddenCollisionInstructionNames =
			Array.AsReadOnly(new[] { "BITMAP_CACHE_ENABLE" });

		// Both references expose VARI/VARS only when the session's
		// scoped-variable setting is enabled. They are v24 capabilities, not
		// Snake-only extensions.
		private static readonly IReadOnlyCollection<string> ScopedInstructionNames =
			Array.AsReadOnly(new[] { "VARI", "VARS" });

		// v24 exposes this expression function as a METHOD instruction, while the
		// shared handler store retains the Snake-side instruction of the same name.
		private static readonly IReadOnlyCollection<string> MethodProjectedFunctionNames =
			Array.AsReadOnly(new[] { "BITMAP_CACHE_ENABLE" });

		public string ModuleId => "gemuera.v24";
			public void Declare(LegacyCompatibilityProfileBuilder builder)
			{
				builder.DeclareInstructionNames(PortOnlyInstructionNames);
				builder.DeclareInstructionNames(HiddenCollisionInstructionNames);
				builder.DeclareScopedInstructionNames(ScopedInstructionNames);
				builder.DeclareMethodProjectedFunctionNames(MethodProjectedFunctionNames);
			}
		public void Apply(LegacyCompatibilityProfileBuilder builder)
		{
			// v24 基线变体：所有含 v24 基座的会话（v24pure/snake/erafl）先落这两条，
			// 子方言模块在自己的 Apply 里按需覆盖。
			builder.SubstituteInstruction("FOR", LegacyInstructionVariant.ForCountV24);
			builder.SubstituteInstruction("SETBGIMAGE", LegacyInstructionVariant.SetBgImageV24);
		}
	}

	internal sealed class DisabledSnakeCompatibilityPolicy : ISnakeCompatibilityPolicy
	{
		public static readonly DisabledSnakeCompatibilityPolicy Instance = new DisabledSnakeCompatibilityPolicy();
		public bool IsEnabled => false;
		public bool UsesParserDiagnostics => false;
		public bool AllowsUserDefinedVariableResolution => false;
		public bool AllowsPrivateArguments => false;
		public bool AllowsExtraCallArguments => false;
		public bool AllowsScopedVariablePreRegistration => false;
		public bool ContinuesAfterStartupFault => false;
		public bool UsesFastDisplayRefresh => false;
		public bool UsesLazyResourceIndex => false;
		public bool UsesTimesClamp => false;
		public bool UsesRandClamp => false;
		public bool UsesMaskAlphaChannel => false;
		public bool UsesEscapeESequence => false;
		public bool UsesThrowEvent => false;
		public bool UsesBeforeErrorEvent => false;
		public bool UsesRandomizeReseed => false;
		public bool UsesRelationWithoutMastername => false;
		public bool MergesPresetErdFromErbDir => false;
		public bool AllowsOutKeyword => false;
		public bool UsesVariadicStrip => false;
		public bool UsesFloatTypeSystem => false;
	}

	internal sealed class LegacySnakeCompatibilityPolicy : ISnakeCompatibilityPolicy
	{
		// quirk capability 账本驱动：每个布尔属性 = snake profile 声明的对应 id 是否在场
		//（映射表见 SnakeCompatibilityCapabilities.PolicyPropertyByCapabilityId，
		// 映射穷尽性由 SurfaceSmoke 反射断言把关）。
		private readonly IReadOnlyCollection<string> capabilities;

		private readonly bool moduleSelected;

		private LegacySnakeCompatibilityPolicy(IReadOnlyCollection<string> capabilities, bool moduleSelected)
		{
			this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
			this.moduleSelected = moduleSelected;
		}

		public static LegacySnakeCompatibilityPolicy FromCapabilities(
			IReadOnlyCollection<string> capabilityIds, bool moduleSelected)
		{
			return new LegacySnakeCompatibilityPolicy(capabilityIds, moduleSelected);
		}

		// 模块语义仍以选中为准；社区包在 v24pure 基线上声明完整 snake capability 集时，
		// IsEnabled 同样为 true（P-D 后内置模块退场，包成为唯一声明载体）。
		// 只由 catalog 标为 Wired 的子集推断：DiagnosticOnly 账本项不能放大行为（设计 §6.3）。
		public bool IsEnabled => moduleSelected
			|| GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RequiredWiredCapabilityIds.All(capabilities.Contains);
		public bool UsesParserDiagnostics => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ParserDiagnostics);
		public bool AllowsUserDefinedVariableResolution => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.UserDefinedVariableResolution);
		public bool AllowsPrivateArguments => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.PrivateArguments);
		public bool AllowsExtraCallArguments => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ExtraCallArguments);
		public bool AllowsScopedVariablePreRegistration => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ScopedVariablePreRegistration);
		public bool ContinuesAfterStartupFault => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ContinueAfterStartupFault);
		public bool UsesFastDisplayRefresh => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.FastDisplayRefresh);
		public bool UsesLazyResourceIndex => true;
		public bool UsesTimesClamp => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.TimesClamp);
		public bool UsesRandClamp => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RandClamp);
		public bool UsesMaskAlphaChannel => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.MaskAlphaChannel);
		public bool UsesEscapeESequence => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.EscapeESequence);
		public bool UsesThrowEvent => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ThrowEvent);
		public bool UsesBeforeErrorEvent => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.BeforeErrorEvent);
		public bool UsesRandomizeReseed => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RandomizeReseed);
		public bool UsesRelationWithoutMastername => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RelationWithoutMastername);
		public bool MergesPresetErdFromErbDir => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.PresetErdMerge);
		public bool AllowsOutKeyword => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.OutKeyword);
		public bool UsesVariadicStrip => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.VariadicStrip);
		public bool UsesFloatTypeSystem => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.FloatTypeSystem);
	}

	// megaten 默认策略：三个 flag 全 false，保证 v24pure/snake/erafl 会话下
	// 13 处引擎门控点的布尔表达式与回退前基线完全等价。
	internal sealed class DisabledMegatenCompatibilityPolicy : IMegatenCompatibilityPolicy
	{
		public static readonly DisabledMegatenCompatibilityPolicy Instance = new DisabledMegatenCompatibilityPolicy();
		public bool IsEnabled => false;
		public bool UsesVariableCaseForFunctionLabelLookup => false;
		public bool AllowsOutAsVariableNameAfterRefKeyword => false;
		public bool AllowsPrivateSystemVariableShadowing => false;
		public bool UsesStandardColorNameTable => false;
		public bool TreatZeroAlphaRgbHexAsOpaque => false;
	}

	// megaten 策略：moduleSelected 保持内置 profile 语义；三个 flag 由 plan capability 派生，
	// 因此社区包在 v24pure 基线上声明对应 behavior id 也能激活。
	internal sealed class LegacyMegatenCompatibilityPolicy : IMegatenCompatibilityPolicy
	{
		static readonly IReadOnlySet<string> RequiredCapabilities = new HashSet<string>(StringComparer.Ordinal)
		{
			MegatenCompatibilityModule.LabelLookupCaseBehavior,
			MegatenCompatibilityModule.RefOutNameBehavior,
			MegatenCompatibilityModule.PrivateSystemShadowBehavior,
		};

		private readonly IReadOnlyCollection<string> capabilities;
		private readonly bool moduleSelected;

		private LegacyMegatenCompatibilityPolicy(
			IReadOnlyCollection<string> capabilities,
			bool moduleSelected)
		{
			this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
			this.moduleSelected = moduleSelected;
		}

		public static LegacyMegatenCompatibilityPolicy FromCapabilities(
			IReadOnlyCollection<string> capabilityIds,
			bool moduleSelected)
		{
			return new LegacyMegatenCompatibilityPolicy(capabilityIds, moduleSelected);
		}

		public bool IsEnabled => moduleSelected || RequiredCapabilities.All(capabilities.Contains);
		public bool UsesVariableCaseForFunctionLabelLookup =>
			capabilities.Contains(MegatenCompatibilityModule.LabelLookupCaseBehavior);
		public bool AllowsOutAsVariableNameAfterRefKeyword =>
			capabilities.Contains(MegatenCompatibilityModule.RefOutNameBehavior);
		public bool AllowsPrivateSystemVariableShadowing =>
			capabilities.Contains(MegatenCompatibilityModule.PrivateSystemShadowBehavior);
		public bool UsesStandardColorNameTable =>
			capabilities.Contains(MegatenCompatibilityModule.StandardColorNameBehavior);
		public bool TreatZeroAlphaRgbHexAsOpaque =>
			capabilities.Contains(MegatenCompatibilityModule.HtmlRgbZeroAlphaBehavior);
	}

	internal sealed class DisabledEraFlCompatibilityPolicy : IEraFlCompatibilityPolicy
{
    public static readonly DisabledEraFlCompatibilityPolicy Instance = new DisabledEraFlCompatibilityPolicy();
    public bool IsEnabled => false;
    public bool UsesExtendedDisplayHistory => false;
    public bool AllowsFloatLiterals => false;
    public bool AllowsOutKeyword => false;
    public bool UsesSafeArithmeticGuard => false;
    public bool AllowsExtendedHtmlAttributes => false;
    public bool StripsLeadingBomFromDataTableXml => false;
    public string TaskStartRoomLookupFunction => string.Empty;
    public string GMapQuestType => string.Empty;
    public bool IsOmittedDefaultArgument(char currentToken) => false;
    public bool IsPointerInputMetadataOption(string optionText) => false;
    // Why（对照源码 MainWindow.MouseDown / EmueraConsole.InputMouseKey）：RESULT:1 的鼠标
    // 按钮协议在所有 Emuera 实现中都是 1=左、2=右、3=中（文档约定），与是否为 eraFL 无关。
    // 非 eraFL 策略之前原样返回宿主 VK（中键 0x04 → RESULT:1=4），依赖 RESULT:1==3 分支的
    // snake/v24 游戏会读到错误值。What/How：与 eraFL 共用同一 0x04→3 归一化，其它值不改写。
    public int NormalizePointerButtonResult(int mouseButton) => EraFlCompatibilityModule.NormalizePointerButtonResult(mouseButton);
    public string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger) => input ?? string.Empty;
    public bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString) => false;
    public bool TryRecoverQuestStartRoomIndex(string functionName, long returnedRoomIndex, string requestedRoomTag, long mapId, string questType, string[,] mapData, out long recoveredRoomIndex)
    {
        recoveredRoomIndex = returnedRoomIndex;
        return false;
    }
    public bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes) => false;
    public bool TryParseGMapDataTableFromXml(string schemaXml, string dataXml, out System.Data.DataTable table, out IReadOnlyList<EraFlGMapNode> nodes)
    {
        table = new System.Data.DataTable();
        nodes = Array.Empty<EraFlGMapNode>();
        return false;
    }
}

internal sealed class LegacyEraFlCompatibilityPolicy : IEraFlCompatibilityPolicy
{
    // 布尔型/开关型 quirk 由 capability 账本派生（erafl profile 或社区包声明），
    // 算法型行为（GMap、指针归一化）仍委托 Core 模块的确定性实现，但按 capability 放行。
    private readonly IReadOnlyCollection<string> capabilities;
    private readonly bool moduleSelected;
    private bool HasDynamicMap => capabilities.Contains(EraFlCompatibilityModule.DynamicMapCapability);
    private bool HasPointerButton => capabilities.Contains(EraFlCompatibilityModule.PointerButtonCapability);

    private LegacyEraFlCompatibilityPolicy(IReadOnlyCollection<string> capabilities, bool moduleSelected)
    {
        this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        this.moduleSelected = moduleSelected;
    }

    public static LegacyEraFlCompatibilityPolicy FromCapabilities(
        IReadOnlyCollection<string> capabilityIds, bool moduleSelected)
    {
        return new LegacyEraFlCompatibilityPolicy(capabilityIds, moduleSelected);
    }

    // 只由 catalog 标为 Wired 的子集推断：DiagnosticOnly 表面 capability（如 markup.div-v2.v1）
    // 只是 profile 账本，不再参与 IsEnabled 推断（设计 §6.3；Lead 裁定的有意行为变化）。
    public bool IsEnabled => moduleSelected || EraFlCompatibilityModule.RequiredWiredCapabilityIds.All(capabilities.Contains);
    public bool UsesExtendedDisplayHistory => capabilities.Contains(EraFlCompatibilityModule.DisplayExtendedHistoryBehavior);
    public bool AllowsFloatLiterals => capabilities.Contains(EraFlCompatibilityModule.FloatLiteralsBehavior);
    public bool AllowsOutKeyword => capabilities.Contains(EraFlCompatibilityModule.OutKeywordBehavior);
    public bool UsesSafeArithmeticGuard => capabilities.Contains(EraFlCompatibilityModule.SafeArithmeticGuardBehavior);
    public bool AllowsExtendedHtmlAttributes => capabilities.Contains(EraFlCompatibilityModule.ExtendedHtmlAttributesBehavior);
    public bool StripsLeadingBomFromDataTableXml => capabilities.Contains(EraFlCompatibilityModule.DataTableXmlLeadingBomBehavior);
    public string TaskStartRoomLookupFunction => HasDynamicMap ? EraFlCompatibilityModule.TaskStartRoomLookupFunction : string.Empty;
    public string GMapQuestType => HasDynamicMap ? EraFlCompatibilityModule.GMapQuestType : string.Empty;
    public bool IsOmittedDefaultArgument(char currentToken) =>
        capabilities.Contains(EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior)
        && EraFlCompatibilityModule.IsOmittedDefaultArgument(currentToken);
    public bool IsPointerInputMetadataOption(string optionText) =>
        HasPointerButton && EraFlCompatibilityModule.IsPointerInputMetadataOption(optionText);
    public int NormalizePointerButtonResult(int mouseButton) => EraFlCompatibilityModule.NormalizePointerButtonResult(mouseButton);
    public string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger) =>
        HasPointerButton
            ? EraFlCompatibilityModule.NormalizePointerIntegerSubmission(input, mouseButton, waitingForInteger)
            : input ?? string.Empty;
    public bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString) =>
        capabilities.Contains(EraFlCompatibilityModule.PointerBlankStringBehavior)
        && EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(mouseButton, waitingForString);
    public bool TryRecoverQuestStartRoomIndex(string functionName, long returnedRoomIndex, string requestedRoomTag, long mapId, string questType, string[,] mapData, out long recoveredRoomIndex)
    {
        if (!HasDynamicMap)
        {
            recoveredRoomIndex = returnedRoomIndex;
            return false;
        }
        return EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
            functionName,
            returnedRoomIndex,
            requestedRoomTag,
            mapId,
            questType,
            mapData,
            out recoveredRoomIndex);
    }
    public bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes)
    {
        if (!HasDynamicMap || nodes == null)
            return false;
        var coreNodes = new List<EraFlCompatibilityModule.GMapNodeData>(nodes.Count);
        foreach (EraFlGMapNode node in nodes)
            coreNodes.Add(new EraFlCompatibilityModule.GMapNodeData(node.NodeId, node.NodeName, node.PathList));
        return EraFlCompatibilityModule.TryPopulateGMapRoomData(mapId, mapData, coreNodes);
    }
    public bool TryParseGMapDataTableFromXml(string schemaXml, string dataXml, out System.Data.DataTable table, out IReadOnlyList<EraFlGMapNode> nodes)
    {
        if (!HasDynamicMap)
        {
            table = new System.Data.DataTable();
            nodes = Array.Empty<EraFlGMapNode>();
            return false;
        }
        if (!EraFlCompatibilityModule.TryParseGMapDataTableFromXml(schemaXml, dataXml,
                out System.Data.DataTable? parsedTable,
                out IReadOnlyList<EraFlCompatibilityModule.GMapNodeData> coreNodes)
            || parsedTable == null)
        {
            table = new System.Data.DataTable();
            nodes = Array.Empty<EraFlGMapNode>();
            return false;
        }
        table = parsedTable;
        var projected = new List<EraFlGMapNode>(coreNodes.Count);
        foreach (EraFlCompatibilityModule.GMapNodeData node in coreNodes)
            projected.Add(new EraFlGMapNode(node.NodeId, node.NodeName ?? string.Empty, node.PathList ?? string.Empty));
        nodes = projected.AsReadOnly();
        return true;
    }
}

}
