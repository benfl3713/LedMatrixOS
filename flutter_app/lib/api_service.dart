import 'dart:convert';
import 'dart:async';
import 'package:http/http.dart' as http;

abstract class LedMatrixApi {
  Future<Map<String, dynamic>> getApps();
  Future<bool> activateApp(String appId);
  Future<Map<String, dynamic>?> getAppSettings(String appId);
  Future<bool> updateAppSettings(String appId, Map<String, dynamic> settings);
  Future<Map<String, dynamic>> getSettings();
  Future<bool> setBrightness(int brightness);
  Future<bool> setPower(bool enabled);
  String getPreviewUrl();
}

class HttpLedMatrixApi implements LedMatrixApi {
  final String baseUrl;
  
  HttpLedMatrixApi({required this.baseUrl});
  
  @override
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
  
  @override
  Future<bool> activateApp(String appId) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/apps/$appId'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  @override
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
  
  @override
  Future<bool> updateAppSettings(String appId, Map<String, dynamic> settings) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/apps/$appId/settings'),
      headers: {'Content-Type': 'application/json'},
      body: json.encode(settings),
    );
    
    return response.statusCode == 200;
  }
  
  @override
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
  
  @override
  Future<bool> setBrightness(int brightness) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/settings/brightness/$brightness'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  @override
  Future<bool> setPower(bool enabled) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/settings/power/$enabled'),
      headers: {'Content-Type': 'application/json'},
    );
    
    return response.statusCode == 200;
  }
  
  @override
  String getPreviewUrl() {
    return '$baseUrl/preview?_=${DateTime.now().millisecondsSinceEpoch}';
  }
}

class MockLedMatrixApi implements LedMatrixApi {
  String _activeAppId = 'scrolling-text';
  int _brightness = 50;
  bool _isEnabled = true;
  
  final Map<String, List<Map<String, dynamic>>> _mockAppSettings = {
    'scrolling-text': [
      {
        'key': 'text',
        'name': 'Text',
        'description': 'Text to display',
        'type': 2,
        'defaultValue': 'Hello World',
        'currentValue': 'Hello Mock Mode'
      },
      {
        'key': 'speed',
        'name': 'Speed',
        'description': 'Scroll speed',
        'type': 1,
        'defaultValue': 10,
        'currentValue': 15,
        'minValue': 1,
        'maxValue': 50
      }
    ],
    'clock': [
      {
        'key': 'show_date',
        'name': 'Show Date',
        'description': 'Whether to show the date',
        'type': 0,
        'defaultValue': true,
        'currentValue': true
      }
    ],
    'solid_color': [
      {'key': 'red', 'name': 'Red', 'description': 'Red color component (0-255)', 'type': 1, 'defaultValue': 20, 'currentValue': 144, 'minValue': 0, 'maxValue': 255},
      {'key': 'green', 'name': 'Green', 'description': 'Green color component (0-255)', 'type': 1, 'defaultValue': 255, 'currentValue': 68, 'minValue': 0, 'maxValue': 255},
      {'key': 'blue', 'name': 'Blue', 'description': 'Blue color component (0-255)', 'type': 1, 'defaultValue': 0, 'currentValue': 0, 'minValue': 0, 'maxValue': 255}
    ],
    'flip-clock': [
      {'key': 'showSeconds', 'name': 'Show Seconds', 'description': 'Display seconds with flip animation', 'type': 0, 'defaultValue': true, 'currentValue': true},
      {'key': 'show24Hour', 'name': '24-Hour Format', 'description': 'Use 24-hour format instead of 12-hour', 'type': 0, 'defaultValue': true, 'currentValue': true},
      {'key': 'textColor', 'name': 'Text Color', 'description': 'Color of the flip cards text', 'type': 4, 'defaultValue': 'White', 'currentValue': 'White', 'options': ['White', 'Red', 'Green', 'Blue', 'Yellow', 'Cyan', 'Magenta']},
      {'key': 'backgroundColor', 'name': 'Background Color', 'description': 'Color of the flip cards background', 'type': 4, 'defaultValue': 'Black', 'currentValue': 'Black', 'options': ['Black', 'DarkBlue', 'DarkGray', 'White']}
    ],
    'countdown-timer': [
      {'key': 'durationMinutes', 'name': 'Duration (Minutes)', 'description': 'How long to count down from', 'type': 1, 'defaultValue': 5, 'currentValue': 45, 'minValue': 1, 'maxValue': 99},
      {'key': 'autoRestart', 'name': 'Auto Restart', 'description': 'Automatically restart after completion', 'type': 0, 'defaultValue': false, 'currentValue': false},
      {'key': 'textColor', 'name': 'Text Color', 'description': 'Color of the timer display', 'type': 4, 'defaultValue': 'Cyan', 'currentValue': 'Yellow', 'options': ['White', 'Red', 'Green', 'Blue', 'Yellow', 'Cyan', 'Magenta', 'Orange']},
      {'key': 'backgroundColor', 'name': 'Background Color', 'description': 'Background color', 'type': 4, 'defaultValue': 'Black', 'currentValue': 'Black', 'options': ['Black', 'DarkBlue', 'DarkGray']}
    ],
    'tube-line': [
      {'key': 'lineId', 'name': 'Line', 'description': 'TfL line ID to render.', 'type': 4, 'defaultValue': 'jubilee', 'currentValue': 'jubilee', 'options': ['bakerloo', 'central', 'circle', 'district', 'hammersmith-city', 'jubilee', 'metropolitan', 'northern', 'piccadilly', 'victoria', 'waterloo-city', 'dlr', 'elizabeth', 'london-overground', 'liberty', 'lioness', 'mildmay', 'suffragette', 'weaver', 'windrush', 'tram']}
    ],
    'tube-departures': [
      {'key': 'stationSearch', 'name': 'Station Search', 'description': 'Type a station name (e.g. Baker Street).', 'type': 2, 'defaultValue': '', 'currentValue': 'st paul'},
      {'key': 'stationId', 'name': 'Station ID', 'description': 'TfL Naptan ID (auto-filled when you select from Station Select).', 'type': 2, 'defaultValue': '', 'currentValue': '940GZZLUBND'},
      {'key': 'maxDepartures', 'name': 'Max Departures', 'description': 'Number of departures to cycle through', 'type': 1, 'defaultValue': 3, 'currentValue': 6, 'minValue': 1, 'maxValue': 12},
      {'key': 'colorDeparturesByLine', 'name': 'Colour Departures By Line', 'description': 'When enabled, each departure row uses the line colour.', 'type': 0, 'defaultValue': false, 'currentValue': true}
    ],
    'home': [
      {'key': 'displayMode', 'name': 'Display Mode', 'description': 'Visual style for the display', 'type': 4, 'defaultValue': 'Ambient Particles', 'currentValue': 'Ambient Particles', 'options': ['Ambient Particles', 'Flowing Waves', 'Starfield', 'Geometric Art', 'Minimalist']},
      {'key': 'showDate', 'name': 'Show Date', 'description': 'Display the current date', 'type': 0, 'defaultValue': true, 'currentValue': false},
      {'key': 'show24Hour', 'name': '24-Hour Format', 'description': 'Use 24-hour time format', 'type': 0, 'defaultValue': true, 'currentValue': true},
      {'key': 'theme', 'name': 'Theme', 'description': 'Color theme for the display', 'type': 4, 'defaultValue': 'Calm Blue', 'currentValue': 'Calm Blue', 'options': ['Calm Blue', 'Warm Sunset', 'Forest Green', 'Lavender Dreams', 'Monochrome']},
      {'key': 'ambientSpeed', 'name': 'Animation Speed', 'description': 'Speed of ambient animations (1-10)', 'type': 1, 'defaultValue': 3, 'currentValue': 3, 'minValue': 1, 'maxValue': 10}
    ]
  };

