#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emuera.Compatibility.Packs
{
	/// <summary>包身份与游戏绑定（可选）。声明后加载时与 GameBase.csv 比对，
	/// 不匹配则拒绝加载并回退纯 v24（agent-profiles schema 的降级不变量经验，
	/// 见 docs/designs/compat-pack-interface.md §3.3）。</summary>
	public sealed class CompatPackGameIdentity
	{
		internal CompatPackGameIdentity(string gameCode, string version, string versionAccept)
		{
			GameCode = gameCode;
			Version = version;
			VersionAccept = versionAccept;
		}

		/// <summary>GameBase.csv コード 行（如 20250628）。</summary>
		public string GameCode { get; }
		/// <summary>GameBase.csv バージョン 行（如 305）。</summary>
		public string Version { get; }
		/// <summary>版本容忍：exact（默认，精确匹配）或 minor（次级差异可接受）。</summary>
		public string VersionAccept { get; }
	}

		/// <summary>
	/// 清单声明的静态表面（纯数据，无需程序集入口代码）：指令/函数名单增删。
	/// 指令名在解析期 Trim+Upper，函数名保持原样；语义对账在加载器/规则层。
	/// </summary>
	public sealed class CompatPackSurface
	{
		public static CompatPackSurface Empty { get; } = new CompatPackSurface(
			Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

		internal CompatPackSurface(
			IReadOnlyList<string> addInstructions,
			IReadOnlyList<string> hideInstructions,
			IReadOnlyList<string> addFunctions,
			IReadOnlyList<string> hideFunctions)
		{
			AddInstructions = addInstructions;
			HideInstructions = hideInstructions;
			AddFunctions = addFunctions;
			HideFunctions = hideFunctions;
		}

		public IReadOnlyList<string> AddInstructions { get; }
		public IReadOnlyList<string> HideInstructions { get; }
		public IReadOnlyList<string> AddFunctions { get; }
		public IReadOnlyList<string> HideFunctions { get; }
	}

/// <summary>
	/// 兼容包内嵌清单（compatpack.manifest.json）的不可变模型与解析器。
	/// 结构校验全部 fail-closed：任一字段不合法即整体拒绝，错误全量收集（agent-profiles
	/// 的 outcome.Errors 模式），绝不半解析静默继续。语义校验（capability id 词汇表命中、
	/// 表面名单与引擎注册表对账）在加载器侧执行，不在本类。
	/// </summary>
	public sealed class CompatPackManifest
	{
		/// <summary>内嵌资源名（区分大小写，Ordinal 匹配）。</summary>
		public const string ManifestResourceName = "compatpack.manifest.json";

		// 与仓库模块 id / capability id 词汇表同风格的最小字符集约束；
		// 语义层面（是否真实存在）由加载器对照词汇表判断。
		private static readonly Regex IdPattern = new(
			"^[a-z0-9][a-z0-9.\\-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
		private static readonly Regex SemverPattern = new(
			"^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
		private static readonly Regex LowerHexPattern = new(
			"^[0-9a-f]{8,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

		internal CompatPackManifest(
			string packId,
			string packVersion,
			CompatVersion contractAbi,
			CompatVersion engineApi,
			bool usedLegacyTargetEngineApi,
			string baseProfileId,
			CompatPackSurface surface,
			string? baseSurfaceHash,
			IReadOnlyList<string> capabilities,
			string? saveProfileId,
			IReadOnlyDictionary<string, string> variantSelections,
			CompatPackGameIdentity? gameIdentity)
		{
			PackId = packId;
			PackVersion = packVersion;
			ContractAbi = contractAbi;
			EngineApi = engineApi;
			UsedLegacyTargetEngineApi = usedLegacyTargetEngineApi;
			BaseProfileId = baseProfileId;
			Surface = surface;
			BaseSurfaceHash = baseSurfaceHash;
			Capabilities = capabilities;
			SaveProfileId = saveProfileId;
			VariantSelections = variantSelections;
			GameIdentity = gameIdentity;
		}

		public string PackId { get; }
		public string PackVersion { get; }
		/// <summary>包编译时依赖的公开契约面版本（Emuera.CompatPack ABI 线）：major 必须与引擎相等，
		/// 包 minor 不得高于引擎 minor（包可能用到引擎缺失的契约增量）。</summary>
		public CompatVersion ContractAbi { get; }
		/// <summary>包依赖的引擎行为/能力契约版本（不再复用 DialectModuleDefinition.ModuleApiVersion）；
		/// 比较规则与 <see cref="ContractAbi"/> 同：major 相等且包 minor &lt;= 引擎 minor。</summary>
		public CompatVersion EngineApi { get; }
		/// <summary>true = 清单使用 legacy 字段 <c>targetEngineApi: 1</c> 并已映射为双轴 1.0/1.0。
		/// 迁移期标记：宿主可据此记 [LOAD] 日志，发布门禁可据此拒绝未迁移的官方包。</summary>
		public bool UsedLegacyTargetEngineApi { get; }
		/// <summary>包叠加的基线 profile。v1 固定为 v24pure（规则层显式拒载其他值）；宿主还会与当前会话基线核对。</summary>
		public string BaseProfileId { get; }
		/// <summary>manifest 静态表面（空 = 仅 capability/variant 声明）。</summary>
		public CompatPackSurface Surface { get; }
		/// <summary>可选：声明基于哪个 v24 表面快照（生成清单哈希，规范化算法见
		/// <c>LegacySurfaceHash.ComputeV24SurfaceHash</c>）。v1 起精确对账：声明非空且与当前
		/// 引擎基线不一致即拒载（加载器/规则层执行）；不声明则不触发该校验。</summary>
		public string? BaseSurfaceHash { get; }
		public IReadOnlyList<string> Capabilities { get; }
		public string? SaveProfileId { get; }
		/// <summary>指令名 → 变体名。v1 仅接受 "builtin:*"（引擎内置变体；未知内置名拒载）；
		/// v1 没有自带变体通道——IInstructionVariantContribution 为 v2 接线预留（宿主尚不消费，
		/// 携带即拒载），不要按"贡献绑定隐式生效"的旧表述使用。</summary>
		public IReadOnlyDictionary<string, string> VariantSelections { get; }
		public CompatPackGameIdentity? GameIdentity { get; }

		/// <summary>解析并结构校验清单 JSON。任何字段不合法返回 false 且 errors 非空。</summary>
		public static bool TryParse(string? json, out CompatPackManifest? manifest, out IReadOnlyList<string> errors)
		{
			var collected = new List<string>();
			manifest = null;
			if (string.IsNullOrWhiteSpace(json))
			{
				collected.Add("清单内容为空。");
				errors = collected;
				return false;
			}

			JsonDocument document;
			try
			{
				// 容忍 UTF-8 BOM（编辑器常见落盘形态），其余解析失败一律拒绝。
				document = JsonDocument.Parse(json.TrimStart('\uFEFF'), new JsonDocumentOptions
				{
					AllowTrailingCommas = false,
					CommentHandling = JsonCommentHandling.Disallow,
				});
			}
			catch (JsonException exception)
			{
				collected.Add("清单不是合法 JSON：" + exception.Message);
				errors = collected;
				return false;
			}

			using (document)
			{
				JsonElement root = document.RootElement;
				if (root.ValueKind != JsonValueKind.Object)
				{
					collected.Add("清单根必须是 JSON 对象。");
					errors = collected;
					return false;
				}

				string packId = RequireId(root, "packId", collected, required: true) ?? "";
				string packVersion = Require(root, "packVersion", collected, SemverPattern, "语义化版本 x.y.z");
				(CompatVersion ContractAbi, CompatVersion EngineApi, bool UsedLegacyTargetEngineApi) axes =
					RequireVersionAxes(root, collected);
				string baseProfileId = OptionalIdOrDefault(root, "baseProfileId", "v24pure", collected);
				CompatPackSurface surface = RequireSurface(root, collected);
				string? baseSurfaceHash = Optional(root, "baseSurfaceHash", collected, LowerHexPattern, "小写十六进制哈希（8-64 位）");
				IReadOnlyList<string> capabilities = RequireStringArray(root, "capabilities", collected, required: false);
				string? saveProfileId = RequireId(root, "saveProfileId", collected, required: false);
				IReadOnlyDictionary<string, string> variantSelections = RequireVariantSelections(root, collected);
				CompatPackGameIdentity? gameIdentity = RequireGameIdentity(root, collected);

				// 未知字段整体拒绝（与 schema additionalProperties:false 一致），防止拼写错误静默失效。
				foreach (JsonProperty property in root.EnumerateObject())
				{
					switch (property.Name)
					{
						case "packId":
						case "packVersion":
						case "contractAbi":
						case "engineApi":
						case "targetEngineApi":
						case "baseProfileId":
						case "surface":
						case "baseSurfaceHash":
						case "capabilities":
						case "saveProfileId":
						case "variantSelections":
						case "gameIdentity":
							break;
						default:
							collected.Add("未知字段：'" + property.Name + "'。");
							break;
					}
				}

				errors = collected;
				if (collected.Count > 0)
					return false;

				manifest = new CompatPackManifest(
					packId,
					packVersion,
					axes.ContractAbi,
					axes.EngineApi,
					axes.UsedLegacyTargetEngineApi,
					baseProfileId,
					surface,
					baseSurfaceHash,
					capabilities,
					saveProfileId,
					variantSelections,
					gameIdentity);
				return true;
			}
		}

		/// <summary>
		/// 从程序集内嵌资源发现并解析清单。资源名 Ordinal 精确匹配
		/// <see cref="ManifestResourceName"/>；缺失/读取失败/解析失败均 fail-closed 返回 false。
		/// </summary>
		public static bool TryLoadFromAssembly(Assembly assembly, out CompatPackManifest? manifest, out IReadOnlyList<string> errors)
		{
			var collected = new List<string>();
			manifest = null;
			if (assembly is null)
			{
				collected.Add("程序集为空。");
				errors = collected;
				return false;
			}

			bool found = false;
			string json = string.Empty;
			try
			{
				foreach (string name in assembly.GetManifestResourceNames())
				{
					if (!string.Equals(name, ManifestResourceName, StringComparison.Ordinal))
						continue;
					using Stream? stream = assembly.GetManifestResourceStream(name);
					if (stream is null)
					{
						collected.Add("内嵌清单资源无法打开：" + name + "。");
						continue;
					}
					using var reader = new StreamReader(stream, Encoding.UTF8);
					json = reader.ReadToEnd();
					found = true;
					break;
				}
				if (!found && collected.Count == 0)
				{
					collected.Add("程序集 '" + assembly.GetName().Name + "' 缺少内嵌清单资源 "
						+ ManifestResourceName + "；缺清单的程序集不是兼容包。");
				}
			}
			catch (Exception exception) when (exception is IOException or InvalidOperationException)
			{
				collected.Add("读取内嵌清单失败：" + exception.Message);
			}

			errors = collected;
			if (collected.Count > 0)
				return false;

			return TryParse(json, out manifest, out errors);
		}

		private static string Require(JsonElement root, string field, List<string> errors, Regex pattern, string form)
		{
			if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind != JsonValueKind.String)
			{
				errors.Add("字段 '" + field + "' 缺失或不是字符串。");
				return "";
			}
			string text = value.GetString() ?? "";
			if (!pattern.IsMatch(text))
			{
				errors.Add("字段 '" + field + "' 形式不合法（应为 " + form + "）：" + text);
				return "";
			}
			return text;
		}

		private static string? RequireId(JsonElement root, string field, List<string> errors, bool required)
		{
			if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
			{
				if (required)
					errors.Add("字段 '" + field + "' 缺失。");
				return null;
			}
			if (value.ValueKind != JsonValueKind.String)
			{
				errors.Add("字段 '" + field + "' 不是字符串。");
				return null;
			}
			string text = value.GetString() ?? "";
			if (!IdPattern.IsMatch(text))
			{
				errors.Add("字段 '" + field + "' 形式不合法（小写字母数字、点、连字符）：" + text);
				return null;
			}
			return text;
		}

		/// <summary>
		/// 双轴版本字段解析（迁移期兼容 legacy targetEngineApi）：
		/// - 新格式：contractAbi 与 engineApi 必须同时声明，格式 major.minor；
		/// - 旧格式：仅 targetEngineApi，只接受 1，映射为 contractAbi=engineApi=1.0；
		/// - 新旧混用、其他 legacy 值一律拒绝（fail-closed，错误全量收集）。
		/// 任一错误收集后返回 default 轴值；调用方在 collected.Count > 0 时不会构造清单。
		/// </summary>
		private static (CompatVersion ContractAbi, CompatVersion EngineApi, bool UsedLegacyTargetEngineApi)
			RequireVersionAxes(JsonElement root, List<string> errors)
		{
			bool hasContractAbi = root.TryGetProperty("contractAbi", out _);
			bool hasEngineApi = root.TryGetProperty("engineApi", out _);
			bool hasLegacy = root.TryGetProperty("targetEngineApi", out JsonElement legacyValue);

			if (hasContractAbi || hasEngineApi)
			{
				if (hasLegacy)
				{
					errors.Add("字段 'targetEngineApi' 不能与 'contractAbi'/'engineApi' 混用"
						+ "（legacy 迁移字段只允许单独出现；混用会掩盖包作者的真实意图）。");
				}
				CompatVersion contractAbi = hasContractAbi ? RequireVersion(root, "contractAbi", errors) : default;
				CompatVersion engineApi = hasEngineApi ? RequireVersion(root, "engineApi", errors) : default;
				// "必须同时声明"必须报成独立错误：只声明一轴时另一轴缺失，不能静默取默认值。
				if (!hasContractAbi)
					errors.Add("字段 'contractAbi' 缺失：新格式清单必须同时声明 'contractAbi' 与 'engineApi'（格式 major.minor）。");
				if (!hasEngineApi)
					errors.Add("字段 'engineApi' 缺失：新格式清单必须同时声明 'contractAbi' 与 'engineApi'（格式 major.minor）。");
				return (contractAbi, engineApi, false);
			}

			if (hasLegacy)
			{
				if (IsLegacyTargetEngineApiOne(legacyValue))
					return (new CompatVersion(1, 0), new CompatVersion(1, 0), true);

				errors.Add("legacy 字段 'targetEngineApi' 只接受值 1，当前为 "
					+ DescribeVersionValue(legacyValue)
					+ "；其他旧值不再生效，请迁移到 'contractAbi'/'engineApi'（格式 major.minor）。");
				return (default, default, false);
			}

			errors.Add("字段 'contractAbi' 缺失：清单必须声明 'contractAbi' 与 'engineApi'（格式 major.minor）；"
				+ "legacy 字段 'targetEngineApi' 仅接受值 1，用于尚未迁移的包。");
			return (default, default, false);
		}

		private static CompatVersion RequireVersion(JsonElement root, string field, List<string> errors)
		{
			if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind != JsonValueKind.String)
			{
				errors.Add("字段 '" + field + "' 缺失或不是字符串（格式 major.minor，如 \"1.0\"）。");
				return default;
			}
			string text = value.GetString() ?? "";
			if (!CompatVersion.TryParse(text, out CompatVersion version))
			{
				errors.Add("字段 '" + field + "' 形式不合法（应为 major.minor，如 \"1.0\"）：" + text);
				return default;
			}
			return version;
		}

		/// <summary>legacy targetEngineApi 值判定：只接受数字 1 或字符串 "1"（旧解析器的两种编码）。</summary>
		private static bool IsLegacyTargetEngineApiOne(JsonElement value)
		{
			if (value.ValueKind == JsonValueKind.Number)
				return value.TryGetInt32(out int number) && number == 1;
			if (value.ValueKind == JsonValueKind.String)
			{
				return int.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer,
					System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed == 1;
			}
			return false;
		}

		private static string DescribeVersionValue(JsonElement value)
		{
			switch (value.ValueKind)
			{
				case JsonValueKind.Null:
					return "null";
				case JsonValueKind.String:
					return "'" + (value.GetString() ?? "") + "'";
				default:
					return value.GetRawText();
			}
		}

		private static string? Optional(JsonElement root, string field, List<string> errors, Regex pattern, string form)
		{
			if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return null;
			if (value.ValueKind != JsonValueKind.String)
			{
				errors.Add("字段 '" + field + "' 不是字符串。");
				return null;
			}
			string text = value.GetString() ?? "";
			if (!pattern.IsMatch(text))
			{
				errors.Add("字段 '" + field + "' 形式不合法（应为 " + form + "）：" + text);
				return null;
			}
			return text;
		}

		private static IReadOnlyList<string> RequireStringArray(JsonElement root, string field, List<string> errors, bool required)
		{
			var result = new List<string>();
			if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
			{
				if (required)
					errors.Add("字段 '" + field + "' 缺失。");
				return new ReadOnlyCollection<string>(result);
			}
			if (value.ValueKind != JsonValueKind.Array)
			{
				errors.Add("字段 '" + field + "' 必须是字符串数组。");
				return new ReadOnlyCollection<string>(result);
			}
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.String)
				{
					errors.Add("字段 '" + field + "' 含非字符串元素。");
					continue;
				}
				string text = item.GetString() ?? "";
				if (!IdPattern.IsMatch(text))
				{
					errors.Add("字段 '" + field + "' 元素形式不合法（小写字母数字、点、连字符）：" + text);
					continue;
				}
				if (!seen.Add(text))
				{
					errors.Add("字段 '" + field + "' 元素重复：" + text);
					continue;
				}
				result.Add(text);
			}
			return new ReadOnlyCollection<string>(result);
		}

		private static IReadOnlyDictionary<string, string> RequireVariantSelections(JsonElement root, List<string> errors)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			if (!root.TryGetProperty("variantSelections", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return new ReadOnlyDictionary<string, string>(result);
			if (value.ValueKind != JsonValueKind.Object)
			{
				errors.Add("字段 'variantSelections' 必须是对象（指令名 → 变体名）。");
				return new ReadOnlyDictionary<string, string>(result);
			}
			foreach (JsonProperty property in value.EnumerateObject())
			{
				string instruction = property.Name.Trim().ToUpperInvariant();
				if (instruction.Length == 0)
				{
					errors.Add("字段 'variantSelections' 存在空指令名键。");
					continue;
				}
				if (property.Value.ValueKind != JsonValueKind.String)
				{
					errors.Add("字段 'variantSelections' 的 '" + property.Name + "' 值必须是字符串。");
					continue;
				}
				string variant = property.Value.GetString() ?? "";
				if (variant.Length == 0 || variant.IndexOf(' ') >= 0)
				{
					errors.Add("字段 'variantSelections' 的 '" + property.Name + "' 变体名不合法（非空且不含空格）：" + variant);
					continue;
				}
				// 同一指令（含 JSON 原生重复键、Trim+Upper 规范化后冲突）出现两条绑定即整体拒绝，
				// 不做 last-wins 静默覆盖（fail-closed）。
				if (!result.TryAdd(instruction, variant))
				{
					errors.Add("字段 'variantSelections' 存在重复指令名键（规范化后冲突）：" + property.Name);
				}
			}
			return new ReadOnlyDictionary<string, string>(result);
		}

		private static string OptionalIdOrDefault(JsonElement root, string field, string defaultValue, List<string> errors)
		{
			if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return defaultValue;
			if (value.ValueKind != JsonValueKind.String)
			{
				errors.Add("字段 '" + field + "' 不是字符串。");
				return defaultValue;
			}
			string text = (value.GetString() ?? "").Trim();
			if (!IdPattern.IsMatch(text))
			{
				errors.Add("字段 '" + field + "' 形式不合法（小写字母数字、点、连字符）：" + text);
				return defaultValue;
			}
			return text;
		}

		private static CompatPackSurface RequireSurface(JsonElement root, List<string> errors)
		{
			if (!root.TryGetProperty("surface", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return CompatPackSurface.Empty;
			if (value.ValueKind != JsonValueKind.Object)
			{
				errors.Add("字段 'surface' 必须是对象。");
				return CompatPackSurface.Empty;
			}

			IReadOnlyList<string> addInstructions = Array.Empty<string>();
			IReadOnlyList<string> hideInstructions = Array.Empty<string>();
			IReadOnlyList<string> addFunctions = Array.Empty<string>();
			IReadOnlyList<string> hideFunctions = Array.Empty<string>();
			foreach (JsonProperty property in value.EnumerateObject())
			{
				switch (property.Name)
				{
					case "addInstructions":
						addInstructions = RequireSurfaceNameArray(value, property.Name, errors, normalizeUpper: true);
						break;
					case "hideInstructions":
						hideInstructions = RequireSurfaceNameArray(value, property.Name, errors, normalizeUpper: true);
						break;
					case "addFunctions":
						addFunctions = RequireSurfaceNameArray(value, property.Name, errors, normalizeUpper: false);
						break;
					case "hideFunctions":
						hideFunctions = RequireSurfaceNameArray(value, property.Name, errors, normalizeUpper: false);
						break;
					default:
						errors.Add("字段 'surface' 含未知成员：'" + property.Name + "'。");
						break;
				}
			}
			return new CompatPackSurface(addInstructions, hideInstructions, addFunctions, hideFunctions);
		}

		private static IReadOnlyList<string> RequireSurfaceNameArray(
			JsonElement parent, string field, List<string> errors, bool normalizeUpper)
		{
			var result = new List<string>();
			if (!parent.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return new ReadOnlyCollection<string>(result);
			if (value.ValueKind != JsonValueKind.Array)
			{
				errors.Add("字段 '" + field + "' 必须是字符串数组。");
				return new ReadOnlyCollection<string>(result);
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.String)
				{
					errors.Add("字段 '" + field + "' 含非字符串元素。");
					continue;
				}
				string text = (item.GetString() ?? "").Trim();
				if (text.Length == 0)
				{
					errors.Add("字段 '" + field + "' 含空名字。");
					continue;
				}
				bool hasWhitespace = false;
				foreach (char c in text)
				{
					if (char.IsWhiteSpace(c))
					{
						hasWhitespace = true;
						break;
					}
				}
				if (hasWhitespace)
				{
					errors.Add("字段 '" + field + "' 的名字不允许包含空白：'" + text + "'。");
					continue;
				}
				string normalized = normalizeUpper ? text.ToUpperInvariant() : text;
				if (!seen.Add(normalized))
				{
					errors.Add("字段 '" + field + "' 名字重复：" + normalized);
					continue;
				}
				result.Add(normalized);
			}
			return new ReadOnlyCollection<string>(result);
		}

		private static CompatPackGameIdentity? RequireGameIdentity(JsonElement root, List<string> errors)
		{
			if (!root.TryGetProperty("gameIdentity", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return null;
			if (value.ValueKind != JsonValueKind.Object)
			{
				errors.Add("字段 'gameIdentity' 必须是对象。");
				return null;
			}

			int errorsBefore = errors.Count;
			string gameCode = "";
			string version = "";
			string versionAccept = "exact";
			foreach (JsonProperty property in value.EnumerateObject())
			{
				switch (property.Name)
				{
					case "gameCode":
						gameCode = RequireMemberString(property.Value, "gameIdentity.gameCode", errors);
						break;
					case "version":
						version = RequireMemberString(property.Value, "gameIdentity.version", errors);
						break;
					case "versionAccept":
						versionAccept = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : "";
						if (!string.Equals(versionAccept, "exact", StringComparison.Ordinal)
							&& !string.Equals(versionAccept, "minor", StringComparison.Ordinal))
						{
							errors.Add("字段 'gameIdentity.versionAccept' 只接受 exact 或 minor：" + versionAccept);
						}
						break;
					default:
						errors.Add("字段 'gameIdentity' 含未知成员：'" + property.Name + "'。");
						break;
				}
			}
			if (gameCode.Length == 0)
				errors.Add("字段 'gameIdentity.gameCode' 缺失或为空。");
			if (version.Length == 0)
				errors.Add("字段 'gameIdentity.version' 缺失或为空。");
			// 只看本字段的局部错误（全局列表可能已含其它字段的问题）。
			if (errors.Count > errorsBefore)
				return null;
			return new CompatPackGameIdentity(gameCode, version, versionAccept);
		}

		private static string RequireMemberString(JsonElement value, string field, List<string> errors)
		{
			if (value.ValueKind != JsonValueKind.String)
			{
				errors.Add("字段 '" + field + "' 不是字符串。");
				return "";
			}
			string text = (value.GetString() ?? "").Trim();
			if (text.Length == 0)
				errors.Add("字段 '" + field + "' 为空。");
			return text;
		}
	}
}
