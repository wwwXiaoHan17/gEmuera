@tool
class_name McpProtocol

const JSONRPC_VERSION = "2.0"

static func parse_request(buffer: PackedByteArray) -> Dictionary:
	var text = buffer.get_string_from_utf8()
	if text.is_empty():
		return {}
	var json = JSON.new()
	var err = json.parse(text)
	if err != OK:
		return {
			"error": {
				"code": -32700,
				"message": "Parse error: " + json.get_error_message()
			}
		}
	return json.get_data()

static func build_result(id: String, result: Dictionary) -> String:
	var response = {
		"jsonrpc": JSONRPC_VERSION,
		"id": id,
		"result": result
	}
	return JSON.stringify(response)

static func build_error(id: String, code: int, message: String) -> String:
	var response = {
		"jsonrpc": JSONRPC_VERSION,
		"id": id,
		"error": {
			"code": code,
			"message": message
		}
	}
	return JSON.stringify(response)

static func build_notification(method: String, params: Dictionary) -> String:
	var notification = {
		"jsonrpc": JSONRPC_VERSION,
		"method": method,
		"params": params
	}
	return JSON.stringify(notification)