  @override
  Future<Map<String, dynamic>> getApps() async {
    await Future.delayed(const Duration(milliseconds: 500));
    return {
      "apps": [
        {"id": "home", "name": "Home", "hasSettings": true},
        {"id": "clock", "name": "Clock", "hasSettings": true},
        {"id": "solid_color", "name": "Solid Color", "hasSettings": true},
        {"id": "rainbow-spiral", "name": "Rainbow Spiral", "hasSettings": false},
        {"id": "bouncing-balls", "name": "Bouncing Balls", "hasSettings": false},
        {"id": "matrix-rain", "name": "Matrix Rain", "hasSettings": false},
        {"id": "geometric-patterns", "name": "Geometric Patterns", "hasSettings": false},
        {"id": "animated-clock", "name": "Animated Clock", "hasSettings": false},
        {"id": "dvd-logo", "name": "DVD Logo", "hasSettings": false},
        {"id": "weather", "name": "Weather", "hasSettings": false},
        {"id": "spotify", "name": "Spotify", "hasSettings": false},
        {"id": "flip-clock", "name": "Flip Clock", "hasSettings": true},
        {"id": "countdown-timer", "name": "Countdown Timer", "hasSettings": true},
        {"id": "scrolling-text", "name": "Scrolling Text", "hasSettings": true},
        {"id": "equalizer", "name": "Equalizer Visualizer", "hasSettings": true},
        {"id": "fire", "name": "Fire", "hasSettings": false},
        {"id": "tube-status", "name": "Tube Status", "hasSettings": false},
        {"id": "tube-line", "name": "Tube Line", "hasSettings": true},
        {"id": "tube-departures", "name": "Tube Departures", "hasSettings": true},
        {"id": "dashboard", "name": "Morning Dashboard", "hasSettings": true}
      ],
      "activeApp": _activeAppId
    };
  }

  @override
  Future<bool> activateApp(String appId) async {
    await Future.delayed(const Duration(milliseconds: 200));
    _activeAppId = appId;
    return true;
  }

  @override
  Future<Map<String, dynamic>?> getAppSettings(String appId) async {
    await Future.delayed(const Duration(milliseconds: 300));
    if (_mockAppSettings.containsKey(appId)) {
      return {'settings': _mockAppSettings[appId]};
    }
    return {'settings': []};
  }

  @override
  Future<bool> updateAppSettings(String appId, Map<String, dynamic> settings) async {
    await Future.delayed(const Duration(milliseconds: 200));
    if (_mockAppSettings.containsKey(appId)) {
      final appSettings = _mockAppSettings[appId]!;
      settings.forEach((key, value) {
        final index = appSettings.indexWhere((s) => s['key'] == key);
        if (index != -1) {
          appSettings[index]['currentValue'] = value;
        }
      });
    }
    return true;
  }

  @override
  Future<Map<String, dynamic>> getSettings() async {
    await Future.delayed(const Duration(milliseconds: 400));
    return {
      'width': 64,
      'height': 32,
      'brightness': _brightness,
      'isRunning': true,
      'isEnabled': _isEnabled,
    };
  }

  @override
  Future<bool> setBrightness(int brightness) async {
    await Future.delayed(const Duration(milliseconds: 100));
    _brightness = brightness;
    return true;
  }

  @override
  Future<bool> setPower(bool enabled) async {
    await Future.delayed(const Duration(milliseconds: 100));
    _isEnabled = enabled;
    return true;
  }

  @override
  String getPreviewUrl() {
    return 'https://picsum.photos/256/128?_=${DateTime.now().millisecondsSinceEpoch}';
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
