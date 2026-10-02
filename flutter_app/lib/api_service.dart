import 'dart:convert';
import 'dart:async';
import 'package:http/http.dart' as http;

class LedMatrixApi {
  final String baseUrl;
  
  LedMatrixApi({required this.baseUrl});
  
  Future<Map<String, dynamic>> getApps() async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/apps'),
      headers: {'Content-Type': 'application/json'},
    );
    
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load apps: ${response.statusCode}');
    }
  }
  
  Future<bool> activateApp(String appId) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/apps/$appId'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  Future<Map<String, dynamic>?> getAppSettings(String appId) async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/apps/$appId/settings'),
      headers: {'Content-Type': 'application/json'},
    );
    
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else if (response.statusCode == 400) {
      return null; // App not active
    } else {
      throw Exception('Failed to load app settings: ${response.statusCode}');
    }
  }
  
  Future<bool> updateAppSettings(String appId, Map<String, dynamic> settings) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/apps/$appId/settings'),
      headers: {'Content-Type': 'application/json'},
      body: json.encode(settings),
    );
    
    return response.statusCode == 200;
  }
  
  Future<Map<String, dynamic>> getSettings() async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/settings'),
      headers: {'Content-Type': 'application/json'},
    );
    
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load settings: ${response.statusCode}');
    }
  }
  
  Future<bool> setBrightness(int brightness) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/settings/brightness/$brightness'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  Future<bool> setPower(bool enabled) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/settings/power/$enabled'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  /// Shows a banner over whatever is running. Colours are hex strings such as "#ff0000".
  Future<bool> showToast(String message, {double seconds = 4, String? color, String? background}) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/overlays/toast'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'message': message,
        'seconds': seconds,
        if (color != null) 'color': color,
        if (background != null) 'background': background,
      }),
    );

    return response.statusCode == 200;
  }

  Future<bool> setBadge(String id, {String? color, bool pulsing = true}) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/overlays/badge'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'id': id, if (color != null) 'color': color, 'pulsing': pulsing}),
    );

    return response.statusCode == 200;
  }

  Future<bool> dismissOverlay(String id) async {
    final response = await http.delete(Uri.parse('$baseUrl/api/overlays/${Uri.encodeComponent(id)}'));
    return response.statusCode == 200;
  }

  Future<bool> clearOverlays() async {
    final response = await http.delete(Uri.parse('$baseUrl/api/overlays'));
    return response.statusCode == 200;
  }

  Future<bool> reloadSchedule() async {
    final response = await http.post(Uri.parse('$baseUrl/api/schedule/reload'));
    return response.statusCode == 200;
  }

  Future<Map<String, dynamic>> getHealth() async {
    final response = await http.get(Uri.parse('$baseUrl/api/health'));
    if (response.statusCode == 200) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw Exception('Failed to load health: ${response.statusCode}');
  }

  Uri getPreviewSocketUri() {
    final uri = Uri.parse(baseUrl);
    return uri.replace(scheme: uri.scheme == 'https' ? 'wss' : 'ws', path: '/ws/preview');
  }

  String getPreviewUrl() {
    return '$baseUrl/preview?_=${DateTime.now().millisecondsSinceEpoch}';
  }
}

class MatrixApp {
  final String id;
  final String name;
  final bool hasSettings;
  
  MatrixApp({required this.id, required this.name, required this.hasSettings});
  
  factory MatrixApp.fromJson(Map<String, dynamic> json) {
    return MatrixApp(
      id: json['id'] ?? '',
      name: json['name'] ?? '',
      hasSettings: json['hasSettings'] ?? false,
    );
  }
}

class MatrixSettings {
  final int width;
  final int height;
  final int brightness;
  final bool isRunning;
  final bool isEnabled;
  
  MatrixSettings({
    required this.width,
    required this.height,
    required this.brightness,
    required this.isRunning,
    required this.isEnabled,
  });
  
  factory MatrixSettings.fromJson(Map<String, dynamic> json) {
    return MatrixSettings(
      width: json['width'] ?? 0,
      height: json['height'] ?? 0,
      brightness: json['brightness'] ?? 0,
      isRunning: json['isRunning'] ?? false,
      isEnabled: json['isEnabled'] ?? true,
    );
  }
}

enum AppSettingType {
  boolean,
  integer,
  string,
  color,
  select
}

class AppSetting {
  final String key;
  final String name;
  final String description;
  final AppSettingType type;
  final dynamic defaultValue;
  final dynamic currentValue;
  final dynamic minValue;
  final dynamic maxValue;
  final List<String>? options;
  
  AppSetting({
    required this.key,
    required this.name,
    required this.description,
    required this.type,
    required this.defaultValue,
    required this.currentValue,
    this.minValue,
    this.maxValue,
    this.options,
  });
  
  factory AppSetting.fromJson(Map<String, dynamic> json) {
    AppSettingType type;
    switch (json['type'] ?? 0) {
      case 0:
        type = AppSettingType.boolean;
        break;
      case 1:
        type = AppSettingType.integer;
        break;
      case 2:
        type = AppSettingType.string;
        break;
      case 3:
        type = AppSettingType.color;
        break;
      case 4:
        type = AppSettingType.select;
        break;
      default:
        type = AppSettingType.string;
    }
    
    return AppSetting(
      key: json['key'] ?? '',
      name: json['name'] ?? '',
      description: json['description'] ?? '',
      type: type,
      defaultValue: json['defaultValue'],
      currentValue: json['currentValue'],
      minValue: json['minValue'],
      maxValue: json['maxValue'],
      options: json['options'] != null ? List<String>.from(json['options']) : null,
    );
  }
}
