@tool
class_name McpHandler

# Override in subclasses to declare every method this handler accepts.
# Used by the dispatcher to build a method->handler lookup at registration time.
func get_methods() -> PackedStringArray:
	return PackedStringArray()

func handle(method: String, params: Dictionary) -> Dictionary:
	push_error("McpHandler.handle() must be overridden")
	return {"error": "Not implemented"}

func require_param(params: Dictionary, key: String) -> Variant:
	if not params.has(key):
		push_error("Missing required parameter: " + key)
		return null
	return params[key]

func require_scene_opened(scene_path: String) -> bool:
	var editor_interface = Engine.get_singleton("EditorInterface")
	if not editor_interface:
		push_error("EditorInterface not available")
		return false
	var resource_path = scene_path
	if not resource_path.begins_with("res://"):
		resource_path = "res://" + resource_path
	return FileAccess.file_exists(resource_path)

func get_editor_interface() -> EditorInterface:
	# EditorInterface is available as a singleton in the editor context
	if Engine.has_singleton("EditorInterface"):
		return Engine.get_singleton("EditorInterface")
	return null

func get_editor_tree() -> SceneTree:
	var ei = get_editor_interface()
	if ei:
		return ei.get_tree()
	return null

func get_edited_scene_root() -> Node:
	var ei = get_editor_interface()
	if ei:
		return ei.get_edited_scene_root()
	return null

# Shared node lookup. Accepts "root", "/root", relative paths from scene root,
# or absolute paths starting with "/" (the leading slash is stripped before resolving).
func get_target_node(node_path: String) -> Node:
	var root = get_edited_scene_root()
	if not root:
		return null
	if node_path == "root" or node_path == "/root":
		return root
	var target = root.get_node_or_null(node_path)
	if not target and node_path.begins_with("/"):
		target = root.get_node_or_null(node_path.substr(1))
	return target
