shader_type canvas_item;
render_mode blend_disabled;

// Same as color_matrix.gdshader but with blend_disabled for compositing use.
// When rendering to a SubViewport for CPU-side readback, we need raw straight-alpha
// output without premultiplied blending onto the transparent background.
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
