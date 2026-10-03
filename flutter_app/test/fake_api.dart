import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:led_matrix_controller/api/led_api.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/main.dart';

class FakeApi implements LedApi {
  FakeApi({
    this.failActivate = false,
    this.failApps = false,
  });

  bool failActivate;
  bool failApps;
  String active = 'clock';
  int brightness = 128;
  bool power = true;
  String transition = 'fade';

  final List<String> calls = [];
  final List<Map<String, Object?>> settingUpdates = [];

  final apps = const [
    MatrixApp(id: 'clock', name: 'Clock', hasSettings: true),
    MatrixApp(id: 'weather', name: 'Weather', hasSettings: true),
    MatrixApp(id: 'fire', name: 'Fire', hasSettings: false),
  ];

  final settings = <AppSetting>[
    const AppSetting(key: 'showSeconds', name: 'Show seconds', type: AppSettingType.boolean, currentValue: true),
    const AppSetting(
        key: 'size', name: 'Size', type: AppSettingType.integer, currentValue: 3, minValue: 1, maxValue: 5),
    const AppSetting(key: 'tint', name: 'Tint', type: AppSettingType.color, currentValue: '#FF0000'),
    const AppSetting(
        key: 'style', name: 'Style', type: AppSettingType.select, currentValue: 'a', options: ['a', 'b']),
  ];

  @override
  String get baseUrl => 'http://fake';

  @override
  Future<Result<AppList>> getApps() async {
    if (failApps) return const Err(ApiError(ApiErrorKind.network, 'Cannot reach the device: boom'));
    return Ok(AppList(apps: apps, activeApp: active));
  }

  @override
  Future<Result<void>> activateApp(String id) async {
    calls.add('activate:$id');
    if (failActivate) return const Err(ApiError(ApiErrorKind.http, 'HTTP 404: nope', statusCode: 404));
    active = id;
    return const Ok(null);
  }

  @override
  Future<Result<List<AppSetting>>> getAppSettings(String id) async => Ok(settings);

  @override
  Future<Result<void>> updateAppSettings(String id, Map<String, Object?> values) async {
    settingUpdates.add(values);
    return const Ok(null);
  }

  @override
  Future<Result<DeviceSettings>> getSettings() async => Ok(DeviceSettings(
      width: 256, height: 64, brightness: brightness, fps: 30, isRunning: true, isEnabled: power));

  @override
  Future<Result<void>> setBrightness(int value) async {
    calls.add('brightness:$value');
    brightness = value;
    return const Ok(null);
  }

  @override
  Future<Result<void>> setPower(bool enabled) async {
    calls.add('power:$enabled');
    power = enabled;
    return const Ok(null);
  }

  @override
  Future<Result<TransitionList>> getTransitions() async =>
      Ok(TransitionList(current: transition, transitions: const ['fade', 'slide', 'random']));

  @override
  Future<Result<void>> setTransition(String name) async {
    calls.add('transition:$name');
    transition = name;
    return const Ok(null);
  }

  @override
  Future<Result<Health>> getHealth() async => const Ok(
      Health(status: 'ok', activeApp: 'clock', isEnabled: true, width: 256, height: 64, uptimeSeconds: 3700));

  @override
  Uri get previewSocketUri => Uri.parse('ws://fake/ws/preview');

  @override
  String previewUrl({int? cacheBust}) => 'http://fake/preview';
}

Duration? _noRetry(int retryCount, Object error) => null;

/// The whole app with the API and preview feed faked and background polling off.
Widget testApp(FakeApi api) => ProviderScope(
      retry: _noRetry,
      overrides: [
        apiProvider.overrideWithValue(api),
        pollIntervalProvider.overrideWithValue(null),
        previewFeedProvider.overrideWith((ref) => Stream.value(const PreviewUpdate(connected: true))),
      ],
      child: const LedMatrixApp(),
    );
