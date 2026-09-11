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
	/// Composes the legacy bridge from the exact frozen Core module closure.
	/// The complete legacy handler tables remain implementation detail; only
	/// these module declarations decide their parser-visible surfaces.
	/// </summary>
	internal static class LegacyCompatibilityModuleCatalog
	{
		private const string V24ModuleId = "gemuera.v24";
		private const string SnakeModuleId = "game.snake";
		private const string EraFlModuleId = "game.erafl";
		private const string V18ModuleId = "gemuera.v18";

		private static readonly ILegacyCompatibilityModule[] modules =
		{
			new LegacyV24CompatibilityModule(),
			new LegacySnakeCompatibilityModule(),
			new LegacyEraFlCompatibilityModule(),
			new LegacyV18CompatibilityModule(),
		};

		private static readonly IReadOnlyDictionary<string, ILegacyCompatibilityModule> modulesById =
			new ReadOnlyDictionary<string, ILegacyCompatibilityModule>(
				modules.ToDictionary(module => module.ModuleId, StringComparer.Ordinal));

		private static readonly IReadOnlyDictionary<string, ISet<string>> expectedModuleClosures =
			new ReadOnlyDictionary<string, ISet<string>>(
				new Dictionary<string, ISet<string>>(StringComparer.Ordinal)
				{
					["v24pure"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId },
					["snake"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId, SnakeModuleId },
					["erafl"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId, EraFlModuleId },
					["v18"] = new HashSet<string>(StringComparer.Ordinal) { V18ModuleId },
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
			if (!selectedModules.SetEquals(expectedModules))
			{
				throw new InvalidOperationException(
					$"Legacy profile '{plan.ProfileId}' does not match its exact dialect module closure.");
			}

			var builder = new LegacyCompatibilityProfileBuilder(plan, scopedVariableInstructionsEnabled);
			foreach (ILegacyCompatibilityModule module in modules)
			{
				// 记录每个 Declare 名字的归属模块，供"未选中模块"诊断提示使用。
				builder.SetDeclaringModule(module.ModuleId);
				module.Declare(builder);
			}
			foreach (DialectModuleSnapshot selectedModule in plan.Dialect.Modules)
			{
				if (!modulesById.ContainsKey(selectedModule.ModuleId))
				{
					throw new InvalidOperationException(
						$"Legacy profile '{plan.ProfileId}' selected unsupported module '{selectedModule.ModuleId}'.");
				}
				ILegacyCompatibilityModule module = modulesById[selectedModule.ModuleId];
				builder.SetDeclaringModule(module.ModuleId);
				module.Apply(builder);
			}
			return builder.Build();
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

		public LegacyCompatibilityProfile Build()
		{
			return new LegacyCompatibilityProfile(
				plan.ProfileId,
				plan,
				scopedVariableInstructionsEnabled,
				snake,
				eraFl,
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

	internal sealed class LegacySnakeCompatibilityModule : ILegacyCompatibilityModule
	{
		// 蛇系参数契约名集：这些同名函数在 snake 与 v24 参考注册表中重载形态不同
		//（DialectFunctionContracts 逐名提供 CheckArgumentType 差异）。激活后该会话
		// 使用蛇系契约；v24pure/erafl 会话保持 v24 形态。
		private static readonly IReadOnlyCollection<string> DivergentFunctionContractNames =
			Array.AsReadOnly(new[]
			{
				"ABS", "ARGLEN", "CBGSETSPRITE", "CBRT", "EXISTVAR", "EXPONENT",
				"GETVAR", "GETVARS", "LIMIT", "LOG", "LOG10", "POWER", "SIGN",
				"SPRITECREATE", "SPRITECREATEFROMFILE", "SQRT", "TOINT",
				"UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG", "UNCHECKED_SUB",
			});

		// Public-key delta between the checked-in v24 and Snake reference
		// registries. Handler class prefixes are not dialect ownership evidence.
		private static readonly IReadOnlyCollection<string> InstructionNames =
			Array.AsReadOnly(new[]
			{
						"BITMAP_CACHE_ENABLE", "CALLSTR", "CLEARIMAGELAYER", "CLEARIMAGELAYER_ALL",
				"HTML_PRINTC", "HTML_PRINTLC", "JUMPSTR", "SET_SKIA_QUALITY", "SET_TEXT_DRAWING_MODE",
				"SETANIMETIMER", "SETIMAGELAYER", "SETIMAGELAYERL", "STRICT_FONT_FALLBACK",
				"TEXT_BGC_OFF", "TEXT_BGC_ON", "TINPUTNF", "TINPUTSNF", "TONEINPUTNF",
				"TONEINPUTSNF", "TRYCALLSTR", "TRYCCALLSTR", "TRYCJUMPSTR", "TRYJUMPSTR",
			});

		// The v24 baseline does not expose these Snake/port expression functions.
		// Keep this list at the visibility boundary so ExpressionParser cannot
		// resolve an unselected dialect capability through the static handler map.
		private static readonly IReadOnlyCollection<string> FunctionNames =
			Array.AsReadOnly(new[]
			{
				"ACOS", "ARGLEN", "ASIN", "ATAN", "BGMCONTROL", "BITGET", "BITINDEXOFFIRST",
				"BITSET", "BITTOGGLE", "CBGSETCIMG", "CEIL", "COS", "DISABLE_INPUT_MACRO",
				"DT_CELL_GETF", "DT_CELL_SETF", "ENABLE_INPUT_MACRO", "EVAL", "EVALF", "EVALS",
				"EXISTSIMAGELAYER", "FLOOR", "GCLEARLOWALPHA", "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT",
				"GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GET_SKIA_QUALITY", "GET_TEXT_DRAWING_MODE",
				"GETANIMETIMER", "GETARGCOUNT", "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME",
				"GETCSVNOBYNAME", "GETCSVNOBYNICKNAME", "GETLINEY", "GETMETHF", "GETPLATFORM",
				"GETSOUNDORBGMINFO", "GETVARF", "GFILLPOLYGON", "G_POLYGON_DRAW",
				"G_POLYGON_FILL", "G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR", "GROTATE", "ISPLAYINGBGM",
				"ISPLAYINGSOUND", "MAP_FINDKEY", "MAP_FROMSTRING", "MAP_MERGE", "MAP_REMOVEIF",
				"MAP_TOSTRING", "MAP_VALUES", "MATCHALL", "MATCHALLEX", "MOUSEBUTTON", "ROUND",
				"SEQUENCEINPUT", "SIN", "SOUNDCONTROL", "SPRITECREATEFROMFILE", "SQL_CONNECT",
				"SQL_CONNECTION_OPEN", "SQL_DISCONNECT", "SQL_ESCAPE", "SQL_EXECUTE_NONQUERY",
				"SQL_EXECUTE_READER", "SQL_EXECUTE_SCALAR_FLOAT", "SQL_EXECUTE_SCALAR_LONG",
				"SQL_EXECUTE_SCALAR_STRING", "SQL_EXPORT_DT_XML", "SQL_EXPORT_MAP_XML", "SQL_IMPORT_DT_XML",
				"SQL_IMPORT_MAP_XML", "SQL_IMPORT_XML_CUSTOM", "SQL_P_EXECUTE_NONQUERY", "SQL_P_EXECUTE_READER",
				"SQL_P_EXECUTE_SCALAR_FLOAT", "SQL_P_EXECUTE_SCALAR_LONG", "SQL_P_EXECUTE_SCALAR_STRING",
				"SQL_READER_CLOSE", "SQL_READER_GET_FLOAT", "SQL_READER_GET_LONG", "SQL_READER_GET_STRING",
				"SQL_READER_ISNULL", "SQL_READER_READ", "STRFORMCHECK", "TAN", "TOFLOAT", "TOSTRF",
				"UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG", "UNCHECKED_SUB", "陥落状態", "陷落状态",
			});

		// The checked-in Snake expression registry is not a strict v24 union.
		// These functions are either v24-only or Godot-port-only aliases and
		// therefore must remain unavailable in a Snake session.
		// NOTE: 陥落状態/陷落状态 must NOT be hidden here — eraTW 系口上包在多个角色
		// ERB 中直接调用它，而定义只存在于个别口上包（如さとり包）里；SnakeFallenStateMethod
		// 提供共通 TALENT/CFLAG 兜底（用户 @陥落状態 定义优先执行），隐藏后这些口上会在
		// 运行时抛"陥落状態は解釈できない識別子です"（eraThe World 平板日志 20260805 实证）。
		private static readonly IReadOnlyCollection<string> SnakeExcludedFunctionNames =
			Array.AsReadOnly(new[]
			{
					"BITMAP_CACHE_ENABLE", "CBGSETCIMG", "DT_CELL_SETF", "GCLEARLOWALPHA",
				"GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT", "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GROTATE",
				"GETARGCOUNT", "GFILLPOLYGON", "MOUSEBUTTON", "SETANIMETIMER",
			});

		public string ModuleId => "game.snake";

		public void Declare(LegacyCompatibilityProfileBuilder builder)
		{
			builder.DeclareInstructionNames(InstructionNames);
			builder.DeclareFunctionNames(FunctionNames);
		}

		public void Apply(LegacyCompatibilityProfileBuilder builder)
		{
			builder.ExposeInstructionNames(InstructionNames);
			builder.ExposeFunctionNames(FunctionNames);
			builder.HideFunctionNames(SnakeExcludedFunctionNames);
			builder.SetSnakePolicy(LegacySnakeCompatibilityPolicy.FromCapabilities(builder.Plan.CapabilityIds));
			// snake 覆盖 v24 基线：SETBGIMAGE 用 snake 变体；FOR 回退共享表原条目
			//（snake 的 FOR 文法即共享表注册的 REPEAT 内核，EXTENDED 标志保持不变）。
			builder.SubstituteInstruction("FOR", LegacyInstructionVariant.SharedTable);
			builder.SubstituteInstruction("SETBGIMAGE", LegacyInstructionVariant.SetBgImageSnake);
			// 蛇系函数参数契约（重载差异名集）随模块激活。
			builder.ActivateDialectFunctionContracts(DivergentFunctionContractNames);
		}
	}

	internal sealed class LegacyV18CompatibilityModule : ILegacyCompatibilityModule
	{
		// v18 参考（emuera_v18_exported）注册表相对 v24 参考（emuera.em-master）的差集取证：
		// 指令 266 ⊂ 305、函数 160 ⊂ 270，v18 无独有名。以下为"v24 注册而 v18 未注册"的
		// 名单（指令 39 + 函数 110，2026-09-12 双源脚本取证）；选中 v18 模块的会话隐藏它们。
		// 证据提取：双方 FunctionCode 枚举 + FunctionIdentifier/Creator 注册引用逐一比对。
		private static readonly IReadOnlyCollection<string> V24OnlyInstructionNames =
			Array.AsReadOnly(new[]
			{
				"BINPUT", "BINPUTS", "BREAKBUTTON", "CALLSHARP", "CLEARBGIMAGE", "DT_COLUMN_OPTIONS",
				"FORCE_BEGIN", "FORCE_QUIT", "FORCE_QUIT_AND_RESTART", "HTML_PRINT_ISLAND",
				"HTML_PRINT_ISLAND_CLEAR", "INPUTANY", "ONEBINPUT", "ONEBINPUTS", "PLAYBGM",
				"PLAYSOUND", "PRINTFORMN", "PRINTFORMSN", "PRINTN", "PRINTSN", "PRINTVN",
				"QUIT_AND_RESTART", "REMOVEBGIMAGE", "SETBGIMAGE", "SETBGMVOLUME", "SETSOUNDVOLUME",
				"SKIPLOG", "STOPBGM", "STOPSOUND", "TOOLTIP_CUSTOM", "TOOLTIP_FORMAT", "TOOLTIP_IMG",
				"TOOLTIP_SETFONT", "TOOLTIP_SETFONTSIZE", "TRYCALLF", "TRYCALLFORMF", "UPDATECHECK",
				"VARI", "VARS",
			});

		private static readonly IReadOnlyCollection<string> V24OnlyFunctionNames =
			Array.AsReadOnly(new[]
			{
				"ARRAYMSORTEX", "BITMAP_CACHE_ENABLE", "CHKGLOBALDATA", "CHKVARDATA", "CLEARMEMORY",
				"DT_CELL_GET", "DT_CELL_GETS", "DT_CELL_ISNULL", "DT_CELL_SET", "DT_CLEAR",
				"DT_COLUMN_ADD", "DT_COLUMN_EXIST", "DT_COLUMN_LENGTH", "DT_COLUMN_NAMES",
				"DT_COLUMN_REMOVE", "DT_CREATE", "DT_EXIST", "DT_FROMXML", "DT_NOCASE", "DT_RELEASE",
				"DT_ROW_ADD", "DT_ROW_LENGTH", "DT_ROW_REMOVE", "DT_ROW_SET", "DT_SELECT", "DT_TOXML",
				"ENUMFILES", "ENUMFUNCBEGINSWITH", "ENUMFUNCENDSWITH", "ENUMFUNCWITH",
				"ENUMMACROBEGINSWITH", "ENUMMACROENDSWITH", "ENUMMACROWITH", "ENUMVARBEGINSWITH",
				"ENUMVARENDSWITH", "ENUMVARWITH", "ERDNAME", "EXISTFILE", "EXISTFUNCTION", "EXISTMETH",
				"EXISTSOUND", "EXISTVAR", "FIND_VARDATA", "FLOWINPUT", "FLOWINPUTS", "GDASHSTYLE",
				"GDRAWGWITHROTATE", "GDRAWLINE", "GDRAWTEXT", "GETDISPLAYLINE", "GETDOINGFUNCTION",
				"GETMEMORYUSAGE", "GETMETH", "GETMETHS", "GETTEXTBOX", "GETVAR", "GETVARS",
				"GGETBRUSH", "GGETFONT", "GGETFONTSIZE", "GGETFONTSTYLE", "GGETPEN", "GGETPENWIDTH",
				"GGETTEXTSIZE", "GROTATE", "HOTKEY_STATE", "HOTKEY_STATE_INIT", "HTML_STRINGLEN",
				"HTML_STRINGLINES", "HTML_SUBSTRING", "ISDEFINED", "MAP_CLEAR", "MAP_CREATE",
				"MAP_EXIST", "MAP_FROMXML", "MAP_GET", "MAP_GETKEYS", "MAP_HAS", "MAP_RELEASE",
				"MAP_REMOVE", "MAP_SET", "MAP_SIZE", "MAP_TOXML", "MOUSEB", "MOVETEXTBOX",
				"OUTPUTLOG", "REGEXPMATCH", "RESUMETEXTBOX", "SETTEXTBOX", "SETVAR",
				"SPRITEDISPOSEALL", "VARSETEX", "XML_ADDATTRIBUTE", "XML_ADDATTRIBUTE_BYNAME",
				"XML_ADDNODE", "XML_ADDNODE_BYNAME", "XML_DOCUMENT", "XML_EXIST", "XML_GET",
				"XML_GET_BYNAME", "XML_RELEASE", "XML_REMOVEATTRIBUTE", "XML_REMOVEATTRIBUTE_BYNAME",
				"XML_REMOVENODE", "XML_REMOVENODE_BYNAME", "XML_REPLACE", "XML_REPLACE_BYNAME",
				"XML_SET", "XML_SET_BYNAME", "XML_TOSTR",
			});

		public string ModuleId => "gemuera.v18";
		public void Declare(LegacyCompatibilityProfileBuilder builder) { }
		public void Apply(LegacyCompatibilityProfileBuilder builder)
		{
			builder.HideInstructionNames(V24OnlyInstructionNames);
			builder.HideFunctionNames(V24OnlyFunctionNames);
		}
	}

	internal sealed class LegacyEraFlCompatibilityModule : ILegacyCompatibilityModule
	{
		// eraFL 实测依赖的 snake 系指令（handler 由共享仓 snake 侧注册，erafl 只声明
		// 可见性需求）。证据：erafl-master グラフィック生成.ERB:356 "SETANIMETIMER 1000 / フレームレート"。
		// 三接口职责：v24pure = v24 参考；snake = v24 + snake 方言；erafl = v24 + eraFL 实测能力清单 + eraFL 策略。
		private static readonly IReadOnlyCollection<string> InstructionNames =
			Array.AsReadOnly(new[] { "SETANIMETIMER" });

		public string ModuleId => EraFlCompatibilityModule.ModuleId;
		public void Declare(LegacyCompatibilityProfileBuilder builder)
		{
			// Declare 对所有会话执行：erafl 声明加入隐藏集（v24pure/snake 下该指令本就隐藏，
			// 零影响）；仅 erafl 会话的 Apply 才解除隐藏。
			builder.DeclareInstructionNames(InstructionNames);
		}
		public void Apply(LegacyCompatibilityProfileBuilder builder)
		{
			builder.ExposeInstructionNames(InstructionNames);
			builder.SetEraFlPolicy(LegacyEraFlCompatibilityPolicy.FromCapabilities(builder.Plan.CapabilityIds));
			// erafl 显式绑定共享表的 SETANIMETIMER handler：绑定归属在模块声明中可见，
			// 未来 snake 侧 handler 分叉时 erafl 在此固定自己的变体，不再隐式跟随共享表。
			builder.SubstituteInstruction("SETANIMETIMER", LegacyInstructionVariant.SharedTable);
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
	}

	internal sealed class LegacySnakeCompatibilityPolicy : ISnakeCompatibilityPolicy
	{
		// quirk capability 账本驱动：每个布尔属性 = snake profile 声明的对应 id 是否在场
		//（映射表见 SnakeCompatibilityCapabilities.PolicyPropertyByCapabilityId，
		// 映射穷尽性由 SurfaceSmoke 反射断言把关）。
		private readonly IReadOnlyCollection<string> capabilities;

		private LegacySnakeCompatibilityPolicy(IReadOnlyCollection<string> capabilities)
		{
			this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
		}

		public static LegacySnakeCompatibilityPolicy FromCapabilities(IReadOnlyCollection<string> capabilityIds)
		{
			return new LegacySnakeCompatibilityPolicy(capabilityIds);
		}

		public bool IsEnabled => true;
		public bool UsesParserDiagnostics => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ParserDiagnostics);
		public bool AllowsUserDefinedVariableResolution => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.UserDefinedVariableResolution);
		public bool AllowsPrivateArguments => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.PrivateArguments);
		public bool AllowsExtraCallArguments => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ExtraCallArguments);
		public bool AllowsScopedVariablePreRegistration => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ScopedVariablePreRegistration);
		public bool ContinuesAfterStartupFault => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ContinueAfterStartupFault);
		public bool UsesFastDisplayRefresh => capabilities.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.FastDisplayRefresh);
		public bool UsesLazyResourceIndex => true;
	}

	internal sealed class DisabledEraFlCompatibilityPolicy : IEraFlCompatibilityPolicy
	{
		public static readonly DisabledEraFlCompatibilityPolicy Instance = new DisabledEraFlCompatibilityPolicy();
		public bool IsEnabled => false;
		public bool UsesExtendedDisplayHistory => false;
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
		// 布尔型/开关型 quirk 由 capability 账本派生（erafl profile 声明），
		// 算法型行为（GMap、指针归一化）仍委托 Core 模块的确定性实现。
		private readonly IReadOnlyCollection<string> capabilities;

		private LegacyEraFlCompatibilityPolicy(IReadOnlyCollection<string> capabilities)
		{
			this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
		}

		public static LegacyEraFlCompatibilityPolicy FromCapabilities(IReadOnlyCollection<string> capabilityIds)
		{
			return new LegacyEraFlCompatibilityPolicy(capabilityIds);
		}

		public bool IsEnabled => true;
		public bool UsesExtendedDisplayHistory => capabilities.Contains(EraFlCompatibilityModule.DisplayExtendedHistoryBehavior);
		public string TaskStartRoomLookupFunction => EraFlCompatibilityModule.TaskStartRoomLookupFunction;
		public string GMapQuestType => EraFlCompatibilityModule.GMapQuestType;
		public bool IsOmittedDefaultArgument(char currentToken) =>
			capabilities.Contains(EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior)
			&& EraFlCompatibilityModule.IsOmittedDefaultArgument(currentToken);
		public bool IsPointerInputMetadataOption(string optionText) => EraFlCompatibilityModule.IsPointerInputMetadataOption(optionText);
		public int NormalizePointerButtonResult(int mouseButton) => EraFlCompatibilityModule.NormalizePointerButtonResult(mouseButton);
		public string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger) =>
			EraFlCompatibilityModule.NormalizePointerIntegerSubmission(input, mouseButton, waitingForInteger);
		public bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString) =>
			capabilities.Contains(EraFlCompatibilityModule.PointerBlankStringBehavior)
			&& EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(mouseButton, waitingForString);
		public bool TryRecoverQuestStartRoomIndex(string functionName, long returnedRoomIndex, string requestedRoomTag, long mapId, string questType, string[,] mapData, out long recoveredRoomIndex) =>
			EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
				functionName,
				returnedRoomIndex,
				requestedRoomTag,
				mapId,
				questType,
				mapData,
				out recoveredRoomIndex);
		public bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes)
		{
			if (nodes == null)
				return false;
			var coreNodes = new List<EraFlCompatibilityModule.GMapNodeData>(nodes.Count);
			foreach (EraFlGMapNode node in nodes)
				coreNodes.Add(new EraFlCompatibilityModule.GMapNodeData(node.NodeId, node.NodeName, node.PathList));
			return EraFlCompatibilityModule.TryPopulateGMapRoomData(mapId, mapData, coreNodes);
		}
		public bool TryParseGMapDataTableFromXml(string schemaXml, string dataXml, out System.Data.DataTable table, out IReadOnlyList<EraFlGMapNode> nodes)
		{
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
