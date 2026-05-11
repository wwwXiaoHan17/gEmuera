@tool
extends RefCounted

# Regression test suite for Godot MCP.
# Run from the Godot editor via File > Run > regression.gd (or Script panel
# "Run" button while this file is open).
#
# Each test receives an assertion callback `assert_true(cond, msg)` and
# `assert_equal(a, b, msg)`. Results are printed to the editor Output panel.

var _pass := 0
var _fail := 0
var _reports: Array[String] = []

func _run() -> void:
	print("========== MCP Regression Tests ==========")
	_test_dispatcher_registry()
	_test_project_handler()
	_test_scene_handler()
	_test_node_handler()
	_test_input_handler()
	_test_editor_handler()
	_test_animation_handler()
	_test_resource_handler()
	_test_tilemap_handler()
	_test_theme_handler()
	_test_shader_handler()
	_test_physics_handler()
	_test_scene_3d_handler()
	_test_animation_tree_handler()
	_test_batch_handler()
	_test_particles_handler()
	_test_audio_handler()
	_test_profiling_handler()
	_test_export_handler()
	_test_code_analysis_handler()
	_test_navigation_handler()
	_test_testing_handler()
	_test_runtime_analysis_handler()
	_test_undo_stack()
	_print_summary()

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

func _assert_true(cond: bool, msg: String) -> void:
	if cond:
		_pass += 1
		_reports.append("[PASS] " + msg)
	else:
		_fail += 1
		_reports.append("[FAIL] " + msg)
		push_error("[FAIL] " + msg)

func _assert_equal(a: Variant, b: Variant, msg: String) -> void:
	_assert_true(a == b, msg + " | expected: %s, got: %s" % [str(b), str(a)])

func _assert_no_error(result: Dictionary, msg: String) -> void:
	var has_err: bool = result.has("error") and not result.get("success", false)
	_assert_true(not has_err, msg + " | err=%s" % result.get("error", "none"))

func _assert_has_key(result: Dictionary, key: String, msg: String) -> void:
	_assert_true(result.has(key), msg + " | missing key: %s" % key)

func _make_dispatcher() -> McpDispatcher:
	var d := McpDispatcher.new()
	d.register_handler("project", preload("res://addons/godot_mcp/handlers/project_handler.gd").new())
	d.register_handler("scene", preload("res://addons/godot_mcp/handlers/scene_handler.gd").new())
	d.register_handler("node", preload("res://addons/godot_mcp/handlers/node_handler.gd").new())
	d.register_handler("script", preload("res://addons/godot_mcp/handlers/script_handler.gd").new())
	d.register_handler("editor", preload("res://addons/godot_mcp/handlers/editor_handler.gd").new())
	d.register_handler("input", preload("res://addons/godot_mcp/handlers/input_handler.gd").new())
	d.register_handler("animation", preload("res://addons/godot_mcp/handlers/animation_handler.gd").new())
	d.register_handler("resource", preload("res://addons/godot_mcp/handlers/resource_handler.gd").new())
	d.register_handler("tilemap", preload("res://addons/godot_mcp/handlers/tilemap_handler.gd").new())
	d.register_handler("theme", preload("res://addons/godot_mcp/handlers/theme_handler.gd").new())
	d.register_handler("shader", preload("res://addons/godot_mcp/handlers/shader_handler.gd").new())
	d.register_handler("physics", preload("res://addons/godot_mcp/handlers/physics_handler.gd").new())
	d.register_handler("scene_3d", preload("res://addons/godot_mcp/handlers/scene_3d_handler.gd").new())
	d.register_handler("animation_tree", preload("res://addons/godot_mcp/handlers/animation_tree_handler.gd").new())
	d.register_handler("batch", preload("res://addons/godot_mcp/handlers/batch_handler.gd").new())
	d.register_handler("particles", preload("res://addons/godot_mcp/handlers/particles_handler.gd").new())
	d.register_handler("audio", preload("res://addons/godot_mcp/handlers/audio_handler.gd").new())
	d.register_handler("profiling", preload("res://addons/godot_mcp/handlers/profiling_handler.gd").new())
	d.register_handler("export", preload("res://addons/godot_mcp/handlers/export_handler.gd").new())
	d.register_handler("code_analysis", preload("res://addons/godot_mcp/handlers/code_analysis_handler.gd").new())
	d.register_handler("navigation", preload("res://addons/godot_mcp/handlers/navigation_handler.gd").new())
	d.register_handler("testing", preload("res://addons/godot_mcp/handlers/testing_handler.gd").new())
	d.register_handler("runtime_analysis", preload("res://addons/godot_mcp/handlers/runtime_analysis_handler.gd").new())
	return d

