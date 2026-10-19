shader_type canvas_item;

// ============================================================
// health_bar.gdshader —— 可调试血条
//
// 应用对象：ColorRect（或 TextureRect）挂 ShaderMaterial。
// 兼容渲染器：canvas_item，Forward+ / Mobile / Compatibility 通吃。
//
// 编辑器内调试方式：
//   1. Inspector 里所有参数都是滑杆/取色器，直接拖 ratio / ghost_ratio 看效果。
//   2. debug_view 滑到 1..5 可逐层可视化：填充遮罩 / 圆角遮罩 / 填充坐标t / ghost / 低血脉冲。
//   3. Godot 4.7 实时调试器：鼠标悬停在 fragment() 里的中间变量上，预览窗口会热力图显示该值。
//   4. Shader 编辑器自带预览，改了 uniform 默认值立即生效。
//
// GDScript 驱动（血条节点 _process 或受伤回调里）：
//     material.set_shader_parameter("ratio", hp / hp_max)
//     material.set_shader_parameter("ghost_ratio", displayed_hp / hp_max)
// C# 等价：material.SetShaderParameter("ratio", hp / hp_max)
// ============================================================

// ---- 基础颜色 ----
uniform vec4 fill_color : source_color = vec4(0.22, 0.85, 0.30, 1.0);  // 主血条色
uniform vec4 back_color : source_color = vec4(0.08, 0.09, 0.11, 1.0);   // 底色（空血部分）
uniform vec4 ghost_color : source_color = vec4(1.0, 0.95, 0.55, 0.9);   // 延迟残留（受伤后白条）
uniform vec4 border_color : source_color = vec4(1.0, 1.0, 1.0, 0.55);   // 描边色
uniform float border_width : hint_range(0.0, 0.1, 0.005) = 0.02;        // 描边宽度（相对半宽）

// ---- 血量 ----
uniform float ratio : hint_range(0.0, 1.0, 0.01) = 0.7;            // 当前血量 0..1（核心驱动参数）
uniform float ghost_ratio : hint_range(0.0, 1.0, 0.01) = 1.0;      // ghost 血量（≥ ratio 时显示残留条）

// ---- 形状 ----
uniform vec2 fill_direction = vec2(1.0, 0.0);
// 填充增长方向（归一化，血量增加时填充边缘移动的方向）：
//   (1,0)  左→右（左锚定，右端先空——标准血条）
//   (-1,0) 右→左（右锚定，左端先空）
//   (0,-1) 下→上（下锚定，顶部先空——能量条）
//   (0,1)  上→下（上锚定，底部先空）
//   任意对角也行（斜向血条）
uniform float roundness : hint_range(0.0, 0.5, 0.01) = 0.15;       // 圆角半径（相对半宽；0.5=胶囊）
uniform float edge_softness : hint_range(0.0, 0.05, 0.001) = 0.006;// 填充前沿软边（抗锯齿）

// ---- 反馈 ----
uniform bool hp_gradient = false;                            // 血量渐变：满绿 → 低红（自动上色）
uniform float low_hp : hint_range(0.0, 1.0, 0.01) = 0.25;   // 低血量阈值
uniform float pulse_speed : hint_range(0.0, 10.0, 0.1) = 4.0;  // 低血量闪烁频率
uniform float scan_speed : hint_range(0.0, 3.0, 0.1) = 0.8;    // 扫描流光速度
uniform float scan_width : hint_range(0.0, 0.3, 0.01) = 0.12;  // 扫描流光宽度

// ---- 调试 ----
uniform int debug_view : hint_range(0, 5, 1) = 0;
// 0=正常  1=填充遮罩  2=圆角遮罩  3=填充坐标t  4=ghost遮罩  5=低血量脉冲

// ---- SDF 圆角矩形（《Godot着色器宝典》ch08）----
float sd_rounded_rect(vec2 p, vec2 half_size, float r) {
	vec2 q = abs(p) - half_size + r;
	return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - r;
}
float fill(float d, float w) {
	return 1.0 - smoothstep(-w, w, d);
}

void fragment() {
	vec2 p = UV - vec2(0.5);   // 中心化 [-0.5, 0.5]

	// 1. 圆角遮罩：外框 + 内区（描边 = 两者之差）
	float roundness_inner = max(roundness - border_width, 0.0);
	float outer_mask = fill(sd_rounded_rect(p, vec2(0.5), roundness), edge_softness);
	float inner_mask = fill(sd_rounded_rect(p, vec2(0.5) - vec2(border_width), roundness_inner), edge_softness);
	float border_mask = outer_mask * (1.0 - inner_mask);

	// 2. 填充坐标 t：把 2D UV 投影到填充方向，得 0..1（t=0 起始端，t=1 末端）
	float dir_len = max(length(fill_direction), 0.001);
	float t = dot(UV - vec2(0.5), fill_direction / dir_len) + 0.5;

	// 3. 血量遮罩：smoothstep 给前沿一条软边（ch06 塑形函数）
	float fill_mask  = smoothstep(t, t + edge_softness * 2.0, ratio);
	float ghost_mask = smoothstep(t, t + edge_softness * 2.0, ghost_ratio) * (1.0 - fill_mask);

	// 4. 低血量脉冲：ratio < low_hp 时主填充明暗闪烁（ch05 多频呼吸思想）
	float danger = 1.0 - smoothstep(low_hp, low_hp + 0.08, ratio);
	float pulse = 0.7 + 0.3 * sin(TIME * pulse_speed);
	float danger_pulse = mix(1.0, pulse, danger);

	// 5. 扫描流光：一条亮带沿填充方向扫过（只显示在已填充区）
	float scan_t = fract(TIME * scan_speed);
	float scan = (1.0 - smoothstep(0.0, scan_width, abs(t - scan_t))) * 0.25;

	// 6. 顶部玻璃高光
	float top_highlight = 1.0 - smoothstep(0.0, 0.35, UV.y);

	// 7. 装配
	vec3 fill_rgb = fill_color.rgb;
	if (hp_gradient) {
		// 血量渐变：满血绿 → 低血红（uniform 布尔，整条 draw 同分支，无 divergence）
		fill_rgb = mix(vec3(0.85, 0.2, 0.15), vec3(0.25, 0.9, 0.35), smoothstep(0.0, 1.0, ratio));
	}

	vec3 col = back_color.rgb;                                   // 底色
	col = mix(col, ghost_color.rgb, ghost_mask);                 // ghost 残留
	col = mix(col, fill_rgb * danger_pulse, fill_mask);          // 主填充（含低血脉冲）
	col += fill_rgb * top_highlight * 0.18 * fill_mask;          // 玻璃高光
	col += scan * fill_mask;                                     // 扫描流光
	col = mix(col, border_color.rgb, border_mask);               // 描边

	// 8. 调试视图（uniform 分支，非逐像素动态分支）
	if (debug_view == 1) col = vec3(fill_mask);
	else if (debug_view == 2) col = vec3(inner_mask);
	else if (debug_view == 3) col = vec3(t);
	else if (debug_view == 4) col = vec3(ghost_mask);
	else if (debug_view == 5) col = vec3(danger_pulse);

	COLOR = vec4(col, outer_mask);
}
