import 'dart:collection';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/result.dart';
import '../../api/screen_models.dart';
import '../../core/providers.dart';

// ---------------------------------------------------------------------------
// Schema and list
// ---------------------------------------------------------------------------

final screenSchemaProvider =
    FutureProvider<ScreenSchema>((ref) async => (await ref.watch(apiProvider).getScreenSchema()).getOrThrow());

class ScreenListNotifier extends AsyncNotifier<List<ScreenSummary>> {
  @override
  Future<List<ScreenSummary>> build() async => (await ref.watch(apiProvider).listScreens()).getOrThrow();

  Future<void> refresh() async {
    final result = await ref.read(apiProvider).listScreens();
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  Future<bool> activate(String id) async {
    final ok = await ref.read(appListProvider.notifier).activate('screen:$id');
    return ok;
  }
}

final screenListProvider = AsyncNotifierProvider<ScreenListNotifier, List<ScreenSummary>>(ScreenListNotifier.new);

/// Unsaved screens created from a template, keyed by id, waiting for the editor to pick them up.
class NewScreenDrafts extends Notifier<Map<String, ScreenDefinition>> {
  @override
  Map<String, ScreenDefinition> build() => {};

  void put(ScreenDefinition d) => state = {...state, d.id: d};

  void remove(String id) => state = {...state}..remove(id);
}

final newScreenDraftsProvider = NotifierProvider<NewScreenDrafts, Map<String, ScreenDefinition>>(NewScreenDrafts.new);

/// "Living Room!" -> "living-room".
String slugify(String name) {
  final s = name.toLowerCase().replaceAll(RegExp(r'[^a-z0-9_-]+'), '-').replaceAll(RegExp(r'^-+|-+$'), '');
  return s.isEmpty ? 'screen' : s;
}

String uniqueScreenId(String name, Iterable<String> taken) {
  final base = slugify(name);
  final used = taken.toSet();
  if (!used.contains(base)) return base;
  var i = 2;
  while (used.contains('$base-$i')) {
    i++;
  }
  return '$base-$i';
}

// ---------------------------------------------------------------------------
// Error placement
// ---------------------------------------------------------------------------

/// JSON-path-like location of every node, e.g. `root`, `root.children[1]`, `root.fill`.
Map<ScreenNode, String> nodePaths(ScreenNode root) {
  final out = LinkedHashMap<ScreenNode, String>.identity();
  void walk(ScreenNode n, String path) {
    out[n] = path;
    for (final e in n.childEntries) {
      walk(e.node, e.index == null ? '$path.${e.slot}' : '$path.${e.slot}[${e.index}]');
    }
  }

  walk(root, 'root');
  return out;
}

/// Errors that belong to one node: the property (or '' for the node itself) and the message.
class NodeProblem {
  const NodeProblem(this.prop, this.message);

  final String prop;
  final String message;

  @override
  String toString() => prop.isEmpty ? message : '$prop: $message';
}

class ErrorPlacement {
  const ErrorPlacement({this.byNode = const {}, this.name = const [], this.general = const []});

  final Map<ScreenNode, List<NodeProblem>> byNode;
  final List<String> name;
  final List<String> general;