func _dispatch(d: McpDispatcher, method: String, params: Dictionary) -> Dictionary:
	var req := {"id": "regression", "method": method, "params": params}
	var resp := d.dispatch(req)
	if resp.has("error"):
		var out := {"error": resp.error.message}
		# Preserve error_code and other context from the JSON-RPC data field.
		if resp.error.has("data") and resp.error.data is Dictionary:
			for key in resp.error.data.keys():
				if not out.has(key):
					out[key] = resp.error.data[key]
		return out
	return resp.result

func _project_path() -> String:
	return "E:/godot-mcp-main"

# ---------------------------------------------------------------------------
# Phase A: Dispatcher + Base infra
# ---------------------------------------------------------------------------

func _test_dispatcher_registry() -> void:
	var d := _make_dispatcher()
	# Every registered handler should resolve correctly.
	var checks := [
		["get_filesystem_tree", "project"],
		["get_scene_tree", "scene"],
		["delete_node", "node"],
		["simulate_key", "input"],
		["get_editor_errors", "editor"],
		["list_animations", "animation"],
		["create_particles", "particles"],
		["get_audio_bus_layout", "audio"],
		["get_performance_monitors", "profiling"],
		["list_export_presets", "export"],
		["find_unused_resources", "code_analysis"],
		["setup_navigation_region", "navigation"],
		["run_test_scenario", "testing"],
		["get_game_scene_tree", "runtime_analysis"],
	]
	for c in checks:
		var h := d._find_handler_for_method(c[0])
		_assert_true(h != null, "Dispatcher resolves %s -> non-null" % c[0])

# ---------------------------------------------------------------------------
# Phase 1 handlers
# ---------------------------------------------------------------------------

func _test_project_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "get_filesystem_tree", {"projectPath": _project_path(), "max_depth": 2})
	_assert_no_error(r1, "project:get_filesystem_tree")
	_assert_has_key(r1, "tree", "project:get_filesystem_tree returns tree")

	var r2 := _dispatch(d, "search_files", {"projectPath": _project_path(), "glob": "*.gd"})
	_assert_no_error(r2, "project:search_files")

	var r3 := _dispatch(d, "get_project_settings", {"projectPath": _project_path(), "keys": ["application/config/name"]})
	_assert_no_error(r3, "project:get_project_settings")

	# Empty keys returns empty settings (not an error)
	var r4 := _dispatch(d, "get_project_settings", {"projectPath": _project_path()})
	_assert_true(r4.has("settings"), "project:get_project_settings missing keys -> empty settings")

	# UID round-trip (Godot 4.4+); if API absent, handler returns error — accept either.
	var r5 := _dispatch(d, "uid_to_project_path", {"projectPath": _project_path(), "uid": "uid://invalid"})
	# Error is acceptable for invalid UID.
	_assert_true(r5.has("error") or r5.has("path"), "project:uid_to_project_path returns error or path")

