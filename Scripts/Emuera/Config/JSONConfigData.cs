using System.Text.Json.Serialization;

namespace MinorShift.Emuera
{
	internal sealed class JSONConfigData
	{
		[JsonPropertyName("UseButtonFocusBackgroundColor")]
		public bool UseButtonFocusBackgroundColor { get; set; }

		[JsonPropertyName("UseNewRandom")]
		public bool UseNewRandom { get; set; }

		/// <summary>
		/// VARI/VARS 指令开关。参考 v24 通过 setting.json 提供此项，缺省 false
		/// （gEmuera 曾缺省 true，导致 v24pure 默认暴露 VARI/VARS，与参考不一致）。
		/// </summary>
		[JsonPropertyName("UseScopedVariableInstruction")]
		public bool UseScopedVariableInstruction { get; set; }

		[JsonPropertyName("RenderingBackend")]
		public RenderingBackend RenderingBackend { get; set; } = RenderingBackend.Auto;
	}
}
