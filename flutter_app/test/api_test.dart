import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:led_matrix_controller/api/led_api.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/preview_frame.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/core/app_icons.dart';
import 'package:led_matrix_controller/features/apps/color_picker.dart';

void main() {
  group('brightness conversion', () {
    test('percent to wire value spans 0-255', () {
      expect(percentToBrightness(0), 0);
      expect(percentToBrightness(50), 128);
      expect(percentToBrightness(100), 255);
    });

    test('wire value to percent', () {
      expect(brightnessToPercent(0), 0);
      expect(brightnessToPercent(255), 100);
      expect(brightnessToPercent(128), 50);
    });

    test('values are clamped', () {
      expect(percentToBrightness(150), 255);
      expect(brightnessToPercent(999), 100);
      expect(brightnessToPercent(-5), 0);
    });

    test('round trip is stable for every percent', () {
      for (var p = 0; p <= 100; p++) {
        expect(brightnessToPercent(percentToBrightness(p)), p);
      }
    });
  });

  group('models', () {
    test('DeviceSettings parses fps and brightness, with copyWith', () {
      final s = DeviceSettings.fromJson({
        'width': 256,
        'height': 64,
        'brightness': 255,
        'fps': 30,
        'isRunning': true,
        'isEnabled': false,
      });
      expect(s.fps, 30);
      expect(s.brightnessPercent, 100);
      expect(s.isEnabled, isFalse);
      final c = s.copyWith(brightness: 0, isEnabled: true);
      expect(c.brightnessPercent, 0);
      expect(c.isEnabled, isTrue);
      expect(c.fps, 30);
    });

    test('AppList parses and finds the active app', () {
      final l = AppList.fromJson({
        'apps': [
          {'id': 'clock', 'name': 'Clock', 'hasSettings': true},
          {'id': 'fire', 'name': 'Fire', 'hasSettings': false},
        ],
        'activeApp': 'fire',
      });
      expect(l.apps, hasLength(2));
      expect(l.active?.name, 'Fire');
      expect(l.copyWith(activeApp: 'clock').active?.id, 'clock');
    });

    test('AppSetting parses every type and bounds', () {
      final s = AppSetting.fromJson({
        'key': 'size',
        'name': 'Size',
        'description': 'd',
        'type': 1,
        'defaultValue': 2,
        'currentValue': 3,
        'minValue': 1,
        'maxValue': 5,
        'options': null,
      });
      expect(s.type, AppSettingType.integer);
      expect(s.minValue, 1);
      expect(s.copyWith(currentValue: 4).currentValue, 4);
      expect(AppSetting.fromJson({'key': 'c', 'name': 'C', 'type': 3}).type, AppSettingType.color);
      final select = AppSetting.fromJson({'key': 's', 'name': 'S', 'type': 4, 'options': ['a', 'b']});
      expect(select.options, ['a', 'b']);
      expect(AppSetting.fromJson({'key': 'x', 'name': 'X', 'type': 99}).type, AppSettingType.string);
    });

    test('TransitionList and Health parse', () {
      final t = TransitionList.fromJson({'current': 'fade', 'transitions': ['fade', 'random']});
      expect(t.transitions, ['fade', 'random']);
      final h = Health.fromJson({'status': 'ok', 'uptimeSeconds': 5, 'width': 256, 'height': 64});
      expect(h.isOk, isTrue);
      expect(h.copyWith(status: 'stopped').isOk, isFalse);
    });
  });

  group('preview frames', () {
    test('parses little-endian header and RGB payload', () {
      final bytes = Uint8List.fromList([2, 0, 1, 0, 10, 20, 30, 40, 50, 60]);
      final f = parsePreviewFrame(bytes)!;
      expect((f.width, f.height), (2, 1));
      expect(f.rgba, [10, 20, 30, 255, 40, 50, 60, 255]);
    });

    test('rejects short or empty frames', () {
      expect(parsePreviewFrame([1, 2]), isNull);
      expect(parsePreviewFrame([0, 0, 0, 0]), isNull);
      expect(parsePreviewFrame([2, 0, 1, 0, 1, 2, 3]), isNull);
    });
  });

  group('HttpLedApi', () {
    test('getApps parses a response', () async {
      final api = HttpLedApi(
        baseUrl: 'http://x/',
        client: MockClient((req) async {
          expect(req.url.toString(), 'http://x/api/apps');
          return http.Response(jsonEncode({'apps': [], 'activeApp': null}), 200);
        }),
      );
      final r = await api.getApps();
      expect(r.isOk, isTrue);
    });

    test('setBrightness sends 0-255', () async {
      Uri? seen;
      final api = HttpLedApi(
        baseUrl: 'http://x',
        client: MockClient((req) async {
          seen = req.url;
          return http.Response('{}', 200);
        }),
      );
      await api.setBrightness(128);
      expect(seen!.path, '/api/settings/brightness/128');
    });

    test('HTTP errors become typed errors with the server message', () async {
      final api = HttpLedApi(
        baseUrl: 'http://x',
        client: MockClient((req) async => http.Response(jsonEncode({'message': 'Unknown setting(s)', 'rejected': ['a']}), 400)),
      );
      final r = await api.updateAppSettings('clock', {'a': 1});
      final e = r.errorOrNull!;
      expect(e.kind, ApiErrorKind.http);
      expect(e.statusCode, 400);
      expect(e.message, contains('Unknown setting(s): a'));
    });

    test('network failures are surfaced, not thrown', () async {
      final api = HttpLedApi(baseUrl: 'http://x', client: MockClient((req) async => throw Exception('down')));
      final r = await api.getSettings();
      expect(r.errorOrNull!.kind, ApiErrorKind.network);
    });

    test('timeouts are typed', () async {
      final api = HttpLedApi(
        baseUrl: 'http://x',
        timeout: const Duration(milliseconds: 20),
        client: MockClient((req) => Completer<http.Response>().future),
      );
      final r = await api.getHealth();
      expect(r.errorOrNull!.kind, ApiErrorKind.timeout);
    });

    test('malformed bodies are parse errors', () async {
      final api = HttpLedApi(baseUrl: 'http://x', client: MockClient((req) async => http.Response('not json', 200)));
      expect((await api.getTransitions()).errorOrNull!.kind, ApiErrorKind.parse);
    });

    test('preview socket uri swaps the scheme', () {
      expect(HttpLedApi(baseUrl: 'http://x:5005').previewSocketUri.toString(), 'ws://x:5005/ws/preview');
      expect(HttpLedApi(baseUrl: 'https://x').previewSocketUri.toString(), 'wss://x/ws/preview');
    });
  });

  group('helpers', () {
    test('hex colours round trip', () {
      expect(parseHexColor('#00E5FF'), isNotNull);
      expect(colorToHex(parseHexColor('#00e5ff')!), '#00E5FF');
      expect(colorToHex(parseHexColor('f0a')!), '#FF00AA');
      expect(parseHexColor('nope'), isNull);
      expect(parseHexColor(null), isNull);
    });

    test('icon helper falls back to a default', () {
      expect(iconForApp('some-new-app'), iconForApp('another-unknown'));
      expect(iconForApp('tube-status'), isNot(iconForApp('some-new-app')));
    });
  });
}