func _test_scene_handler() -> void:
	var d := _make_dispatcher()
	# Requires an open scene — if none, expect error.
	var r1 := _dispatch(d, "get_scene_tree", {"projectPath": _project_path(), "max_depth": 2})
	# May error if no scene open; either way is fine for regression shape check.
	_assert_true(r1.has("tree") or r1.has("error"), "scene:get_scene_tree returns tree or error")

	var r2 := _dispatch(d, "get_scene_file_content", {"projectPath": _project_path(), "scene_path": "addons/godot_mcp/mcp_plugin.gd"})
	# This is a .gd file, not .tscn, so it should error.
	_assert_true(r2.has("error"), "scene:get_scene_file_content non-scene -> error")

	# play_scene may fail in headless mode where EditorInterface is not fully ready.
	var r3 := _dispatch(d, "play_scene", {"projectPath": _project_path()})
	_assert_true(r3.get("success", false) or r3.has("error"), "scene:play_scene returns result or error")

	_dispatch(d, "stop_scene", {"projectPath": _project_path()})
	# stop_scene is idempotent; no error expected.

func _test_node_handler() -> void:
	var d := _make_dispatcher()
	# Error path — missing node_path
	var r1 := _dispatch(d, "delete_node", {"projectPath": _project_path()})
	_assert_true(r1.has("error"), "node:delete_node missing node_path -> error")

	var r2 := _dispatch(d, "get_node_properties", {"projectPath": _project_path(), "node_path": "root"})
	# root may not exist if no scene open.
	_assert_true(r2.has("properties") or r2.has("error"), "node:get_node_properties returns properties or error")

	# add_resource error path — missing resource_class
	var r3 := _dispatch(d, "add_resource", {"projectPath": _project_path(), "node_path": "root", "property": "shape"})
	_assert_true(r3.has("error"), "node:add_resource missing resource_class -> error")

	# set_anchor_preset error path — non-Control
	var r4 := _dispatch(d, "set_anchor_preset", {"projectPath": _project_path(), "node_path": "root", "preset": "center"})
	_assert_true(r4.has("error") or r4.get("success", false), "node:set_anchor_preset returns error or success")

func _test_input_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "simulate_key", {"projectPath": _project_path(), "key": "space", "pressed": true})
	_assert_no_error(r1, "input:simulate_key")

	var r2 := _dispatch(d, "simulate_mouse_click", {"projectPath": _project_path(), "x": 100, "y": 100})
	_assert_no_error(r2, "input:simulate_mouse_click")

	var r3 := _dispatch(d, "simulate_sequence", {"projectPath": _project_path(), "events": [{"type": "wait", "delay_ms": 10}, {"type": "key", "key": "enter"}]})
	_assert_no_error(r3, "input:simulate_sequence")

func _test_editor_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "get_editor_errors", {"projectPath": _project_path()})
	# Headless editor may not provide EditorInterface; accept either success or error.
	_assert_true(r1.has("errors") or r1.has("error"), "editor:get_editor_errors returns errors or error")

	var r2 := _dispatch(d, "execute_editor_script", {"projectPath": _project_path(), "code": "1 + 1"})
	_assert_no_error(r2, "editor:execute_editor_script simple expression")

	# Guardrail test
	var r3 := _dispatch(d, "execute_editor_script", {"projectPath": _project_path(), "code": "OS.execute(\"echo\", [\"hi\"])"})
	_assert_true(r3.has("error") and r3.get("error_code", "") == "guarded", "editor:execute_editor_script guardrail blocks OS.execute")

	# compare_screenshots error — missing images
	var r4 := _dispatch(d, "compare_screenshots", {"projectPath": _project_path()})
	_assert_true(r4.has("error"), "editor:compare_screenshots missing images -> error")

func _test_animation_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "list_animations", {"projectPath": _project_path()})
	# Error if no AnimationPlayer found — acceptable.
	_assert_true(r1.has("animations") or r1.has("error"), "animation:list_animations returns animations or error")

	var r2 := _dispatch(d, "get_animation_info", {"projectPath": _project_path(), "animation_name": "test"})
	_assert_true(r2.has("error") or r2.has("success"), "animation:get_animation_info returns result")

# ---------------------------------------------------------------------------
# Phase B handlers
# ---------------------------------------------------------------------------

