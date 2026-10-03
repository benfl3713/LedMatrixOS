import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'models.dart';
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
      return Err(ApiError(ApiErrorKind.http, _httpMessage(response), statusCode: response.statusCode));
    }
    try {
      return Ok(parse(response.body));
    } catch (e) {
      return Err(ApiError(ApiErrorKind.parse, 'Unexpected response from the device: $e'));
    }
  }

  static String _httpMessage(http.Response r) {
    var detail = r.body.trim();
    try {
      final decoded = jsonDecode(detail);
      if (decoded is Map && decoded['message'] is String) {
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
  Uri get previewSocketUri {
    final uri = Uri.parse(baseUrl);
    return uri.replace(scheme: uri.scheme == 'https' ? 'wss' : 'ws', path: '/ws/preview');
  }

  @override
  String previewUrl({int? cacheBust}) => '$baseUrl/preview?_=${cacheBust ?? DateTime.now().millisecondsSinceEpoch}';
}
