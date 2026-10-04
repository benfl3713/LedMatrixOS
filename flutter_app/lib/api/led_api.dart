import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'models.dart';
import 'schedule_models.dart';
import 'screen_models.dart';
import 'result.dart';

/// The device REST API. Every call returns a [Result]; nothing throws.
abstract class LedApi {
  String get baseUrl;

  Future<Result<AppList>> getApps();
  Future<Result<void>> activateApp(String id);
  Future<Result<List<AppSetting>>> getAppSettings(String id);
  Future<Result<void>> updateAppSettings(String id, Map<String, Object?> values);
  Future<Result<DeviceSettings>> getSettings();
  Future<Result<void>> setBrightness(int value);
  Future<Result<void>> setPower(bool enabled);
  Future<Result<TransitionList>> getTransitions();
  Future<Result<void>> setTransition(String name);
  Future<Result<Health>> getHealth();

  /// The stored schedule, or null when the device does not expose the document on `GET /api/schedule`.
  Future<Result<ScheduleDocument?>> getSchedule();

  /// Replaces the whole schedule. A 400 comes back as an [ApiError] whose `errors` lists the problems.
  Future<Result<ScheduleDocument>> saveSchedule(ScheduleDocument doc);
  Future<Result<ScheduleStatus>> getScheduleStatus();

  Future<Result<List<OverlayInfo>>> getOverlays();
  Future<Result<void>> dismissOverlay(String id);
  Future<Result<void>> clearOverlays();
  Future<Result<void>> sendToast({required String message, double? seconds, String? color, String? background});
  Future<Result<void>> sendBadge({required String id, int? x, int? y, int? size, String? color, bool? pulsing});

  /// With a [message] posts the coloured message alert; without one, the plain flash.
  Future<Result<void>> sendAlert({String? message, String? color});

  Future<Result<List<ScreenSummary>>> listScreens();
  Future<Result<ScreenDefinition>> getScreen(String id);

  /// Creates or replaces a screen. A 400 comes back as an [ApiError] whose `fieldErrors` lists the problems.
  Future<Result<ScreenDefinition>> putScreen(ScreenDefinition screen);
  Future<Result<void>> deleteScreen(String id);
  Future<Result<ScreenSchema>> getScreenSchema();

  Uri get previewSocketUri;
  String previewUrl({int? cacheBust});
}

class HttpLedApi implements LedApi {
  HttpLedApi({required String baseUrl, http.Client? client, this.timeout = const Duration(seconds: 6)})
      : baseUrl = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl,
        _client = client ?? http.Client();

  @override
  final String baseUrl;
  final Duration timeout;
  final http.Client _client;

  Uri _uri(String path) => Uri.parse('$baseUrl$path');

  Future<Result<T>> _send<T>(
    Future<http.Response> Function() request,
    T Function(String body) parse,
  ) async {
    final http.Response response;
    try {
      response = await request().timeout(timeout);
    } on TimeoutException {
      return Err(ApiError(ApiErrorKind.timeout, 'Request timed out after ${timeout.inSeconds}s'));
    } catch (e) {
      return Err(ApiError(ApiErrorKind.network, 'Cannot reach the device: $e'));
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      return Err(ApiError(ApiErrorKind.http, _httpMessage(response),
          statusCode: response.statusCode,
          errors: _validationErrors(response),
          fieldErrors: _fieldErrors(response)));
    }
    try {
      return Ok(parse(response.body));
    } catch (e) {
      return Err(ApiError(ApiErrorKind.parse, 'Unexpected response from the device: $e'));
    }
  }

  static List<String> _validationErrors(http.Response r) {
    try {
      final decoded = jsonDecode(r.body);
      if (decoded is Map && decoded['errors'] is List) {
        return (decoded['errors'] as List).map((e) => _describeError(e)).toList();
      }
    } catch (_) {
      // Not a JSON validation body; the plain message is used instead.
    }
    return const [];
  }

  static String _describeError(Object? e) =>
      e is Map && e['message'] is String ? FieldError('${e['path'] ?? ''}', e['message'] as String).toString() : e.toString();

