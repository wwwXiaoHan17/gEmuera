using System;
using System.IO;
using System.Net.Http;
using GEmuera.Core.Agent.Context;
using GEmuera.Core.Agent.Llm;
using gEmuera.Diagnostics;
using MinorShift.Emuera.Runtime.Utils.PluginSystem;

namespace MinorShift.Emuera.Runtime.AgentBridge
{
	/// <summary>
	/// 三个 CALLSHARP LLM 方法（CALL_GEMINI / CALL_OLLAMA / CALL_GENERIC_LLM_API）的统一路由实现。
	/// 语义（解释器线程同步调用）：
	///   - 未配置 [agent.llm] 三元组 → RESULT=-1，游戏走原版逻辑（离线兜底不变量），仅诊断日志；
	///   - 调用成功且预算足够 → RESULT=1，RESULTS=回复文本，经 TurnBudget 记账；
	///   - 网络/超时/预算耗尽 → RESULT=-1，仅诊断日志，绝不抛异常穿透解释器。
	/// </summary>
	public static class AgentLlmMethods
	{
		/// <summary>Spike 默认回合预算；阶段 1 后续接入 Profile budget 字段替换。</summary>
		static readonly TurnBudget Budget = new TurnBudget(8000);

		/// <summary>共享 HttpClient（连接复用）；Timeout 略大于 30s 硬超时，实际由 CTS 控制。</summary>
		static readonly HttpClient SharedHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };

		static readonly object configGate = new object();
		static AgentLlmTriple? cachedTriple;
		static bool configResolved;
		static bool unconfiguredLogged;

		public static (long Result, string Results) Execute(string methodName, PluginMethodParameter[] args)
		{
			var triple = ResolveTriple();
			if (triple == null)
			{
				LogUnconfiguredOnce(methodName);
				return (-1, "");
			}

			string prompt = ReadStringArg(args, 0);
			if (string.IsNullOrWhiteSpace(prompt))
			{
				global::GenericUtils.Warn($"AGENT_LLM_EMPTY_PROMPT method={methodName}");
				return (-1, "");
			}

			var provider = new OpenAiCompatProvider(SharedHttp, triple.BaseUrl, triple.ApiKey, triple.Model);
			var request = new LlmRequest(
				new[] { new LlmChatMessage("user", prompt) },
				1024);

			LlmResult llmResult;
			try
			{
				using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
				llmResult = provider.CompleteAsync(request, cts.Token).GetAwaiter().GetResult();
			}
			catch (Exception error)
			{
				global::GenericUtils.Warn($"AGENT_LLM_DISPATCH_FAULT method={methodName} error={error.GetType().Name}: {error.Message}");
				return (-1, "");
			}

			if (!llmResult.Ok)
			{
				global::GenericUtils.Warn($"AGENT_LLM_CALL_FAILED method={methodName} model={triple.Model} reason={llmResult.Error}");
				return (-1, "");
			}

			if (!Budget.TryConsume(llmResult.TokensUsed > 0 ? llmResult.TokensUsed : 1))
			{
				global::GenericUtils.Warn($"AGENT_LLM_BUDGET_EXHAUSTED method={methodName} remaining={Budget.Remaining} requested={llmResult.TokensUsed}");
				return (-1, "");
			}

			return (1, llmResult.Content);
		}

		/// <summary>回合边界重置预算（由检查点流水线在主检查点调用）。</summary>
		public static void ResetTurnBudget()
		{
			Budget.Reset();
		}

		static readonly GEmuera.Core.Agent.Cache.KoujouPrefetchCache PrefetchCache = new();

		/// <summary>当前预取缓存条数（AI_PREFETCH_STATUS 用）。</summary>
		public static int PrefetchCount
		{
			get { return PrefetchCache.Count; }
		}

		/// <summary>按情境 key 读预取缓存（AI_PREFETCH_GET 用）。未命中返回 (-1, "")。</summary>
		public static (long Result, string Results) PrefetchGet(string contextKey)
		{
			return PrefetchCache.TryGet(contextKey, out string text)
				? (1, text)
				: (-1, "");
		}

