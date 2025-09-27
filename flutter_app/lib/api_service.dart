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
  
  Future<bool> setFps(int fps) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/settings/fps/$fps'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  String getPreviewUrl() {
    return '$baseUrl/preview?_=${DateTime.now().millisecondsSinceEpoch}';
  }
}

class MatrixApp {
  final String id;
  final String name;
  
  MatrixApp({required this.id, required this.name});
  
  factory MatrixApp.fromJson(Map<String, dynamic> json) {
    return MatrixApp(
      id: json['id'] ?? '',
      name: json['name'] ?? '',
    );
  }
}

class MatrixSettings {
  final int width;
  final int height;
  final int brightness;
  final int fps;
  final bool isRunning;
  
  MatrixSettings({
    required this.width,
    required this.height,
    required this.brightness,
    required this.fps,
    required this.isRunning,
  });
  
  factory MatrixSettings.fromJson(Map<String, dynamic> json) {
    return MatrixSettings(
      width: json['width'] ?? 0,
      height: json['height'] ?? 0,
      brightness: json['brightness'] ?? 0,
      fps: json['fps'] ?? 30,
      isRunning: json['isRunning'] ?? false,
    );
  }
}