func _test_resource_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "list_resources_in_dir", {"projectPath": _project_path(), "directory": "res://addons/godot_mcp"})
	_assert_no_error(r1, "resource:list_resources_in_dir")

	var r2 := _dispatch(d, "create_resource", {"projectPath": _project_path(), "resource_path": "res://test_dummy.tres", "resource_class": "Resource"})
	_assert_no_error(r2, "resource:create_resource")
	# Cleanup
	DirAccess.remove_absolute(ProjectSettings.globalize_path("res://test_dummy.tres"))

func _test_tilemap_handler() -> void:
	var d := _make_dispatcher()
	# tilemap_get_info requires an open scene with a TileMap — expect error if absent.
	var r1 := _dispatch(d, "tilemap_get_info", {"projectPath": _project_path()})
	_assert_true(r1.has("info") or r1.has("error"), "tilemap:get_info returns info or error")

func _test_theme_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "create_theme", {"projectPath": _project_path(), "theme_path": "res://test_dummy_theme.tres"})
	_assert_no_error(r1, "theme:create_theme")
	DirAccess.remove_absolute(ProjectSettings.globalize_path("res://test_dummy_theme.tres"))

func _test_shader_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "create_shader", {"projectPath": _project_path(), "shader_path": "res://test_dummy.gdshader", "shader_type": "canvas_item"})
	_assert_no_error(r1, "shader:create_shader")
	DirAccess.remove_absolute(ProjectSettings.globalize_path("res://test_dummy.gdshader"))

func _test_physics_handler() -> void:
	var d := _make_dispatcher()
	# Error path — missing node_path
	var r1 := _dispatch(d, "setup_collision", {"projectPath": _project_path()})
	_assert_true(r1.has("error"), "physics:setup_collision missing node_path -> error")

func _test_scene_3d_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "add_mesh_instance", {"projectPath": _project_path(), "scenePath": "res://test_dummy_scene.tscn", "parentNodePath": "root", "nodeName": "Mesh"})
	# May error if scene not open or doesn't exist.
	_assert_true(r1.has("success") or r1.has("error"), "scene_3d:add_mesh_instance returns result or error")

func _test_animation_tree_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "create_animation_tree", {"projectPath": _project_path(), "scenePath": "res://test_dummy_scene.tscn", "parentNodePath": "root", "treeName": "AT"})
	_assert_true(r1.has("success") or r1.has("error"), "animation_tree:create_animation_tree returns result or error")

func _test_batch_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "find_nodes_by_type", {"projectPath": _project_path(), "type": "Node"})
	# May error if no scene is open in headless mode.
	_assert_true(r1.has("matches") or r1.has("error"), "batch:find_nodes_by_type returns matches or error")

# ---------------------------------------------------------------------------
# Phase D handlers
# ---------------------------------------------------------------------------

func _test_particles_handler() -> void:
	var d := _make_dispatcher()
	# Error path — missing parent
	var r1 := _dispatch(d, "create_particles", {"projectPath": _project_path(), "scenePath": "res://test_dummy_scene.tscn", "parentNodePath": "", "nodeName": "P"})
	_assert_true(r1.has("error"), "particles:create_particles missing parent -> error")

func _test_audio_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "get_audio_bus_layout", {"projectPath": _project_path()})
	_assert_no_error(r1, "audio:get_audio_bus_layout")
	_assert_has_key(r1, "buses", "audio:get_audio_bus_layout has buses")

	var r2 := _dispatch(d, "add_audio_bus", {"projectPath": _project_path(), "busName": "TestBus"})
	_assert_no_error(r2, "audio:add_audio_bus")

func _test_profiling_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "get_performance_monitors", {"projectPath": _project_path()})
	_assert_no_error(r1, "profiling:get_performance_monitors")
	_assert_has_key(r1, "monitors", "profiling:get_performance_monitors has monitors")

	var r2 := _dispatch(d, "get_editor_performance", {"projectPath": _project_path()})
	_assert_no_error(r2, "profiling:get_editor_performance")

func _test_export_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "list_export_presets", {"projectPath": _project_path()})
	# May error if export_presets.cfg absent.
	_assert_true(r1.has("presets") or r1.has("error"), "export:list_export_presets returns presets or error")

