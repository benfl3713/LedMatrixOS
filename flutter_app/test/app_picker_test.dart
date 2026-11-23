import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/schedule_models.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/apps/app_picker.dart';
import 'package:led_matrix_controller/features/apps/apps_logic.dart';
import 'package:led_matrix_controller/features/schedule/playlist_editor.dart';
import 'package:led_matrix_controller/features/schedule/rule_editor.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

const _apps = [
  MatrixApp(id: 'clock', name: 'Clock', hasSettings: true, category: 'clocks', description: 'Digital clock'),
  MatrixApp(
    id: 'rail-departures',
    name: 'Rail Departures',
    hasSettings: true,
    category: 'travel',
    description: 'National Rail departures from a station',
    keywords: ['train', 'station', 'national rail'],
  ),
  MatrixApp(
      id: 'bus-arrivals',
      name: 'Bus Arrivals',
      hasSettings: true,
      category: 'travel',
      description: 'Live arrivals at your bus stop'),
  MatrixApp(id: 'fire', name: 'Fire', hasSettings: false, category: 'ambient', description: 'Flickering flames'),
];

Future<void> _pump(WidgetTester tester, Widget home, {Map<String, Object> prefs = const {}}) async {
  SharedPreferences.setMockInitialValues(prefs);
  tester.view.physicalSize = const Size(400, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final api = FakeApi()..apps = _apps;
  await tester.pumpWidget(ProviderScope(
    retry: (a, b) => null,
    overrides: [
      apiProvider.overrideWithValue(api),
      pollIntervalProvider.overrideWithValue(null),
    ],
    child: MaterialApp(theme: ThemeData.dark(useMaterial3: true), home: home),
  ));
  await tester.pumpAndSettle();
}

class _Opener extends StatelessWidget {
  const _Opener({required this.open});

  final Future<void> Function(BuildContext context) open;

  @override
  Widget build(BuildContext context) => Scaffold(
        body: Center(
            child: FilledButton(key: const Key('open'), onPressed: () => open(context), child: const Text('Open'))),
      );
}

void main() {
  test('keywords are parsed from the server and tolerate being missing', () {
    final a = MatrixApp.fromJson({
      'id': 'x',
      'name': 'X',
      'hasSettings': false,
      'keywords': ['train', ' ', 'rail']
    });
    expect(a.keywords, ['train', 'rail']);
    expect(MatrixApp.fromJson({'id': 'x', 'name': 'X', 'hasSettings': false}).keywords, isEmpty);
  });

  test('search finds Rail Departures by the keyword "train", ranked after name and description', () {
    final hits = searchApps(_apps, 'train');
    expect(hits.map((a) => a.id), ['rail-departures']);
    // A description match outranks a keyword match.
    final ranked = searchApps([
      const MatrixApp(id: 'k', name: 'Kw', hasSettings: false, keywords: ['flame']),
      const MatrixApp(id: 'd', name: 'Dd', hasSettings: false, description: 'flames'),
    ], 'flame');
    expect(ranked.map((a) => a.id), ['d', 'k']);
  });

  group('app picker', () {
    testWidgets('single mode: search, category filter, tap returns the app', (tester) async {
      List<String>? result;
      await _pump(tester, _Opener(open: (c) async => result = await showAppPicker(c, selected: 'fire')));
      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('pick-clock')), findsOneWidget);
      expect(find.byKey(const Key('pick-fire')), findsOneWidget);

      // Search by a synonym.
      await tester.enterText(find.byKey(const Key('picker-search')), 'train');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('pick-rail-departures')), findsOneWidget);
      expect(find.byKey(const Key('pick-clock')), findsNothing);

      await tester.enterText(find.byKey(const Key('picker-search')), 'zzzz');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('picker-no-results')), findsOneWidget);

      await tester.enterText(find.byKey(const Key('picker-search')), '');
      await tester.pumpAndSettle();

      // Category chip narrows the list.
      await tester.tap(find.byKey(const Key('picker-filter-travel')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('pick-bus-arrivals')), findsOneWidget);
      expect(find.byKey(const Key('pick-clock')), findsNothing);

      await tester.tap(find.byKey(const Key('pick-rail-departures')));
      await tester.pumpAndSettle();
      expect(result, ['rail-departures']);
      expect(find.byKey(const Key('picker-search')), findsNothing);
    });

    testWidgets('multiple mode: checkboxes and an Add N apps button', (tester) async {
      List<String>? result;
      await _pump(tester, _Opener(open: (c) async => result = await showAppPicker(c, multiple: true)));
      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();

      expect(tester.widget<FilledButton>(find.byKey(const Key('picker-add'))).onPressed, isNull);
      await tester.tap(find.byKey(const Key('pick-fire')));
      await tester.tap(find.byKey(const Key('pick-clock')));
      await tester.pumpAndSettle();
      expect(find.text('Add 2 apps'), findsOneWidget);
      // Untick, then tick again: still two in the order they were last ticked.
      await tester.tap(find.byKey(const Key('pick-fire')));
      await tester.pumpAndSettle();
      expect(find.text('Add 1 app'), findsOneWidget);
      await tester.tap(find.byKey(const Key('pick-fire')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('picker-add')));
      await tester.pumpAndSettle();
      expect(result, ['clock', 'fire']);
    });

    testWidgets('favourites and recents lead the list, and hide while searching', (tester) async {
      await _pump(
        tester,
        _Opener(open: (c) => showAppPicker(c)),
        prefs: {
          'app_favourites': ['fire'],
          'app_recents': ['clock']
        },
      );
      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('pick-fav-fire')), findsOneWidget);
      expect(find.byKey(const Key('pick-recent-clock')), findsOneWidget);

      await tester.enterText(find.byKey(const Key('picker-search')), 'fire');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('pick-fav-fire')), findsNothing);
      expect(find.byKey(const Key('pick-fire')), findsOneWidget);
    });

    testWidgets('the field shows the chosen app and opens the picker', (tester) async {
      var value = 'clock';
      await _pump(
        tester,
        StatefulBuilder(
          builder: (context, setState) => Scaffold(
            body: AppPickerField(key: const Key('field'), appId: value, onChanged: (v) => setState(() => value = v)),
          ),
        ),
      );
      expect(find.text('Clock'), findsOneWidget);
      await tester.tap(find.byKey(const Key('field')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('pick-fire')));
      await tester.pumpAndSettle();
      expect(value, 'fire');
      expect(find.text('Fire'), findsOneWidget);
    });
  });

  group('rule editor', () {
    Future<void> openRule(
        WidgetTester tester, RuleDoc rule, List<PlaylistDoc> playlists, void Function(RuleEditResult?) got,
        {bool isNew = true}) async {
      await _pump(
        tester,
        _Opener(open: (c) async => got(await showRuleEditor(c, rule: rule, isNew: isNew, playlists: playlists))),
      );
      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();
    }

    testWidgets('One app | Playlist switches the control and the single app is applied', (tester) async {
      RuleEditResult? result;
      await openRule(tester, const RuleDoc(playlistId: 'p'), [const PlaylistDoc(name: 'p')], (r) => result = r);
      expect(find.byKey(const Key('rule-playlist')), findsOneWidget);
      expect(find.byKey(const Key('rule-single-app')), findsNothing);

      await tester.tap(find.text('One app'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('rule-single-app')), findsOneWidget);
      expect(find.text('Choose an app'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.byKey(const Key('rule-apply'))).onPressed, isNull);

      await tester.tap(find.byKey(const Key('rule-single-app')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('pick-fire')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('rule-apply')));
      await tester.pumpAndSettle();
      expect(result!.singleAppId, 'fire');
      expect(result!.rule!.playlistId, 'fire');
    });

    testWidgets('with no playlists, Create a playlist opens the editor and selects the new playlist', (tester) async {
      RuleEditResult? result;
      await openRule(tester, const RuleDoc(playlistId: ''), const [], (r) => result = r);
      // No playlists: starts on One app.
      expect(find.byKey(const Key('rule-single-app')), findsOneWidget);
      await tester.tap(find.text('Playlist'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('rule-playlist')), findsNothing);
      expect(find.byKey(const Key('rule-create-playlist')), findsOneWidget);

      await tester.tap(find.byKey(const Key('rule-create-playlist')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('playlist-name')), 'Evening');
      await tester.tap(find.byKey(const Key('add-entry')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('pick-fire')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('picker-add')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('playlist-apply')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('rule-playlist')), findsOneWidget);
      expect(find.text('Evening'), findsOneWidget);
      await tester.tap(find.byKey(const Key('rule-apply')));
      await tester.pumpAndSettle();
      expect(result!.rule!.playlistId, 'Evening');
      expect(result!.createdPlaylist!.entries.single.appId, 'fire');
    });

    testWidgets('priority is a Low / Normal / High / Top choice and keeps an unusual value as Custom', (tester) async {
      RuleEditResult? result;
      await openRule(
          tester, const RuleDoc(playlistId: 'p', priority: 37), [const PlaylistDoc(name: 'p')], (r) => result = r);
      expect(find.byType(Slider).evaluate().where((e) => (e.widget as Slider).label == '37'), isEmpty);
      expect(find.text('Custom (37)'), findsOneWidget);
      expect(tester.widget<ChoiceChip>(find.byKey(const Key('rule-priority-custom'))).selected, isTrue);

      // Applying without touching priority keeps it.
      await tester.ensureVisible(find.byKey(const Key('rule-apply')));
      await tester.tap(find.byKey(const Key('rule-apply')));
      await tester.pumpAndSettle();
      expect(result!.rule!.priority, 37);

      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('High'));
      await tester.tap(find.text('High'));
      await tester.pumpAndSettle();
      expect(tester.widget<ChoiceChip>(find.byKey(const Key('rule-priority-custom'))).selected, isFalse);
      await tester.ensureVisible(find.byKey(const Key('rule-apply')));
      await tester.tap(find.byKey(const Key('rule-apply')));
      await tester.pumpAndSettle();
      expect(result!.rule!.priority, 75);
    });

    testWidgets('fits a 360px phone with a custom priority', (tester) async {
      await openRule(tester, const RuleDoc(playlistId: 'p', priority: 37), [const PlaylistDoc(name: 'p')], (_) {});
      tester.view.physicalSize = const Size(360, 800);
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.text('Playlist'), findsWidgets);
    });

    testWidgets('the apply hint is neutral until the user touches What to show', (tester) async {
      await openRule(tester, const RuleDoc(playlistId: ''), [const PlaylistDoc(name: 'p')], (_) {});
      final scheme = Theme.of(tester.element(find.byKey(const Key('editor-problem')))).colorScheme;
      expect(tester.widget<Text>(find.byKey(const Key('editor-problem'))).style!.color, scheme.onSurfaceVariant);
      await tester.tap(find.text('One app'));
      await tester.pumpAndSettle();
      expect(tester.widget<Text>(find.byKey(const Key('editor-problem'))).style!.color, scheme.error);
    });
  });

  group('playlist editor validation', () {
    testWidgets('no field error until the name is edited; the hint is neutral', (tester) async {
      await _pump(
        tester,
        _Opener(open: (c) => showPlaylistEditor(c, playlist: const PlaylistDoc(name: ''), isNew: true)),
      );
      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();
      expect(find.text('Give the playlist a name.'), findsOneWidget); // only the bottom hint
      final scheme = Theme.of(tester.element(find.byKey(const Key('editor-problem')))).colorScheme;
      expect(tester.widget<Text>(find.byKey(const Key('editor-problem'))).style!.color, scheme.onSurfaceVariant);

      // Toggling another field does not turn the name red.
      await tester.tap(find.byType(SwitchListTile));
      await tester.pumpAndSettle();
      expect(find.text('Give the playlist a name.'), findsOneWidget);

      // Editing the name (and clearing it) shows the error under the field.
      await tester.enterText(find.byKey(const Key('playlist-name')), 'a');
      await tester.enterText(find.byKey(const Key('playlist-name')), '');
      await tester.pumpAndSettle();
      expect(find.text('Give the playlist a name.'), findsNWidgets(2));
      expect(tester.widget<Text>(find.byKey(const Key('editor-problem'))).style!.color, scheme.error);
    });
  });
}
