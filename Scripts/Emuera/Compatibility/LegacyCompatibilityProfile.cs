using System;
using System.Collections.Generic;
using GEmuera.Core.Compatibility;

namespace MinorShift.Emuera.Compatibility
{
	/// <summary>
	/// The narrow legacy-facing policy for Snake-only parser and VM behavior.
	/// The policy is immutable for the lifetime of one legacy session.
	/// </summary>
	internal interface ISnakeCompatibilityPolicy
	{
		bool IsEnabled { get; }
		bool UsesParserDiagnostics { get; }
		bool AllowsUserDefinedVariableResolution { get; }
		bool AllowsPrivateArguments { get; }
		bool AllowsExtraCallArguments { get; }
		bool AllowsScopedVariablePreRegistration { get; }
		bool ContinuesAfterStartupFault { get; }
		bool UsesFastDisplayRefresh { get; }
		bool UsesLazyResourceIndex { get; }
	}

	/// <summary>
	/// The narrow legacy-facing policy for eraFL input and display differences.
	/// Game-specific parsing and recovery algorithms remain in the pure Core
	/// eraFL module; this interface only decides whether they are reachable.
	/// </summary>
	internal interface IEraFlCompatibilityPolicy
	{
		bool IsEnabled { get; }
		bool UsesExtendedDisplayHistory { get; }
		string TaskStartRoomLookupFunction { get; }
		string GMapQuestType { get; }
		bool IsOmittedDefaultArgument(char currentToken);
		bool IsPointerInputMetadataOption(string optionText);
		int NormalizePointerButtonResult(int mouseButton);
		string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger);
		bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString);
		bool TryRecoverQuestStartRoomIndex(
			string functionName,
			long returnedRoomIndex,
			string requestedRoomTag,
			long mapId,
			string questType,
			string[,] mapData,
			out long recoveredRoomIndex);
		bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes);
		bool TryParseGMapDataTableFromXml(
			string schemaXml,
			string dataXml,
			out System.Data.DataTable table,
			out IReadOnlyList<EraFlGMapNode> nodes);
	}

	/// <summary>
	/// eraMegaten 专用窄策略接口：三个门控行为（P1 函数标签查询大小写归一化
	/// 跟随 ICVariable、P2 #DIM REF OUT 的 OUT 名字位、P3 私有 #DIM 遮蔽
	/// SystemVariable 降为警告级 1）。仅决定既有解析路径是否放行，不引入新算法。
	/// </summary>
	internal interface IMegatenCompatibilityPolicy
	{
		bool IsEnabled { get; }
		bool UsesVariableCaseForFunctionLabelLookup { get; }
		bool AllowsOutAsVariableNameAfterRefKeyword { get; }
		bool AllowsPrivateSystemVariableShadowing { get; }
	}

	/// <summary>
	/// Legacy bridge DTO for eraFL GMAP node data. The VM sees this typed
	/// contract instead of the Core module's implementation detail.
	/// </summary>
	internal sealed class EraFlGMapNode
	{
		public EraFlGMapNode(long nodeId, string nodeName, string pathList)
		{
			NodeId = nodeId;
			NodeName = nodeName ?? string.Empty;
			PathList = pathList ?? string.Empty;
		}

		public long NodeId { get; }
		public string NodeName { get; }
		public string PathList { get; }
	}

	/// <summary>
	/// Immutable module composition consumed by the process-wide legacy runtime.
	/// The legacy engine cannot run two VMs concurrently yet, but every parser,
	/// view, and resource decision reads this session-bound object instead of a
	/// mutable global profile enum or a game-name branch.
	/// </summary>
	internal sealed class LegacyCompatibilityProfile
	{
		private readonly ISet<string> hiddenInstructionNames;
		private readonly ISet<string> hiddenFunctionNames;
		private readonly ISet<string> scopedInstructionNames;
		private readonly ISet<string> methodProjectedFunctionNames;
		private readonly IReadOnlyDictionary<string, string> hiddenNameOwners;
		private readonly bool scopedVariableInstructionsEnabled;

		internal LegacyCompatibilityProfile(
			string profileId,
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled,
			ISnakeCompatibilityPolicy snake,
			IEraFlCompatibilityPolicy eraFl,
			// megaten 会话策略（默认 Disabled，保证既有三 profile 零行为变化）
			IMegatenCompatibilityPolicy megaten,
			IEnumerable<string> hiddenInstructionNames,
			IEnumerable<string> hiddenFunctionNames,
			IEnumerable<string> scopedInstructionNames,
			IEnumerable<string> methodProjectedFunctionNames,
			IReadOnlyDictionary<string, string> hiddenNameOwners = null)
		{
			ProfileId = profileId;
			Plan = plan;
			this.scopedVariableInstructionsEnabled = scopedVariableInstructionsEnabled;
			Snake = snake;
			EraFl = eraFl;
			Megaten = megaten;
			this.hiddenInstructionNames = new HashSet<string>(hiddenInstructionNames, StringComparer.Ordinal);
			this.hiddenFunctionNames = new HashSet<string>(hiddenFunctionNames, StringComparer.Ordinal);
			this.scopedInstructionNames = new HashSet<string>(scopedInstructionNames, StringComparer.Ordinal);
			this.methodProjectedFunctionNames = new HashSet<string>(methodProjectedFunctionNames, StringComparer.Ordinal);
			this.hiddenNameOwners = hiddenNameOwners
				?? new Dictionary<string, string>(StringComparer.Ordinal);
		}

		public string ProfileId { get; }
		public CompatibilityPlan Plan { get; }
		public ISnakeCompatibilityPolicy Snake { get; }
		public IEraFlCompatibilityPolicy EraFl { get; }
		// megaten 门控策略：v24pure/snake/erafl 会话下恒为 DisabledMegatenCompatibilityPolicy。
		public IMegatenCompatibilityPolicy Megaten { get; }
		/// <summary>
		/// Frozen session input for the optional Snake <c>VARI</c>/<c>VARS</c>
		/// instruction surface. This is intentionally not read from the mutable
		/// legacy Config singleton while the parser registry is being built.
		/// </summary>
		public bool ScopedVariableInstructionsEnabled => scopedVariableInstructionsEnabled;
		/// <summary>
		/// Identity of the registry surface projected from this profile. The Core
		/// plan hash alone is insufficient because the scoped-variable setting
		/// changes parser-visible names without changing its module closure.
		/// </summary>
		public string RegistrySurfaceHash => Plan.CanonicalHash
			+ ":scoped-variable-instructions="
			+ (scopedVariableInstructionsEnabled ? "enabled" : "disabled");
		public bool UsesExtendedDisplayHistory => Snake.IsEnabled || EraFl.UsesExtendedDisplayHistory;
		// 懒资源索引是性能实现细节而非方言语义：所有 profile（含 v24 基线）统一用它，
		// 启动期零图片 I/O（只解析 CSV 元数据），像素解码与文件头读取延迟到首次使用。
		// 脚本语义由 csvSpriteNames 全量注册 + SpriteExists 懒实体化校验保证不变。
		public bool UsesLazyResourceIndex => true;

		public bool IsInstructionVisible(string instructionName)
		{
			if (string.IsNullOrWhiteSpace(instructionName))
				return false;

			string name = instructionName.Trim().ToUpperInvariant();
			if (!hiddenInstructionNames.Contains(name))
				return !scopedInstructionNames.Contains(name) || scopedVariableInstructionsEnabled;
			return false;
		}

		public bool IsFunctionVisible(string functionName)
		{
			if (string.IsNullOrWhiteSpace(functionName))
				return false;
			return !hiddenFunctionNames.Contains(functionName.Trim());
		}

		public bool ShouldProjectExpressionFunctionAsInstruction(string functionName)
		{
			if (string.IsNullOrWhiteSpace(functionName))
				return false;
			return methodProjectedFunctionNames.Contains(functionName.Trim());
		}

		/// <summary>
		/// 诊断提示：名字在当前会话中不可见（被隐藏），且声明它的方言模块未被本会话选中
		/// （如 v24pure 会话中查询 snake 专属名字）时返回其归属模块 id。用于在报错文案里
		/// 提示"该标识符属于 snake 系扩展，建议改用 snake 接口"。snake 会话中查询
		/// SETANIMETIMER 函数形态（snake 自身隐藏）返回 false——snake 已选中，无需提示。
		/// </summary>
		public bool TryGetUnselectedModuleHint(string name, out string moduleId)
		{
			moduleId = null;
			if (string.IsNullOrWhiteSpace(name))
				return false;

			string normalized = name.Trim();
			// 指令名按 IsInstructionVisible 语义转大写，函数名保持原样（与 IsFunctionVisible 一致）。
			if (!hiddenNameOwners.ContainsKey(normalized))
				normalized = normalized.ToUpperInvariant();
			if (!hiddenNameOwners.TryGetValue(normalized, out string owner))
				return false;

			foreach (DialectModuleSnapshot selected in Plan.Dialect.Modules)
			{
				if (string.Equals(selected.ModuleId, owner, StringComparison.Ordinal))
					return false; // 归属模块已被选中，隐藏是模块自身语义，不提示
			}
			moduleId = owner;
			return true;
		}

		public static LegacyCompatibilityProfile Create(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));

			return LegacyCompatibilityModuleCatalog.Compose(plan, scopedVariableInstructionsEnabled);
		}

		public static LegacyCompatibilityProfile CreateForProfile(
			string profileId,
			bool scopedVariableInstructionsEnabled)
		{
			CompatibilityPlan plan = BuiltInDialectCatalog.CreateLegacySessionPlan(profileId);
			return Create(plan, scopedVariableInstructionsEnabled);
		}

	}
}
