/// Brightness is 0-255 on the wire and a percentage in the UI.
int brightnessToPercent(int value) => (value.clamp(0, 255) * 100 / 255).round();

int percentToBrightness(int percent) => (percent.clamp(0, 100) * 255 / 100).round();

int _int(Object? v, [int fallback = 0]) => v is num ? v.round() : fallback;

class MatrixApp {
  const MatrixApp({required this.id, required this.name, required this.hasSettings});

  final String id;
  final String name;
  final bool hasSettings;

  factory MatrixApp.fromJson(Map<String, dynamic> json) => MatrixApp(
        id: (json['id'] ?? '') as String,
        name: (json['name'] ?? '') as String,
        hasSettings: (json['hasSettings'] ?? false) as bool,
      );

  MatrixApp copyWith({String? id, String? name, bool? hasSettings}) =>
      MatrixApp(id: id ?? this.id, name: name ?? this.name, hasSettings: hasSettings ?? this.hasSettings);
}

class AppList {
  const AppList({required this.apps, this.activeApp});

  final List<MatrixApp> apps;
  final String? activeApp;

  factory AppList.fromJson(Map<String, dynamic> json) => AppList(
        apps: ((json['apps'] ?? const []) as List).map((e) => MatrixApp.fromJson(e as Map<String, dynamic>)).toList(),
        activeApp: json['activeApp'] as String?,
      );

  AppList copyWith({List<MatrixApp>? apps, String? activeApp}) =>
      AppList(apps: apps ?? this.apps, activeApp: activeApp ?? this.activeApp);

  MatrixApp? get active {
    for (final a in apps) {
      if (a.id == activeApp) return a;
    }
    return null;
  }
}

class DeviceSettings {
  const DeviceSettings({
    required this.width,
    required this.height,
    required this.brightness,
    required this.fps,
    required this.isRunning,
    required this.isEnabled,
  });

  final int width;
  final int height;

  /// 0-255, as the server stores it.
  final int brightness;
  final int fps;
  final bool isRunning;
  final bool isEnabled;

  int get brightnessPercent => brightnessToPercent(brightness);

  factory DeviceSettings.fromJson(Map<String, dynamic> json) => DeviceSettings(
        width: _int(json['width']),
        height: _int(json['height']),
        brightness: _int(json['brightness']),
        fps: _int(json['fps']),
        isRunning: (json['isRunning'] ?? false) as bool,
        isEnabled: (json['isEnabled'] ?? true) as bool,
      );

  DeviceSettings copyWith({int? width, int? height, int? brightness, int? fps, bool? isRunning, bool? isEnabled}) =>
      DeviceSettings(
        width: width ?? this.width,
        height: height ?? this.height,
        brightness: brightness ?? this.brightness,
        fps: fps ?? this.fps,
        isRunning: isRunning ?? this.isRunning,
        isEnabled: isEnabled ?? this.isEnabled,
      );
}

class TransitionList {
  const TransitionList({required this.current, required this.transitions});

  final String current;
  final List<String> transitions;

  factory TransitionList.fromJson(Map<String, dynamic> json) => TransitionList(
        current: (json['current'] ?? '') as String,
        transitions: ((json['transitions'] ?? const []) as List).map((e) => e.toString()).toList(),
      );

  TransitionList copyWith({String? current, List<String>? transitions}) =>
      TransitionList(current: current ?? this.current, transitions: transitions ?? this.transitions);
}

class Health {
  const Health({
    required this.status,
    this.activeApp,
    required this.isEnabled,
    required this.width,
    required this.height,
    required this.uptimeSeconds,
  });

  final String status;
  final String? activeApp;
  final bool isEnabled;
  final int width;
  final int height;
  final int uptimeSeconds;

  bool get isOk => status == 'ok';

  factory Health.fromJson(Map<String, dynamic> json) => Health(
        status: (json['status'] ?? 'unknown') as String,
        activeApp: json['activeApp'] as String?,
        isEnabled: (json['isEnabled'] ?? true) as bool,
        width: _int(json['width']),
        height: _int(json['height']),
        uptimeSeconds: _int(json['uptimeSeconds']),
      );

  Health copyWith({String? status, String? activeApp, bool? isEnabled, int? width, int? height, int? uptimeSeconds}) =>
      Health(
        status: status ?? this.status,
        activeApp: activeApp ?? this.activeApp,
        isEnabled: isEnabled ?? this.isEnabled,
        width: width ?? this.width,
        height: height ?? this.height,
        uptimeSeconds: uptimeSeconds ?? this.uptimeSeconds,
      );
}

enum AppSettingType { boolean, integer, string, color, select }

class AppSetting {
  const AppSetting({
    required this.key,
    required this.name,
    this.description = '',
    required this.type,
    this.defaultValue,
    this.currentValue,
    this.minValue,
    this.maxValue,
    this.options,
  });

  final String key;
  final String name;
  final String description;
  final AppSettingType type;
  final Object? defaultValue;
  final Object? currentValue;
  final num? minValue;
  final num? maxValue;
  final List<String>? options;

  factory AppSetting.fromJson(Map<String, dynamic> json) {
    final rawType = json['type'];
    final type = rawType is int && rawType >= 0 && rawType < AppSettingType.values.length
        ? AppSettingType.values[rawType]
        : AppSettingType.string;
    final options = json['options'];
    return AppSetting(
      key: (json['key'] ?? '') as String,
      name: (json['name'] ?? '') as String,
      description: (json['description'] ?? '') as String,
      type: type,
      defaultValue: json['defaultValue'],
      currentValue: json['currentValue'],
      minValue: json['minValue'] is num ? json['minValue'] as num : null,
      maxValue: json['maxValue'] is num ? json['maxValue'] as num : null,
      options: options is List ? options.map((e) => e.toString()).toList() : null,
    );
  }

  AppSetting copyWith({Object? currentValue}) => AppSetting(
        key: key,
        name: name,
        description: description,
        type: type,
        defaultValue: defaultValue,
        currentValue: currentValue ?? this.currentValue,
        minValue: minValue,
        maxValue: maxValue,
        options: options,
      );
}
