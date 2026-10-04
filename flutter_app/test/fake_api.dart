import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:led_matrix_controller/api/led_api.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/api/schedule_models.dart';
import 'package:led_matrix_controller/api/screen_models.dart';
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

  // Schedule ---------------------------------------------------------------

  ScheduleDocument scheduleDoc = const ScheduleDocument(
    playlists: [
      PlaylistDoc(name: 'day', entries: [
        EntryDoc(appId: 'clock', durationMs: 20000),
        EntryDoc(appId: 'weather', durationMs: 10000, transition: 'slide'),
      ]),
      PlaylistDoc(name: 'night', entries: [EntryDoc(appId: 'fire', durationMs: 60000)]),
    ],
    rules: [
      RuleDoc(playlistId: 'day', priority: 10, startTime: '07:00', endTime: '22:00'),
      RuleDoc(playlistId: 'night', priority: 60, startTime: '22:00', endTime: '07:00', daysMask: 62, brightnessOverride: 51),
    ],
  );

  /// Validation errors the next save returns (cleared after use).
  List<String> saveErrors = [];
  final List<ScheduleDocument> savedDocs = [];

  ScheduleStatus scheduleStatus = ScheduleStatus(
    activeRule: const ActiveRuleInfo(index: 0, playlistId: 'day', priority: 10),
    playlist: 'day',
    entryIndex: 1,
    entryCount: 2,
    appId: 'weather',
    nextChange: DateTime.now().add(const Duration(minutes: 5)),
    nextChangeReason: 'playlist',
  );

  @override
  Future<Result<ScheduleDocument?>> getSchedule() async => Ok(scheduleDoc);

  @override
  Future<Result<ScheduleDocument>> saveSchedule(ScheduleDocument doc) async {
    calls.add('saveSchedule');
    if (saveErrors.isNotEmpty) {
      final errors = saveErrors;
      saveErrors = [];
      return Err(ApiError(ApiErrorKind.http, 'HTTP 400: ${errors.join('; ')}', statusCode: 400, errors: errors));
    }
    savedDocs.add(doc);
    scheduleDoc = doc;
    return Ok(doc);
  }

  @override
  Future<Result<ScheduleStatus>> getScheduleStatus() async => Ok(scheduleStatus);

  // Overlays ----------------------------------------------------------------

  List<OverlayInfo> overlays = const [
    OverlayInfo(id: 'toast-1', kind: 'toast', text: 'Dinner is ready', priority: 10, remainingSeconds: 3.2),
    OverlayInfo(id: 'badge-1', kind: 'badge', priority: 5),
  ];

  @override
  Future<Result<List<OverlayInfo>>> getOverlays() async => Ok(overlays);

  @override
  Future<Result<void>> dismissOverlay(String id) async {
    calls.add('dismiss:$id');
    overlays = [for (final o in overlays) if (o.id != id) o];
    return const Ok(null);
  }

  @override
  Future<Result<void>> clearOverlays() async {
    calls.add('clearOverlays');
    overlays = const [];
    return const Ok(null);
  }

  @override
  Future<Result<void>> sendToast({required String message, double? seconds, String? color, String? background}) async {
    calls.add('toast:$message:${seconds?.round()}:$color:$background');
    return const Ok(null);
  }

  @override
  Future<Result<void>> sendBadge({required String id, int? x, int? y, int? size, String? color, bool? pulsing}) async {
    calls.add('badge:$id:$x:$y:$size:$color:$pulsing');
    return const Ok(null);
  }

  @override
  Future<Result<void>> sendAlert({String? message, String? color}) async {
    calls.add('alert:$message:$color');
    return const Ok(null);
  }

  // Screens -----------------------------------------------------------------

  final Map<String, ScreenDefinition> screens = {
    'demo': ScreenDefinition(
      id: 'demo',
      name: 'Demo',
      root: ScreenNode(type: 'stack', props: {'direction': 'vertical'}, children: [
        ScreenNode(type: 'label', props: {'text': 'Hello {weather.temp}', 'color': '#FFFFFF', 'mystery': 7}),
      ]),
    ),
  };

  /// Validation errors the next putScreen returns (cleared after use).
  List<FieldError> screenErrors = [];
  final List<ScreenDefinition> putScreens = [];

  static const screenSchema = ScreenSchema(
    maxDepth: 8,
    maxNodes: 200,
    fonts: ['Big', 'Small'],
    slots: ['children', 'top', 'bottom', 'left', 'right', 'fill', 'item'],
    commonProps: [
      PropSchema(name: 'width', kind: PropKind.int),
      PropSchema(name: 'halign', kind: PropKind.enumeration, options: ['left', 'center', 'right']),
      PropSchema(name: 'visible', kind: PropKind.bool),
    ],
    nodeTypes: [
      NodeTypeSchema(type: 'stack', props: [
        PropSchema(name: 'direction', kind: PropKind.enumeration, options: ['horizontal', 'vertical']),
        PropSchema(name: 'gap', kind: PropKind.int),
      ], slots: ['children']),
      NodeTypeSchema(type: 'dock', props: [], slots: ['top', 'bottom', 'left', 'right', 'fill']),
      NodeTypeSchema(type: 'label', props: [
        PropSchema(name: 'text', kind: PropKind.binding),
        PropSchema(name: 'font', kind: PropKind.enumeration, options: ['Big', 'Small']),
        PropSchema(name: 'color', kind: PropKind.color),
      ], slots: []),
      NodeTypeSchema(type: 'clock', props: [
        PropSchema(name: 'format', kind: PropKind.string),
        PropSchema(name: 'color', kind: PropKind.color),
      ], slots: []),
      NodeTypeSchema(type: 'list', props: [
        PropSchema(name: 'source', kind: PropKind.binding),
      ], slots: ['item']),
    ],
    bindingKeys: [
      BindingKeyInfo(key: 'time', kind: 'time', description: 'Current time'),
      BindingKeyInfo(key: 'weather.<field>', kind: 'weather', description: 'Weather value', fields: ['temp', 'high']),
      BindingKeyInfo(key: 'item', kind: 'item', description: 'Current list item', insideListOnly: true),
    ],
  );

  @override
  Future<Result<List<ScreenSummary>>> listScreens() async =>
      Ok([for (final s in screens.values) ScreenSummary(id: s.id, name: s.name)]);

  @override
  Future<Result<ScreenDefinition>> getScreen(String id) async {
    final s = screens[id];
    if (s == null) return const Err(ApiError(ApiErrorKind.http, 'HTTP 404: not found', statusCode: 404));
    return Ok(s.clone());
  }

  @override
  Future<Result<ScreenDefinition>> putScreen(ScreenDefinition screen) async {
    calls.add('putScreen:${screen.id}');
    if (screenErrors.isNotEmpty) {
      final errors = screenErrors;
      screenErrors = [];
      return Err(ApiError(ApiErrorKind.http, 'HTTP 400: ${errors.join('; ')}',
          statusCode: 400, errors: [for (final e in errors) e.toString()], fieldErrors: errors));
    }
    final copy = screen.clone();
    putScreens.add(copy);
    screens[screen.id] = copy;
    return Ok(copy.clone());
  }

  @override
  Future<Result<void>> deleteScreen(String id) async {
    calls.add('deleteScreen:$id');
    screens.remove(id);
    return const Ok(null);
  }

  @override
  Future<Result<ScreenSchema>> getScreenSchema() async => const Ok(screenSchema);

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
