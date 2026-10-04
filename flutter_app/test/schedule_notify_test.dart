import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

Future<void> _open(WidgetTester tester, FakeApi api, String tab) async {
  SharedPreferences.setMockInitialValues({});
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
  await tester.tap(find.text(tab).last);
  await tester.pumpAndSettle();
}

void main() {
  group('Schedule screen', () {
    testWidgets('shows the active-now card, week bars, rules and playlists', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Schedule');

      expect(find.byKey(const Key('status-card')), findsOneWidget);
      expect(find.text('Active now'), findsOneWidget);
      expect(find.textContaining('Rule 1, priority 10'), findsOneWidget);
      expect(find.textContaining('entry 2 of 2'), findsOneWidget);
      expect(find.textContaining('Next change'), findsOneWidget);

      // Rule 0 runs every day 07:00-22:00; rule 1 is weekdays 22:00-07:00 so it wraps and has tails.
      expect(find.byKey(const Key('rule-bar-0-0')), findsOneWidget);
      expect(find.byKey(const Key('rule-bar-1-1')), findsOneWidget);
      expect(find.byKey(const Key('rule-bar-1-2-tail')), findsOneWidget);
      expect(find.byKey(const Key('rule-bar-1-0')), findsNothing); // not on Sundays
      expect(find.byKey(const Key('rule-tile-1')), findsOneWidget);
      expect(find.textContaining('Brightness 20%'), findsOneWidget);
      expect(find.byKey(const Key('playlist-tile-0')), findsOneWidget);
      expect(find.byKey(const Key('unsaved-banner')), findsNothing);
      expect(find.byKey(const Key('save-schedule')), findsNothing);
    });

    testWidgets('editing a rule marks the schedule unsaved, discard reverts, save sends the whole document', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Schedule');

      await tester.tap(find.byKey(const Key('rule-tile-0')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('day-chip-0'))); // Sunday off
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byKey(const Key('rule-apply')));
      await tester.tap(find.byKey(const Key('rule-apply')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('unsaved-banner')), findsOneWidget);
      expect(find.byKey(const Key('rule-bar-0-0')), findsNothing);

      await tester.tap(find.byKey(const Key('discard-schedule')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('unsaved-banner')), findsNothing);
      expect(find.byKey(const Key('rule-bar-0-0')), findsOneWidget);

      // Edit again and save.
      await tester.tap(find.byKey(const Key('rule-tile-0')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('day-chip-0')));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byKey(const Key('rule-apply')));
      await tester.tap(find.byKey(const Key('rule-apply')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-schedule')));
      await tester.pumpAndSettle();

      expect(api.savedDocs, hasLength(1));
      final saved = api.savedDocs.single;
      expect(saved.rules[0].daysMask, 127 & ~1);
      expect(saved.rules, hasLength(2));
      expect(saved.playlists, hasLength(2));
      expect(find.byKey(const Key('unsaved-banner')), findsNothing);
    });

    testWidgets('server validation errors are listed inline and the draft is kept', (tester) async {
      final api = FakeApi()..saveErrors = ['rules[0].playlistId is required', 'playlists[1].entries must contain at least one entry'];
      await _open(tester, api, 'Schedule');

      await tester.tap(find.byKey(const Key('delete-rule-0')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-schedule')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('validation-errors')), findsOneWidget);
      expect(find.textContaining('rules[0].playlistId is required'), findsOneWidget);
      expect(find.textContaining('entries must contain at least one entry'), findsOneWidget);
      expect(find.byKey(const Key('unsaved-banner')), findsOneWidget);
      expect(api.savedDocs, isEmpty);

      // A retry succeeds and clears the list.
      await tester.tap(find.byKey(const Key('save-schedule')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('validation-errors')), findsNothing);
      expect(api.savedDocs.single.rules, hasLength(1));
    });

    testWidgets('playlist editor adds an entry and reordering changes the order', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Schedule');

      await tester.ensureVisible(find.byKey(const Key('playlist-tile-0')));
      await tester.tap(find.byKey(const Key('playlist-tile-0')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('entry-app-0')), findsOneWidget);
      expect(find.byKey(const Key('entry-app-1')), findsOneWidget);

      await tester.ensureVisible(find.byKey(const Key('add-entry')));
      await tester.tap(find.byKey(const Key('add-entry')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('entry-app-2')), findsOneWidget);

      // Drag the last entry's handle to the top.
      final handles = find.byIcon(Icons.drag_handle_rounded);
      expect(handles, findsNWidgets(3));
      final gesture = await tester.startGesture(tester.getCenter(handles.at(2)));
      await tester.pump(const Duration(milliseconds: 100));
      await gesture.moveBy(const Offset(0, -400));
      await tester.pump(const Duration(milliseconds: 100));
      await gesture.up();
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('playlist-apply')));
      await tester.tap(find.byKey(const Key('playlist-apply')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-schedule')));
      await tester.pumpAndSettle();

      final entries = api.savedDocs.single.playlists[0].entries;
      expect(entries, hasLength(3));
      // The new entry (first app, clock) moved to the front; the originals follow in order.
      expect(entries.map((e) => e.appId).toList(), ['clock', 'clock', 'weather']);
      expect(entries[0].durationMs, 30000); // the new default entry
      expect(entries[1].durationMs, 20000);
      expect(entries[2].transition, 'slide');
    });
  });

  group('Notify screen', () {
    testWidgets('sends a toast with its colours and duration', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Notify');

      expect(find.text('Toast'), findsOneWidget);
      await tester.enterText(find.byKey(const Key('notify-text')), 'Hello');
      await tester.pump();
      await tester.tap(find.byKey(const Key('send-button')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('toast:Hello:4:#000000:#FFFFFF'));
    });

    testWidgets('badge and alert modes send their own fields', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Notify');

      await tester.tap(find.text('Badge'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('badge-x')), '10');
      await tester.tap(find.byKey(const Key('send-button')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('badge:badge:10:null:4:#FF3C3C:true'));

      await tester.tap(find.text('Alert'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('notify-text')), 'Door open');
      await tester.pump();
      await tester.tap(find.byKey(const Key('send-button')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('alert:Door open:#9600FF'));
    });

    testWidgets('a preset can be saved, filled back in and deleted with a long press', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Notify');

      await tester.enterText(find.byKey(const Key('notify-text')), 'Bins out');
      await tester.pump();
      await tester.tap(find.byKey(const Key('save-preset')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('preset-name')), 'Bins');
      await tester.tap(find.byKey(const Key('preset-save-confirm')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('preset-Bins')), findsOneWidget);

      // Change the composer, then tap the preset to fill it back in.
      await tester.tap(find.text('Alert'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('notify-text')), 'Something else');
      await tester.pump();
      await tester.tap(find.text('Bins'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(TextField, 'Bins out'), findsOneWidget);
      await tester.tap(find.byKey(const Key('send-button')));
      await tester.pumpAndSettle();
      expect(api.calls.last, startsWith('toast:Bins out:'));

      await tester.longPress(find.byKey(const Key('preset-Bins')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('preset-Bins')), findsNothing);
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('notify_presets'), '[]');
    });

    testWidgets('active overlays can be dismissed and cleared behind a confirm', (tester) async {
      final api = FakeApi();
      await _open(tester, api, 'Notify');

      expect(find.text('Dinner is ready'), findsOneWidget);
      expect(find.textContaining('toast, priority 10, 4s left'), findsOneWidget);
      expect(find.byKey(const Key('overlay-badge-1')), findsOneWidget);

      await tester.tap(find.byKey(const Key('dismiss-toast-1')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('dismiss:toast-1'));
      expect(find.text('Dinner is ready'), findsNothing);

      await tester.tap(find.byKey(const Key('clear-all')));
      await tester.pumpAndSettle();
      expect(api.calls, isNot(contains('clearOverlays'))); // not yet: confirm first
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();
      expect(api.calls, isNot(contains('clearOverlays')));

      await tester.tap(find.byKey(const Key('clear-all')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('clear-confirm')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('clearOverlays'));
      expect(find.text('Nothing is showing.'), findsOneWidget);
    });
  });
}
