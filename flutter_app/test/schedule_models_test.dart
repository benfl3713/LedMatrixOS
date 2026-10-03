import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:led_matrix_controller/api/led_api.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/api/schedule_models.dart';

const _serverJson = '''
{
  "playlists": [
    {"name": "day", "skipUnavailable": true, "entries": [
      {"appId": "clock", "durationMs": 20000},
      {"appId": "weather", "durationMs": 10000, "transition": "slide", "settings": {"units": "metric", "n": 3}}
    ]}
  ],
  "rules": [
    {"playlistId": "day", "priority": 60, "startTime": "22:00", "endTime": "07:00", "daysMask": 62, "brightnessOverride": 51, "condition": "bus_due:490"},
    {"playlistId": "day"}
  ]
}
''';

void main() {
  group('ScheduleDocument', () {
    test('parses the server shape and applies the server defaults', () {
      final doc = ScheduleDocument.fromJson(jsonDecode(_serverJson) as Map<String, dynamic>);
      expect(doc.playlists.single.skipUnavailable, isTrue);
      expect(doc.playlists.single.entries[1].transition, 'slide');
      expect(doc.rules[0].daysMask, 62);
      expect(doc.rules[0].brightnessOverride, 51);
      expect(doc.rules[0].wraps, isTrue);
      expect(doc.rules[1].priority, 50);
      expect(doc.rules[1].daysMask, 127);
      expect(doc.rules[1].isAllDay, isTrue);
    });

    test('round-trips without losing fields (including entry settings)', () {
      final first = ScheduleDocument.fromJson(jsonDecode(_serverJson) as Map<String, dynamic>);
      final second = ScheduleDocument.fromJson(jsonDecode(first.encode()) as Map<String, dynamic>);
      expect(second.sameAs(first), isTrue);
      expect(second.playlists.single.entries[1].settings, {'units': 'metric', 'n': 3});
    });

    test('null fields are omitted from the wire form', () {
      final json = const RuleDoc(playlistId: 'day').toJson();
      expect(json.keys, containsAll(['playlistId', 'priority', 'daysMask']));
      expect(json.containsKey('startTime'), isFalse);
      expect(json.containsKey('brightnessOverride'), isFalse);
      expect(json.containsKey('condition'), isFalse);
      expect(const EntryDoc(appId: 'clock').toJson().containsKey('transition'), isFalse);
    });

    test('blank conditions are dropped, others trimmed', () {
      expect(const RuleDoc(playlistId: 'x', condition: '  ').toJson().containsKey('condition'), isFalse);
      expect(const RuleDoc(playlistId: 'x', condition: ' spotify_playing ').toJson()['condition'], 'spotify_playing');
    });

    test('sameAs detects edits', () {
      final doc = ScheduleDocument.fromJson(jsonDecode(_serverJson) as Map<String, dynamic>);
      final edited = doc.copyWith(rules: [doc.rules[0].copyWith(priority: 61), doc.rules[1]]);
      expect(edited.sameAs(doc), isFalse);
    });

    test('day mask uses Sunday as bit 0', () {
      const weekdays = RuleDoc(playlistId: 'x', daysMask: 62);
      expect(weekdays.appliesOn(0), isFalse);
      expect(weekdays.appliesOn(1), isTrue);
      expect(weekdays.appliesOn(5), isTrue);
      expect(weekdays.appliesOn(6), isFalse);
    });

    test('time helpers', () {
      expect(parseHm('07:05'), 425);
      expect(parseHm('7:05'), 425);
      expect(parseHm('24:00'), isNull);
      expect(parseHm('nope'), isNull);
      expect(formatHm(425), '07:05');
      expect(const RuleDoc(playlistId: 'x', startTime: '06:00', endTime: '18:00').wraps, isFalse);
      expect(const RuleDoc(playlistId: 'x', startTime: '22:00', endTime: '00:00').wraps, isTrue);
    });
  });

  group('status and overlays', () {
    test('ScheduleStatus parses nested rule and next change', () {
      final s = ScheduleStatus.fromJson({
        'activeRule': {'index': 1, 'playlistId': 'night', 'priority': 60, 'condition': null},
        'playlist': 'night',
        'entryIndex': 0,
        'entryCount': 1,
        'appId': 'fire',
        'nextChange': '2026-10-03T22:00:00+00:00',
        'nextChangeReason': 'rule',
      });
      expect(s.activeRule?.playlistId, 'night');
      expect(s.activeRule?.index, 1);
      expect(s.nextChange!.isAtSameMomentAs(DateTime.utc(2026, 10, 3, 22)), isTrue);
      expect(s.nextChangeReason, 'rule');
    });

    test('ScheduleStatus copes with nothing active', () {
      final s = ScheduleStatus.fromJson({'activeRule': null, 'nextChange': null});
      expect(s.activeRule, isNull);
      expect(s.nextChange, isNull);
    });

    test('OverlayInfo parses', () {
      final o = OverlayInfo.fromJson({'id': 'a', 'kind': 'toast', 'text': 'Hi', 'priority': 3, 'remainingSeconds': 2.5, 'exiting': false});
      expect(o.remainingSeconds, 2.5);
      expect(OverlayInfo.fromJson({'id': 'b', 'kind': 'badge', 'remainingSeconds': null}).remainingSeconds, isNull);
    });
  });

  group('HttpLedApi schedule and overlays', () {
    test('saveSchedule PUTs the document and exposes 400 errors as a list', () async {
      late http.Request seen;
      final api = HttpLedApi(
        baseUrl: 'http://x',
        client: MockClient((req) async {
          seen = req;
          return http.Response(jsonEncode({'errors': ['rules[0].playlistId is required', 'playlists[0].name is required']}), 400);
        }),
      );
      final result = await api.saveSchedule(const ScheduleDocument(rules: [RuleDoc(playlistId: '')]));
      expect(seen.method, 'PUT');
      expect(seen.url.path, '/api/schedule');
      expect(jsonDecode(seen.body)['rules'][0]['playlistId'], '');
      final error = result.errorOrNull!;
      expect(error.statusCode, 400);
      expect(error.errors, ['rules[0].playlistId is required', 'playlists[0].name is required']);
    });

    test('getSchedule returns null when the body is not a document', () async {
      final api = HttpLedApi(
        baseUrl: 'http://x',
        client: MockClient((_) async => http.Response('{"appId":"clock","brightness":null}', 200)),
      );
      final result = await api.getSchedule();
      expect(result, isA<Ok<ScheduleDocument?>>());
      expect(result.valueOrNull, isNull);
    });

    test('sendAlert uses the message endpoint with an {r,g,b} colour, or the plain flash', () async {
      final requests = <http.Request>[];
      final api = HttpLedApi(
        baseUrl: 'http://x',
        client: MockClient((req) async {
          requests.add(req);
          return http.Response('', 200);
        }),
      );
      await api.sendAlert(message: 'Hi', color: '#9600FF');
      await api.sendAlert();
      expect(requests[0].url.path, '/api/notifications/message');
      expect(jsonDecode(requests[0].body), {
        'message': 'Hi',
        'color': {'r': 150, 'g': 0, 'b': 255},
      });
      expect(requests[1].url.path, '/api/notifications');
    });

    test('dismiss and clear use DELETE', () async {
      final requests = <http.Request>[];
      final api = HttpLedApi(
        baseUrl: 'http://x',
        client: MockClient((req) async {
          requests.add(req);
          return http.Response('', 200);
        }),
      );
      await api.dismissOverlay('toast 1');
      await api.clearOverlays();
      expect(requests.map((r) => '${r.method} ${r.url.path}'), ['DELETE /api/overlays/toast%201', 'DELETE /api/overlays']);
    });
  });
}