  static List<FieldError> _fieldErrors(http.Response r) {
    try {
      final decoded = jsonDecode(r.body);
      if (decoded is Map && decoded['errors'] is List) {
        return [
          for (final e in decoded['errors'] as List)
            if (e is Map && e['message'] is String) FieldError('${e['path'] ?? ''}', e['message'] as String),
        ];
      }
    } catch (_) {
      // Not a JSON validation body.
    }
    return const [];
  }

  static String _httpMessage(http.Response r) {
    var detail = r.body.trim();
    try {
      final decoded = jsonDecode(detail);
      if (decoded is Map && decoded['errors'] is List) {
        detail = (decoded['errors'] as List).map(_describeError).join('; ');
      } else if (decoded is Map && decoded['message'] is String) {
        detail = decoded['message'] as String;
        if (decoded['rejected'] is List) detail = '$detail: ${(decoded['rejected'] as List).join(', ')}';
      } else if (decoded is String) {
        detail = decoded;
      }
    } catch (_) {
      // Plain-text body; use it as is.
    }
    if (detail.length > 160) detail = '${detail.substring(0, 160)}...';
    return detail.isEmpty ? 'Device returned HTTP ${r.statusCode}' : 'HTTP ${r.statusCode}: $detail';
  }

  static const _json = {'Content-Type': 'application/json'};

  Map<String, dynamic> _map(String body) => jsonDecode(body) as Map<String, dynamic>;

  @override
  Future<Result<AppList>> getApps() => _send(() => _client.get(_uri('/api/apps')), (b) => AppList.fromJson(_map(b)));

  @override
  Future<Result<void>> activateApp(String id) =>
      _send(() => _client.post(_uri('/api/apps/${Uri.encodeComponent(id)}')), (_) {});