		/// <summary>
		/// 主检查点动作：预算内做一次预取 LLM 往返并写入缓存。
		/// 调用方应在后台线程执行（Task.Run），主线程与解释器线程都不阻塞。
		/// 未配置/失败/预算耗尽一律返回 0（离线兜底不变量，仅日志）。
		/// Spike 占位：固定模板 prompt + 固定情境 key；阶段 2 接入 CSV 角色上下文与真实情境 key。
		/// </summary>
		public static int OnPrimaryCheckpoint()
		{
			ResetTurnBudget();
			PrefetchCache.Clear();

			var triple = ResolveTriple();
			if (triple == null)
				return 0; // 未配置已由 CALLSHARP 路径日志过一次，这里静默

			string prompt = "你是era游戏角色口上生成器。请用简短的一句话，生成一条符合恶魔女仆角色的日常台词，仅输出台词本身。";
			var provider = new OpenAiCompatProvider(SharedHttp, triple.BaseUrl, triple.ApiKey, triple.Model);
			// 推理型模型（如 deepseek-v4-flash）会先消耗 reasoning tokens，配额过小会导致 content 为空。
			var request = new LlmRequest(new[] { new LlmChatMessage("user", prompt) }, 768);

			LlmResult llmResult;
			try
			{
				using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
				llmResult = provider.CompleteAsync(request, cts.Token).GetAwaiter().GetResult();
			}
			catch (Exception error)
			{
				global::GenericUtils.Warn($"AGENT_PREFETCH_FAULT error={error.GetType().Name}: {error.Message}");
				return 0;
			}

			if (!llmResult.Ok)
			{
				global::GenericUtils.Warn($"AGENT_PREFETCH_FAILED reason={llmResult.Error}");
				return 0;
			}

			if (!Budget.TryConsume(llmResult.TokensUsed > 0 ? llmResult.TokensUsed : 1))
			{
				global::GenericUtils.Warn($"AGENT_PREFETCH_BUDGET_EXHAUSTED remaining={Budget.Remaining} requested={llmResult.TokensUsed}");
				return 0;
			}

			PrefetchCache.Put("TARGET|时段|好感档", llmResult.Content);
			global::GenericUtils.Info($"AGENT_PREFETCH_FILLED entries=1 tokens={llmResult.TokensUsed}");
			return 1;
		}

		static AgentLlmTriple? ResolveTriple()
		{
			lock (configGate)
			{
				if (!configResolved)
				{
					cachedTriple = LoadTripleFromConfig();
					configResolved = true;
				}
				return cachedTriple;
			}
		}

		static void LogUnconfiguredOnce(string methodName)
		{
			if (unconfiguredLogged)
				return;
			unconfiguredLogged = true;
			global::GenericUtils.Warn($"AGENT_LLM_UNCONFIGURED method={methodName} hint=add [agent.llm] base_url/api_key/model to user://config.toml OR the repo config.toml next to project.godot, then restart. Searched: user:// res:// AppContext.BaseDirectory CWD — first file found without the section does NOT block later candidates.");
		}

		/// <summary>
		/// 读取 config.toml 的 [agent.llm] 三元组。搜索顺序与 RuntimeDiagnosticsConfigLoader 一致：
		/// user:// → res:// → AppContext.BaseDirectory → CWD。
		/// 某个候选文件存在但缺少 [agent.llm] 段或解析失败时，继续尝试下一候选（多份 config 并存时
		/// user:// 旧文件不能挡住 res:// 新配置——回归修复）；全部候选都没有该段才判未配置。
		/// 结果进程内缓存，修改配置需重启（Spike 约束）。
		/// </summary>
		static AgentLlmTriple? LoadTripleFromConfig()
		{
			string[] candidatePaths =
			{
				"user://config.toml",
				"res://config.toml",
				Path.Combine(AppContext.BaseDirectory, "config.toml"),
				Path.Combine(Directory.GetCurrentDirectory(), "config.toml")
			};

			foreach (string path in candidatePaths)
			{
				string text = TryReadText(path);
				if (text == null)
					continue;

				var parsed = RuntimeTomlParser.Parse(text);
				if (parsed.HasErrors || !parsed.Sections.TryGetValue("agent.llm", out var section))
					continue;

				string baseUrl = section.TryGetValue("base_url", out string b) ? b : "";
				string apiKey = section.TryGetValue("api_key", out string k) ? k : "";
				string model = section.TryGetValue("model", out string m) ? m : "";

				if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
					continue;

				return new AgentLlmTriple(baseUrl, apiKey, model);
			}
			return null;
		}

		static string TryReadText(string path)
		{
			try
			{
				if (path.Contains("://", StringComparison.Ordinal))
				{
					if (Godot.FileAccess.FileExists(path))
					{
						using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
						if (file != null)
							return file.GetAsText();
					}
					return null;
				}
				if (File.Exists(path))
					return File.ReadAllText(path, System.Text.Encoding.UTF8);
			}
			catch { }
			return null;
		}

		static string ReadStringArg(PluginMethodParameter[] args, int index)
		{
			if (args == null || index < 0 || index >= args.Length || args[index] == null)
				return "";
			if (args[index].isString)
				return args[index].strValue ?? "";
			if (args[index].isFloat)
				return args[index].floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
			return args[index].intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		sealed class AgentLlmTriple(string baseUrl, string apiKey, string model)
		{
			public string BaseUrl { get; } = baseUrl;
			public string ApiKey { get; } = apiKey;
			public string Model { get; } = model;
		}
	}
}