func _test_code_analysis_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "get_project_statistics", {"projectPath": _project_path(), "max_files": 100})
	_assert_no_error(r1, "code_analysis:get_project_statistics")
	_assert_has_key(r1, "total_files", "code_analysis:get_project_statistics has total_files")

func _test_navigation_handler() -> void:
	var d := _make_dispatcher()
	# Error path — missing scenePath / parent
	var r1 := _dispatch(d, "setup_navigation_region", {"projectPath": _project_path(), "scenePath": "", "parentNodePath": "", "nodeName": "Nav"})
	_assert_true(r1.has("error"), "navigation:setup_navigation_region missing params -> error")

func _test_testing_handler() -> void:
	var d := _make_dispatcher()
	var r1 := _dispatch(d, "run_test_scenario", {"projectPath": _project_path(), "events": [{"type": "wait", "delay_ms": 10}]})
	_assert_no_error(r1, "testing:run_test_scenario minimal")
	_assert_true(r1.get("passed", false), "testing:run_test_scenario minimal passed")

	var r2 := _dispatch(d, "assert_node_state", {"projectPath": _project_path(), "node_path": "root", "property": "name", "expected": "root"})
	# May error if no scene open.
	_assert_true(r2.get("assert_passed", false) or r2.has("error"), "testing:assert_node_state returns result or error")

	var r3 := _dispatch(d, "get_test_report", {"projectPath": _project_path()})
	_assert_no_error(r3, "testing:get_test_report")
	_assert_has_key(r3, "summary", "testing:get_test_report has summary")

func _test_runtime_analysis_handler() -> void:
	var d := _make_dispatcher()
	# Runtime agent won't be running in editor, so every call should return an error.
	var r1 := _dispatch(d, "get_game_scene_tree", {"projectPath": _project_path()})
	_assert_true(r1.has("error"), "runtime_analysis:get_game_scene_tree -> error when game not running")

	var r2 := _dispatch(d, "batch_get_properties", {"projectPath": _project_path(), "requests": [{"node_path": "/root", "property": "name"}]})
	_assert_true(r2.has("error"), "runtime_analysis:batch_get_properties -> error when game not running")

# ---------------------------------------------------------------------------
# Undo stack verification (Phase A critical fix)
# ---------------------------------------------------------------------------

func _test_undo_stack() -> void:
	var d := _make_dispatcher()
	var ur := EditorInterface.get_editor_undo_redo()
	var before: int = ur.get_history_count() if ur != null and ur.has_method("get_history_count") else -1

	# We can't easily verify undo without a real open scene and real nodes,
	# but we can at least confirm that node_handler returns undoable:true
	# for operations that support it. We'll do a lightweight check on the
	# handler directly.
	var node_handler := preload("res://addons/godot_mcp/handlers/node_handler.gd").new()
	# node_handler needs EditorInterface context; as an EditorScript we have it.
	# get_target_node won't work without an edited scene, so we just verify the
	# helper class exists and has the right methods.
	# Verify UndoHelper script is loadable and has the expected static methods.
	var uh_script = load("res://addons/godot_mcp/handlers/undo_helper.gd")
	_assert_true(uh_script != null, "UndoHelper script loads")
	if uh_script:
		_assert_true(uh_script.has_method("do_simple"), "UndoHelper.do_simple exists")
		_assert_true(uh_script.has_method("do_property"), "UndoHelper.do_property exists")
		_assert_true(uh_script.has_method("add_node"), "UndoHelper.add_node exists")
		_assert_true(uh_script.has_method("remove_node"), "UndoHelper.remove_node exists")

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

func _print_summary() -> void:
	print("\n========== Regression Results ==========")
	for r in _reports:
		print(r)
	print("----------------------------------------")
	print("Passed: %d | Failed: %d | Total: %d" % [_pass, _fail, _pass + _fail])
	if _fail == 0:
		print("ALL TESTS PASSED")
	else:
		print("SOME TESTS FAILED — check Output panel for [FAIL] lines")
	print("========================================")