  @override
  Future<Result<List<AppSetting>>> getAppSettings(String id) => _send(
        () => _client.get(_uri('/api/apps/${Uri.encodeComponent(id)}/settings')),
        (b) => ((_map(b)['settings'] ?? const []) as List)
            .map((e) => AppSetting.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  @override
  Future<Result<void>> updateAppSettings(String id, Map<String, Object?> values) => _send(
        () => _client.post(_uri('/api/apps/${Uri.encodeComponent(id)}/settings'),
            headers: _json, body: jsonEncode(values)),
        (_) {},
      );

  @override
  Future<Result<DeviceSettings>> getSettings() =>
      _send(() => _client.get(_uri('/api/settings')), (b) => DeviceSettings.fromJson(_map(b)));

  @override
  Future<Result<void>> setBrightness(int value) =>
      _send(() => _client.post(_uri('/api/settings/brightness/${value.clamp(0, 255)}')), (_) {});

  @override
  Future<Result<void>> setPower(bool enabled) =>
      _send(() => _client.post(_uri('/api/settings/power/$enabled')), (_) {});

  @override
  Future<Result<TransitionList>> getTransitions() =>
      _send(() => _client.get(_uri('/api/transitions')), (b) => TransitionList.fromJson(_map(b)));

  @override
  Future<Result<void>> setTransition(String name) =>
      _send(() => _client.post(_uri('/api/settings/transition/${Uri.encodeComponent(name)}')), (_) {});

  @override
  Future<Result<Health>> getHealth() => _send(() => _client.get(_uri('/api/health')), (b) => Health.fromJson(_map(b)));

  @override
  Future<Result<ScheduleDocument?>> getSchedule() => _send(() => _client.get(_uri('/api/schedule')), (b) {
        final json = _map(b);
        return ScheduleDocument.looksLikeDocument(json) ? ScheduleDocument.fromJson(json) : null;
      });

  @override
  Future<Result<ScheduleDocument>> saveSchedule(ScheduleDocument doc) => _send(
        () => _client.put(_uri('/api/schedule'), headers: _json, body: doc.encode()),
        (b) => ScheduleDocument.fromJson(_map(b)),
      );

  @override
  Future<Result<ScheduleStatus>> getScheduleStatus() =>
      _send(() => _client.get(_uri('/api/schedule/status')), (b) => ScheduleStatus.fromJson(_map(b)));

  @override
  Future<Result<List<OverlayInfo>>> getOverlays() => _send(
        () => _client.get(_uri('/api/overlays')),
        (b) => ((_map(b)['overlays'] ?? const []) as List)
            .map((e) => OverlayInfo.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  @override
  Future<Result<void>> dismissOverlay(String id) =>
      _send(() => _client.delete(_uri('/api/overlays/${Uri.encodeComponent(id)}')), (_) {});

  @override
  Future<Result<void>> clearOverlays() => _send(() => _client.delete(_uri('/api/overlays')), (_) {});

  @override
  Future<Result<void>> sendToast({required String message, double? seconds, String? color, String? background}) => _send(
        () => _client.post(_uri('/api/overlays/toast'),
            headers: _json,
            body: jsonEncode({
              'message': message,
              if (seconds != null) 'seconds': seconds,
              if (color != null) 'color': color,
              if (background != null) 'background': background,
            })),
        (_) {},
      );

  @override
  Future<Result<void>> sendBadge({required String id, int? x, int? y, int? size, String? color, bool? pulsing}) => _send(
        () => _client.post(_uri('/api/overlays/badge'),
            headers: _json,
            body: jsonEncode({
              'id': id,
              if (x != null) 'x': x,
              if (y != null) 'y': y,
              if (size != null) 'size': size,
              if (color != null) 'color': color,
              if (pulsing != null) 'pulsing': pulsing,
            })),
        (_) {},
      );

  @override
  Future<Result<void>> sendAlert({String? message, String? color}) {
    if (message == null || message.trim().isEmpty) {
      return _send(() => _client.post(_uri('/api/notifications')), (_) {});
    }
    // The server binds the colour as a Pixel, i.e. {r,g,b}.
    return _send(
      () => _client.post(_uri('/api/notifications/message'),
          headers: _json, body: jsonEncode({'message': message, if (color != null) 'color': _rgb(color)})),
      (_) {},
    );
  }

  static Map<String, int>? _rgb(String hex) {
    var s = hex.trim();
    if (s.startsWith('#')) s = s.substring(1);
    if (s.length != 6) return null;
    final v = int.tryParse(s, radix: 16);
    if (v == null) return null;
    return {'r': (v >> 16) & 255, 'g': (v >> 8) & 255, 'b': v & 255};
  }

  @override
  Future<Result<List<ScreenSummary>>> listScreens() => _send(
        () => _client.get(_uri('/api/screens')),
        (b) => ((_map(b)['screens'] ?? const []) as List)
            .map((e) => ScreenSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  @override
  Future<Result<ScreenDefinition>> getScreen(String id) => _send(
        () => _client.get(_uri('/api/screens/${Uri.encodeComponent(id)}')),
        (b) => ScreenDefinition.fromJson(_map(b)),
      );

  @override
  Future<Result<ScreenDefinition>> putScreen(ScreenDefinition screen) => _send(
        () => _client.put(_uri('/api/screens/${Uri.encodeComponent(screen.id)}'),
            headers: _json, body: jsonEncode(screen.toJson())),
        (b) => ScreenDefinition.fromJson(_map(b)),
      );

  @override
  Future<Result<void>> deleteScreen(String id) =>
      _send(() => _client.delete(_uri('/api/screens/${Uri.encodeComponent(id)}')), (_) {});

  @override
  Future<Result<ScreenSchema>> getScreenSchema() =>
      _send(() => _client.get(_uri('/api/screens/schema')), (b) => ScreenSchema.fromJson(_map(b)));

  @override
  Uri get previewSocketUri {
    final uri = Uri.parse(baseUrl);
    return uri.replace(scheme: uri.scheme == 'https' ? 'wss' : 'ws', path: '/ws/preview');
  }

  @override
  String previewUrl({int? cacheBust}) => '$baseUrl/preview?_=${cacheBust ?? DateTime.now().millisecondsSinceEpoch}';
}
