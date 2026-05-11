@tool
class_name TypeHelper

# Single source of truth for marshalling Godot Variants ↔ JSON-friendly
# values. Replaces the per-handler _variant_to_json / _json_to_variant
# duplicates. parse_godot_value() is additive: any input shape that
# previously worked still works the same way; new string forms (like
# "Vector2(1,2)" or "#ff0000") get richer parsing.

# ---------------------------------------------------------------------------
# Variant -> JSON
# ---------------------------------------------------------------------------

static func variant_to_json(value: Variant) -> Variant:
	if value == null:
		return null

	var t = typeof(value)
	match t:
		TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING:
			return value
		TYPE_VECTOR2, TYPE_VECTOR2I:
			return {"x": value.x, "y": value.y}
		TYPE_VECTOR3, TYPE_VECTOR3I:
			return {"x": value.x, "y": value.y, "z": value.z}
		TYPE_VECTOR4, TYPE_VECTOR4I:
			return {"x": value.x, "y": value.y, "z": value.z, "w": value.w}
		TYPE_QUATERNION:
			return {"x": value.x, "y": value.y, "z": value.z, "w": value.w}
		TYPE_COLOR:
			return {"r": value.r, "g": value.g, "b": value.b, "a": value.a}
		TYPE_RECT2, TYPE_RECT2I:
			return {"x": value.position.x, "y": value.position.y, "w": value.size.x, "h": value.size.y}
		TYPE_TRANSFORM2D:
			return {
				"x": {"x": value.x.x, "y": value.x.y},
				"y": {"x": value.y.x, "y": value.y.y},
				"origin": {"x": value.origin.x, "y": value.origin.y},
			}
		TYPE_BASIS:
			return {
				"x": {"x": value.x.x, "y": value.x.y, "z": value.x.z},
				"y": {"x": value.y.x, "y": value.y.y, "z": value.y.z},
				"z": {"x": value.z.x, "y": value.z.y, "z": value.z.z},
			}
		TYPE_TRANSFORM3D:
			return {
				"basis": {
					"x": {"x": value.basis.x.x, "y": value.basis.x.y, "z": value.basis.x.z},
					"y": {"x": value.basis.y.x, "y": value.basis.y.y, "z": value.basis.y.z},
					"z": {"x": value.basis.z.x, "y": value.basis.z.y, "z": value.basis.z.z},
				},
				"origin": {"x": value.origin.x, "y": value.origin.y, "z": value.origin.z},
			}
		TYPE_PROJECTION:
			return str(value)
		TYPE_PLANE:
			return {"x": value.normal.x, "y": value.normal.y, "z": value.normal.z, "d": value.d}
		TYPE_AABB:
			return {
				"position": {"x": value.position.x, "y": value.position.y, "z": value.position.z},
				"size": {"x": value.size.x, "y": value.size.y, "z": value.size.z},
			}
		TYPE_NODE_PATH, TYPE_STRING_NAME:
			return str(value)
		TYPE_OBJECT:
			if value == null:
				return null
			if value is Resource:
				return value.resource_path
			if value is Node:
				return str(value.get_path())
			return str(value)
		TYPE_ARRAY:
			var arr = []
			for item in value:
				arr.append(variant_to_json(item))
			return arr
		TYPE_DICTIONARY:
			var dict = {}
			for k in value.keys():
				dict[str(k)] = variant_to_json(value[k])
			return dict
		TYPE_PACKED_BYTE_ARRAY, TYPE_PACKED_INT32_ARRAY, TYPE_PACKED_INT64_ARRAY, \
		TYPE_PACKED_FLOAT32_ARRAY, TYPE_PACKED_FLOAT64_ARRAY, TYPE_PACKED_STRING_ARRAY:
			var arr2 = []
			for item in value:
				arr2.append(item)
			return arr2
		TYPE_PACKED_VECTOR2_ARRAY, TYPE_PACKED_VECTOR3_ARRAY, TYPE_PACKED_COLOR_ARRAY:
			var arr3 = []
			for item in value:
				arr3.append(variant_to_json(item))
			return arr3
		_:
			return value

