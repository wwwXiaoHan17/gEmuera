shader_type canvas_item;

// 5x5 ColorMatrix: 4 rows (R,G,B,A output) x 4 columns (R,G,B,A input)
// plus an offset vector for the 5th column
uniform vec4 cm_r = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 cm_g = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 cm_b = vec4(0.0, 0.0, 1.0, 0.0);
uniform vec4 cm_a = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 cm_offset = vec4(0.0, 0.0, 0.0, 0.0);

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	COLOR.r = dot(cm_r, c) + cm_offset.r;
	COLOR.g = dot(cm_g, c) + cm_offset.g;
	COLOR.b = dot(cm_b, c) + cm_offset.b;
	COLOR.a = dot(cm_a, c) + cm_offset.a;
}