  bool get isEmpty => byNode.isEmpty && name.isEmpty && general.isEmpty;
}

/// Matches each error's `path` to the deepest node it points into.
ErrorPlacement placeErrors(ScreenDefinition draft, List<FieldError> errors) {
  final paths = nodePaths(draft.root);
  final byNode = LinkedHashMap<ScreenNode, List<NodeProblem>>.identity();
  final name = <String>[];
  final general = <String>[];
  for (final e in errors) {
    if (e.path == 'name') {
      name.add(e.message);
      continue;
    }
    ScreenNode? best;
    var bestLen = -1;
    paths.forEach((node, p) {
      final hit = e.path == p || e.path.startsWith('$p.');
      if (hit && p.length > bestLen) {
        best = node;
        bestLen = p.length;
      }
    });
    if (best == null) {
      general.add(e.toString());
    } else {
      final rest = e.path.length == bestLen ? '' : e.path.substring(bestLen + 1);
      byNode.putIfAbsent(best!, () => []).add(NodeProblem(rest, e.message));
    }
  }
  return ErrorPlacement(byNode: byNode, name: name, general: general);
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------

class ScreenEditState {
  const ScreenEditState({
    required this.savedJson,
    required this.draft,
    required this.isNew,
    this.errors = const [],
    this.saving = false,
    this.version = 0,
  });

  /// Encoded form of what the device has (or of the template, for a new screen).
  final String savedJson;

  /// The tree being edited. It is mutated in place; [version] changes with every edit so listeners rebuild.
  final ScreenDefinition draft;
  final bool isNew;

  /// Validation problems from the last failed save.
  final List<FieldError> errors;
  final bool saving;
  final int version;

  bool get dirty => isNew || draft.encode() != savedJson;

  ErrorPlacement get placed => placeErrors(draft, errors);

  ScreenEditState copyWith({String? savedJson, bool? isNew, List<FieldError>? errors, bool? saving, bool bump = false}) =>
      ScreenEditState(
        savedJson: savedJson ?? this.savedJson,
        draft: draft,
        isNew: isNew ?? this.isNew,
        errors: errors ?? this.errors,
        saving: saving ?? this.saving,
        version: bump ? version + 1 : version,
      );
}

class ScreenEditor extends AsyncNotifier<ScreenEditState> {
  ScreenEditor(this.id);

  final String id;

  @override
  Future<ScreenEditState> build() async {
    final pending = ref.read(newScreenDraftsProvider)[id];
    if (pending != null) return ScreenEditState(savedJson: '', draft: pending, isNew: true);
    final def = (await ref.watch(apiProvider).getScreen(id)).getOrThrow();
    return ScreenEditState(savedJson: def.encode(), draft: def, isNew: false);
  }

  ScreenEditState? get _current => state.value;

  void reload() => ref.invalidateSelf();

  void _touch() {
    final s = _current;
    if (s == null) return;
    state = AsyncData(s.copyWith(bump: true));
  }

  void setName(String name) {
    final s = _current;
    if (s == null) return;
    s.draft.name = name;
    _touch();
  }

  /// Sets a property; null (or an empty string) removes it.
  void setProp(ScreenNode node, String prop, Object? value) {
    if (value == null || value == '') {
      node.props.remove(prop);
    } else {
      node.props[prop] = value;
    }
    _touch();
  }

  /// Adds a new node of [type] to [slot] of [parent] and returns it.
  ScreenNode? addNode(ScreenNode parent, String slot, String type) {
    final node = ScreenNode(type: type);
    if (type == 'list') {
      node.slots['item'] = ScreenNode(type: 'label', props: {'text': '{item}'});
    }
    if (slot == childrenSlot) {
      parent.children.add(node);
    } else {
      parent.slots[slot] = node;
    }
    _touch();
    return node;
  }

  void moveChild(ScreenNode parent, int index, int delta) {
    final to = index + delta;
    if (index < 0 || index >= parent.children.length || to < 0 || to >= parent.children.length) return;
    final n = parent.children.removeAt(index);
    parent.children.insert(to, n);
    _touch();
  }

  void removeNode(ScreenNode parent, String slot, int? index) {
    if (slot == childrenSlot && index != null) {
      if (index < parent.children.length) parent.children.removeAt(index);
    } else {
      parent.slots.remove(slot);
    }
    _touch();
  }

  void discard() => ref.invalidateSelf();

  /// PUTs the screen. Returns true on success; validation problems stay in the state.
  Future<bool> save() async {
    final s = _current;
    if (s == null || s.saving) return false;
    state = AsyncData(s.copyWith(saving: true, errors: const []));
    final result = await ref.read(apiProvider).putScreen(s.draft);
    if (!ref.mounted) return false;
    final error = result.errorOrNull;
    if (error != null) {
      final fields = error.fieldErrors.isNotEmpty
          ? error.fieldErrors
          : (error.errors.isNotEmpty ? [for (final e in error.errors) FieldError('', e)] : <FieldError>[]);
      state = AsyncData(s.copyWith(saving: false, errors: fields));
      ref.read(errorBusProvider.notifier).report(
          fields.isNotEmpty ? 'Screen not saved: ${fields.length} problem(s)' : error);
      return false;
    }
    ref.read(newScreenDraftsProvider.notifier).remove(id);
    state = AsyncData(s.copyWith(savedJson: s.draft.encode(), isNew: false, saving: false, errors: const []));
    ref.invalidate(screenListProvider);
    ref.invalidate(appListProvider);
    return true;
  }

  Future<bool> delete() async {
    final s = _current;
    if (s == null) return false;
    if (s.isNew) {
      ref.read(newScreenDraftsProvider.notifier).remove(id);
      return true;
    }
    final result = await ref.read(apiProvider).deleteScreen(id);
    if (!ref.mounted) return false;
    final error = result.errorOrNull;
    if (error != null) {
      ref.read(errorBusProvider.notifier).report(error);
      return false;
    }
    ref.invalidate(screenListProvider);
    ref.invalidate(appListProvider);
    return true;
  }

  /// Saves pending edits, then makes the screen the active app.
  Future<bool> activate() async {
    final s = _current;
    if (s == null) return false;
    if (s.dirty && !await save()) return false;
    return ref.read(appListProvider.notifier).activate('screen:$id');
  }
}

final screenEditorProvider = AsyncNotifierProvider.autoDispose.family<ScreenEditor, ScreenEditState, String>(ScreenEditor.new);