# ---------------------------------------------------------------------------
# JSON -> Variant (additive parser)
# ---------------------------------------------------------------------------

# Parse arbitrary JSON-friendly input into the closest Godot Variant.
# - target_type: optional typeof() of the destination property (helps disambiguation)
# - class_hint: optional class name string (e.g. "NodePath", "Texture2D")
# Never raises. Returns the input unchanged if no rule matches.
static func parse_godot_value(value: Variant, target_type: int = TYPE_NIL, class_hint: String = "") -> Variant:
	# Strings target_type=String: never auto-convert
	if value is String and target_type == TYPE_STRING:
		return value
	if value is String and target_type == TYPE_STRING_NAME:
		return StringName(value)

	# 1. String inputs: prefer typed constructors, then hex, then resource paths.
	if value is String:
		var s: String = value
		if s.is_empty():
			return s

		# 1a. Godot text-form constructors via str_to_var.
		# Guarded by prefix to avoid accidentally parsing arbitrary user text.
		if _looks_like_typed_constructor(s):
			var parsed = str_to_var(s)
			if parsed != null and typeof(parsed) != TYPE_NIL:
				return parsed

		# 1b. Hex color (#ff0000 or #ff0000aa or 6/8 hex digits).
		if Color.html_is_valid(s.lstrip("#")):
			return Color.from_string(s.lstrip("#"), Color.WHITE)
		if s.begins_with("#") and Color.html_is_valid(s):
			return Color.from_string(s, Color.WHITE)

		# 1c. NodePath hint
		if class_hint == "NodePath" or target_type == TYPE_NODE_PATH:
			return NodePath(s)

		# 1d. Resource paths
		if s.begins_with("res://") or s.begins_with("uid://"):
			if target_type == TYPE_OBJECT or _hint_is_resource(class_hint):
				var loaded = load(s)
				if loaded:
					return loaded
			return s

		return s

	# 2. Dictionary inputs: existing shape-based parsing (preserved verbatim
	# so handlers that already pass {x,y,z} or {r,g,b,a} continue to work).
	if value is Dictionary:
		var d: Dictionary = value
		# Transform3D: {basis: {x,y,z}, origin: {x,y,z}}
		if d.has("basis") and d.has("origin") and d["basis"] is Dictionary and d["origin"] is Dictionary:
			var basis = _dict_to_basis(d["basis"])
			var origin_dict = d["origin"]
			var origin = Vector3(float(origin_dict.get("x", 0)), float(origin_dict.get("y", 0)), float(origin_dict.get("z", 0)))
			return Transform3D(basis, origin)
		# Transform2D: {x: {x,y}, y: {x,y}, origin: {x,y}}
		if d.has("x") and d.has("y") and d.has("origin"):
			var xv = d["x"]
			var yv = d["y"]
			var ov = d["origin"]
			if xv is Dictionary and yv is Dictionary and ov is Dictionary:
				return Transform2D(
					Vector2(float(xv.get("x", 0)), float(xv.get("y", 0))),
					Vector2(float(yv.get("x", 0)), float(yv.get("y", 0))),
					Vector2(float(ov.get("x", 0)), float(ov.get("y", 0)))
				)
		# AABB: {position, size}
		if d.has("position") and d.has("size") and d["position"] is Dictionary and d["size"] is Dictionary:
			var p = d["position"]
			var sz = d["size"]
			return AABB(
				Vector3(float(p.get("x", 0)), float(p.get("y", 0)), float(p.get("z", 0))),
				Vector3(float(sz.get("x", 0)), float(sz.get("y", 0)), float(sz.get("z", 0)))
			)
		# Plane: {normal: {x,y,z}, d}
		if d.has("normal") and d.has("d") and d["normal"] is Dictionary:
			var n = d["normal"]
			return Plane(
				Vector3(float(n.get("x", 0)), float(n.get("y", 0)), float(n.get("z", 0))),
				float(d.get("d", 0))
			)
		# Vector4 / Quaternion: {x,y,z,w}
		if d.has("x") and d.has("y") and d.has("z") and d.has("w"):
			if target_type == TYPE_QUATERNION or class_hint == "Quaternion":
				return Quaternion(float(d.x), float(d.y), float(d.z), float(d.w))
			if target_type == TYPE_VECTOR4I:
				return Vector4i(int(d.x), int(d.y), int(d.z), int(d.w))
			return Vector4(float(d.x), float(d.y), float(d.z), float(d.w))
		# Vector3 / Vector3i
		if d.has("x") and d.has("y") and d.has("z"):
			if target_type == TYPE_VECTOR3I:
				return Vector3i(int(d.x), int(d.y), int(d.z))
			return Vector3(float(d.x), float(d.y), float(d.z))
		# Rect2 / Rect2i
		if d.has("x") and d.has("y") and d.has("w") and d.has("h"):
			if target_type == TYPE_RECT2I:
				return Rect2i(int(d.x), int(d.y), int(d.w), int(d.h))
			return Rect2(float(d.x), float(d.y), float(d.w), float(d.h))
		# Vector2 / Vector2i
		if d.has("x") and d.has("y"):
			if target_type == TYPE_VECTOR2I:
				return Vector2i(int(d.x), int(d.y))
			return Vector2(float(d.x), float(d.y))
		# Color
		if d.has("r") and d.has("g") and d.has("b"):
			return Color(float(d.r), float(d.g), float(d.b), float(d.get("a", 1.0)))

	# 3. Pass through everything else
	return value

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

