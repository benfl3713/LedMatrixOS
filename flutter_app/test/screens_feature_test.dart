import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/api/screen_models.dart';
import 'package:led_matrix_controller/features/screens/screens_provider.dart';

import 'fake_api.dart';

Future<void> _openScreens(WidgetTester tester, FakeApi api) async {
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps'));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('screens-entry')));
  await tester.pumpAndSettle();
}

void main() {
  group('model', () {
    test('JSON round trip keeps unknown props and every slot', () {
      final json = {
        'id': 'x',
        'name': 'X',
        'root': {
          'type': 'dock',
          'padding': 2,
          'weird': {'a': 1},
          'top': {'type': 'label', 'text': {'bind': 'time'}, 'extra': true},
          'fill': {
            'type': 'stack',
            'children': [
              {'type': 'clock', 'format': 'HH:mm'},
              {'type': 'list', 'source': 'a|b', 'item': {'type': 'label', 'text': '{item}'}},
            ],
          },
        },
      };
      final def = ScreenDefinition.fromJson(json);
      expect(def.root.props['weird'], {'a': 1});
      expect(def.root.slots.keys, ['top', 'fill']);
      expect(def.root.slots['fill']!.children, hasLength(2));
      expect(jsonDecode(def.encode()), json);
      // Clones are independent.
      final copy = def.clone()..root.props['padding'] = 9;
      expect(def.root.props['padding'], 2);
      expect(copy.root.props['padding'], 9);
    });

    test('schema parses node types, kinds, options, slots and binding keys', () {
      final schema = ScreenSchema.fromJson({
        'maxDepth': 8,
        'maxNodes': 200,
        'fonts': ['Big'],
        'slots': ['children'],
        'commonProps': [
          {'name': 'width', 'kind': 'int', 'options': null},
        ],
        'nodeTypes': [
          {
            'type': 'label',
            'props': [
              {'name': 'text', 'kind': 'binding', 'options': null},
              {'name': 'font', 'kind': 'enum', 'options': ['Big']},
            ],
            'slots': [],
          },
        ],
        'bindings': {
          'keys': [
            {'key': 'weather.<field>', 'kind': 'weather', 'description': 'w', 'fields': ['temp', 'low'], 'insideListOnly': false},
          ],
        },
      });
      expect(schema.typeOf('label')!.props[1].kind, PropKind.enumeration);
      expect(schema.typeOf('label')!.props[1].options, ['Big']);
      expect(schema.commonProps.single.kind, PropKind.int);
      expect(schema.bindingKeys.single.concreteKeys, ['weather.temp', 'weather.low']);
    });

    test('errors are placed on the deepest node they point into', () {
      final def = ScreenDefinition.fromJson({
        'id': 'x',
        'name': 'X',
        'root': {
          'type': 'stack',
          'children': [
            {'type': 'label'},
            {'type': 'label'},
          ],
        },
      });
      final placed = placeErrors(def, const [
        FieldError('root.children[1].color', 'must be a hex color'),
        FieldError('name', 'name is required'),
        FieldError('', 'boom'),
      ]);
      final second = def.root.children[1];
      expect(placed.byNode[second]!.single.prop, 'color');
      expect(placed.name, ['name is required']);
      expect(placed.general, ['boom']);
    });

    test('slugify and unique ids', () {
      expect(slugify('Living Room!'), 'living-room');
      expect(uniqueScreenId('Demo', ['demo', 'demo-2']), 'demo-3');
    });
  });

  group('screens UI', () {
    testWidgets('Apps screen links to the list of user screens', (tester) async {
      await _openScreens(tester, FakeApi());
      expect(find.text('Demo'), findsOneWidget);
      expect(find.byKey(const Key('screen-tile-demo')), findsOneWidget);
      expect(find.byKey(const Key('new-screen')), findsOneWidget);
    });

    testWidgets('activating from the list uses the screen: alias', (tester) async {
      final api = FakeApi();
      await _openScreens(tester, api);
      await tester.tap(find.byKey(const Key('activate-screen-demo')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('activate:screen:demo'));
    });

    testWidgets('creating from a template opens the editor and Save PUTs it', (tester) async {
      final api = FakeApi();
      await _openScreens(tester, api);

      await tester.tap(find.byKey(const Key('new-screen')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('new-screen-name')), 'Hall Clock');
      await tester.tap(find.byKey(const Key('template-clock')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('screen-name')), findsOneWidget);
      expect(find.text('id: hall-clock'), findsOneWidget);
      expect(find.byKey(const Key('node-root')), findsOneWidget);
      expect(find.byKey(const Key('node-root.children[0]')), findsOneWidget);

      await tester.tap(find.byKey(const Key('save-screen')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('putScreen:hall-clock'));
      final saved = api.putScreens.single;
      expect(saved.name, 'Hall Clock');
      expect(saved.root.type, 'stack');
      expect(saved.root.children.map((c) => c.type), ['clock', 'label']);
    });

    testWidgets('editing a property and saving sends it, keeping unknown props', (tester) async {
      final api = FakeApi();
      await _openScreens(tester, api);
      await tester.tap(find.byKey(const Key('screen-tile-demo')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('node-root.children[0]')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('prop-text')), 'Now {weather.high}');
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-screen')));
      await tester.pumpAndSettle();

      final saved = api.putScreens.single;
      expect(saved.root.children.single.props['text'], 'Now {weather.high}');
      expect(saved.root.children.single.props['mystery'], 7);
      expect(saved.root.children.single.props['color'], '#FFFFFF');
    });

    testWidgets('adding a node from the type picker and picking a binding key', (tester) async {
      final api = FakeApi();
      await _openScreens(tester, api);
      await tester.tap(find.byKey(const Key('screen-tile-demo')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('add-root')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('type-label')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('node-root.children[1]')), findsOneWidget);

      await tester.tap(find.byKey(const Key('bind-text')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('binding-weather.temp')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('binding-use-template')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-screen')));
      await tester.pumpAndSettle();

      final saved = api.putScreens.single;
      expect(saved.root.children, hasLength(2));
      expect(saved.root.children[1].props['text'], '{weather.temp}');
    });

    testWidgets('validation errors show next to the offending node and the draft is kept', (tester) async {
      final api = FakeApi()
        ..screenErrors = [
          const FieldError('root.children[0].color', 'must be a hex color (#RGB, #RRGGBB or #RRGGBBAA)'),
        ];
      await _openScreens(tester, api);
      await tester.tap(find.byKey(const Key('screen-tile-demo')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('node-root')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('prop-gap')), '4');
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-screen')));
      await tester.pumpAndSettle();

      // The failing node opens (root was open, so it is listed under its row) and names the property.
      expect(api.putScreens, isEmpty);
      expect(find.textContaining('must be a hex color'), findsOneWidget);
      expect(find.byKey(const Key('problems-root.children[0]')), findsOneWidget);
      // The edit is still there; a retry succeeds and clears the message.
      await tester.tap(find.byKey(const Key('save-screen')));
      await tester.pumpAndSettle();
      expect(find.textContaining('must be a hex color'), findsNothing);
      expect(api.putScreens.single.root.props['gap'], 4);
    });

    testWidgets('delete asks for confirmation and removes the screen', (tester) async {
      final api = FakeApi();
      await _openScreens(tester, api);
      await tester.tap(find.byKey(const Key('screen-tile-demo')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('delete-screen')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('confirm-delete-screen')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('deleteScreen:demo'));
      expect(find.byKey(const Key('screen-tile-demo')), findsNothing);
    });

    testWidgets('Activate saves pending edits first, then activates the screen', (tester) async {
      final api = FakeApi();
      await _openScreens(tester, api);
      await tester.tap(find.byKey(const Key('screen-tile-demo')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('screen-name')), 'Renamed');
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('activate-screen')));
      await tester.pumpAndSettle();

      expect(api.calls, containsAllInOrder(['putScreen:demo', 'activate:screen:demo']));
      expect(api.putScreens.single.name, 'Renamed');
    });
  });
}
