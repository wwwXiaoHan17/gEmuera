using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinorShift.Emuera
{
	internal static class JSONConfig
	{
		private const string ConfigFileName = "setting.json";
		private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
		{
			AllowTrailingCommas = true,
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
			WriteIndented = true,
		};

		static JSONConfig()
		{
			JsonOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
		}

		public static JSONConfigData Data { get; private set; } = new JSONConfigData();

		public static bool Load(ConfigData configData)
		{
			Data = new JSONConfigData();
			string configPath = GetConfigPath();
			if (string.IsNullOrEmpty(configPath))
			{
				Config.SetJsonConfig(Data);
				return false;
			}

			try
			{
				if (!File.Exists(configPath))
					SaveDefault(configPath);

				string json = File.ReadAllText(configPath, Encoding.UTF8);
				JSONConfigData loaded = JsonSerializer.Deserialize<JSONConfigData>(json, JsonOptions);
				if (loaded != null)
					Data = loaded;
			}
			catch (Exception ex)
			{
				// 配置损坏不能阻断游戏启动；保留默认值并把问题写入诊断日志。
				GenericUtils.Warn("[CONFIG] setting.json load failed: " + ex.GetType().Name + ": " + ex.Message);
			}

			ApplyToLegacyConfig(configData);
			Config.SetJsonConfig(Data);
			return true;
		}

		public static bool Save()
		{
			string configPath = GetConfigPath();
			if (string.IsNullOrEmpty(configPath))
				return false;
			try
			{
				string json = JsonSerializer.Serialize(Data ?? new JSONConfigData(), JsonOptions);
				File.WriteAllText(configPath, json, Encoding.UTF8);
				return true;
			}
			catch (Exception ex)
			{
				GenericUtils.Warn("[CONFIG] setting.json save failed: " + ex.GetType().Name + ": " + ex.Message);
				return false;
			}
		}

		private static string GetConfigPath()
		{
			if (string.IsNullOrEmpty(Program.ExeDir))
				return null;
			return Path.Combine(Program.ExeDir, ConfigFileName);
		}

		private static void SaveDefault(string configPath)
		{
			string json = JsonSerializer.Serialize(Data, JsonOptions);
			File.WriteAllText(configPath, json, Encoding.UTF8);
		}

		private static void ApplyToLegacyConfig(ConfigData configData)
		{
			if (configData == null)
				return;

			AConfigItem scopedItem = configData.GetConfigItem(ConfigCode.UseScopedVariableInstruction);
			if (scopedItem != null)
			{
				scopedItem.SetValue(Data.UseScopedVariableInstruction);
				Data.UseScopedVariableInstruction = scopedItem.GetValue<bool>();
			}
		}
	}
}