# Returns true for strings that look like Godot's str() output for typed
# values — only those should be fed to str_to_var() to avoid accidentally
# parsing user content like "myFunc(arg)" or "(1, 2, 3)" alone.
static func _looks_like_typed_constructor(s: String) -> bool:
	if s.find("(") == -1 or s.find(")") == -1:
		return false
	const PREFIXES = [
		"Vector2(", "Vector2i(",
		"Vector3(", "Vector3i(",
		"Vector4(", "Vector4i(",
		"Color(",
		"Rect2(", "Rect2i(",
		"Transform2D(", "Transform3D(",
		"Quaternion(", "Plane(", "AABB(", "Basis(",
		"NodePath(",
	]
	for p in PREFIXES:
		if s.begins_with(p):
			return true
	return false

static func _hint_is_resource(class_hint: String) -> bool:
	if class_hint.is_empty():
		return false
	const SUFFIXES = ["Resource", "Material", "Texture", "Mesh", "Shader", "Script", "PackedScene", "Animation", "Curve", "Theme"]
	for suf in SUFFIXES:
		if class_hint == suf or class_hint.ends_with(suf):
			return true
	return false

static func _dict_to_basis(d: Dictionary) -> Basis:
	var x = d.get("x", {"x": 1, "y": 0, "z": 0})
	var y = d.get("y", {"x": 0, "y": 1, "z": 0})
	var z = d.get("z", {"x": 0, "y": 0, "z": 1})
	if not (x is Dictionary): x = {"x": 1, "y": 0, "z": 0}
	if not (y is Dictionary): y = {"x": 0, "y": 1, "z": 0}
	if not (z is Dictionary): z = {"x": 0, "y": 0, "z": 1}
	return Basis(
		Vector3(float(x.get("x", 1)), float(x.get("y", 0)), float(x.get("z", 0))),
		Vector3(float(y.get("x", 0)), float(y.get("y", 1)), float(y.get("z", 0))),
		Vector3(float(z.get("x", 0)), float(z.get("y", 0)), float(z.get("z", 1)))
	)
