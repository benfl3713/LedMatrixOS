import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/schedule_models.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'editor_scaffold.dart';

class PlaylistEditResult {
  const PlaylistEditResult.apply(this.playlist) : delete = false;
  const PlaylistEditResult.delete()
      : playlist = null,
        delete = true;

  final PlaylistDoc? playlist;
  final bool delete;
}

Future<PlaylistEditResult?> showPlaylistEditor(
  BuildContext context, {
  required PlaylistDoc playlist,
  required bool isNew,
  List<String> otherNames = const [],
}) =>
    pushEditor<PlaylistEditResult>(context, PlaylistEditor(initial: playlist, isNew: isNew, otherNames: otherNames));

/// One row being edited. A stable [key] lets the reorderable list keep each row's text field state.
class _Row {
  _Row(this.entry) : key = UniqueKey();

  EntryDoc entry;
  final Key key;
}

class PlaylistEditor extends ConsumerStatefulWidget {
  const PlaylistEditor({super.key, required this.initial, required this.isNew, this.otherNames = const []});

  final PlaylistDoc initial;
  final bool isNew;

  /// Names of the other playlists, to reject duplicates.
  final List<String> otherNames;

  @override
  ConsumerState<PlaylistEditor> createState() => _PlaylistEditorState();
}

class _PlaylistEditorState extends ConsumerState<PlaylistEditor> {
  late final TextEditingController _name = TextEditingController(text: widget.initial.name);
  late bool _skip = widget.initial.skipUnavailable;
  late final List<_Row> _rows = [for (final e in widget.initial.entries) _Row(e)];

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  bool _touched = false;

  void _change(VoidCallback fn) => setState(() {
        _touched = true;
        fn();
      });

  void _add() {
    final apps = ref.read(appListProvider).value?.apps ?? const <MatrixApp>[];
    _change(() => _rows.add(_Row(EntryDoc(appId: apps.isEmpty ? '' : apps.first.id))));
  }

  void _apply() {
    Navigator.pop(
      context,
      PlaylistEditResult.apply(PlaylistDoc(
        name: _name.text.trim(),
        skipUnavailable: _skip,
        entries: [for (final r in _rows) r.entry],
      )),
    );
  }

  Future<void> _delete() async {
    final ok = await confirmDestructive(
      context,
      title: 'Delete this playlist?',
      message: 'Rules that use it will have nothing to show until you pick another playlist. You can discard the change before saving.',
      confirmKey: const Key('playlist-delete-confirm'),
    );
    if (ok && mounted) Navigator.pop(context, const PlaylistEditResult.delete());
  }

  String? get _nameError {
    final n = _name.text.trim();
    if (n.isEmpty) return 'Give the playlist a name.';
    if (widget.otherNames.contains(n)) return 'Another playlist already uses this name.';
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    final scheme = Theme.of(context).colorScheme;
    final apps = ref.watch(appListProvider).value?.apps ?? const <MatrixApp>[];
    final transitions = ref.watch(transitionsProvider).value?.transitions ?? const <String>[];
    final nameError = _nameError;
    final problem = nameError ?? (_rows.isEmpty ? 'A playlist needs at least one entry.' : null);
    final totalSeconds = _rows.fold<int>(0, (a, r) => a + r.entry.durationMs) ~/ 1000;

    return EditorScaffold(
      title: widget.isNew ? 'New playlist' : 'Edit playlist',
      dirty: _touched,
      applyKey: const Key('playlist-apply'),
      onApply: problem == null ? _apply : null,
      problem: problem,
      onDelete: widget.isNew ? null : _delete,
      deleteKey: const Key('playlist-delete'),
      children: [
        EditorSection(
          title: 'Details',
          child: Column(
            children: [
              TextField(
                key: const Key('playlist-name'),
                controller: _name,
                textCapitalization: TextCapitalization.sentences,
                onChanged: (_) => _change(() {}),
                decoration: InputDecoration(
                  labelText: 'Name',
                  errorText: _touched ? nameError : null,
                ),
              ),
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                title: const Text('Skip unavailable apps'),
                subtitle: const Text('Apps with nothing to show (for example no music playing) are passed over.'),
                value: _skip,
                onChanged: (v) => _change(() => _skip = v),
              ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 0, 0, Space.sm),
          child: Row(children: [
            Expanded(
              child: Semantics(
                header: true,
                child: Text(
                  _rows.isEmpty ? 'ENTRIES' : 'ENTRIES  -  ${_rows.length} APPS, ${totalSeconds}S LOOP',
                  style: text.labelMedium?.copyWith(color: scheme.primary, letterSpacing: 1, fontWeight: FontWeight.w700),
                ),
              ),
            ),
            TextButton.icon(
              key: const Key('add-entry'),
              onPressed: _add,
              icon: const Icon(Icons.add_rounded),
              label: const Text('Add app'),
            ),
          ]),
        ),
        if (_rows.isEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: Space.lg),
            child: Text('Add the apps this playlist cycles through. Drag the handles to reorder.',
                textAlign: TextAlign.center, style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant)),
          ),
        ReorderableListView(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          buildDefaultDragHandles: false,
          onReorderItem: (from, to) => _change(() => _rows.insert(to, _rows.removeAt(from))),
          children: [
            for (var i = 0; i < _rows.length; i++)
              _EntryCard(
                key: _rows[i].key,
                index: i,
                entry: _rows[i].entry,
                apps: apps,
                transitions: transitions,
                onChanged: (e) => _change(() => _rows[i].entry = e),
                onRemove: () => _change(() => _rows.removeAt(i)),
              ),
          ],
        ),
      ],
    );
  }
}

class _EntryCard extends StatefulWidget {
  const _EntryCard({
    super.key,
    required this.index,
    required this.entry,
    required this.apps,
    required this.transitions,
    required this.onChanged,
    required this.onRemove,
  });

  final int index;
  final EntryDoc entry;
  final List<MatrixApp> apps;
  final List<String> transitions;
  final ValueChanged<EntryDoc> onChanged;
  final VoidCallback onRemove;

  @override
  State<_EntryCard> createState() => _EntryCardState();
}

class _EntryCardState extends State<_EntryCard> {
  late final TextEditingController _seconds = TextEditingController(text: _fmt(widget.entry.durationMs));

  static String _fmt(int ms) => (ms / 1000).toString().replaceAll(RegExp(r'\.0$'), '');

  @override
  void dispose() {
    _seconds.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final e = widget.entry;
    final appIds = {for (final a in widget.apps) a.id, if (e.appId.isNotEmpty) e.appId}.toList();
    String nameOf(String id) {
      for (final a in widget.apps) {
        if (a.id == id) return a.name;
      }
      return id;
    }

    final transitionItems = {...widget.transitions, if (e.transition != null) e.transition!}.toList();
    return Card(
      margin: const EdgeInsets.symmetric(vertical: 4),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(4, 8, 4, 8),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            ReorderableDragStartListener(
              index: widget.index,
              child: const Padding(
                padding: EdgeInsets.fromLTRB(8, 14, 8, 0),
                child: Icon(Icons.drag_handle_rounded),
              ),
            ),
            Expanded(
              child: Column(
                children: [
                  DropdownButtonFormField<String>(
                    key: Key('entry-app-${widget.index}'),
                    isExpanded: true,
                    initialValue: e.appId.isEmpty ? null : e.appId,
                    decoration: const InputDecoration(labelText: 'App', isDense: true),
                    items: [for (final id in appIds) DropdownMenuItem(value: id, child: Text(nameOf(id)))],
                    onChanged: (v) {
                      if (v != null) widget.onChanged(e.copyWith(appId: v));
                    },
                  ),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      SizedBox(
                        width: 110,
                        child: TextField(
                          key: Key('entry-seconds-${widget.index}'),
                          controller: _seconds,
                          keyboardType: const TextInputType.numberWithOptions(decimal: true),
                          decoration: const InputDecoration(
                              labelText: 'Seconds', isDense: true),
                          onChanged: (v) {
                            final s = double.tryParse(v);
                            if (s != null && s > 0) widget.onChanged(e.copyWith(durationMs: (s * 1000).round()));
                          },
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: DropdownButtonFormField<String?>(
                          key: Key('entry-transition-${widget.index}'),
                          isExpanded: true,
                          initialValue: e.transition,
                          decoration: const InputDecoration(labelText: 'Transition', isDense: true),
                          items: [
                            const DropdownMenuItem<String?>(value: null, child: Text('Default')),
                            for (final t in transitionItems) DropdownMenuItem<String?>(value: t, child: Text(t)),
                          ],
                          onChanged: (v) => widget.onChanged(v == null ? e.copyWith(clearTransition: true) : e.copyWith(transition: v)),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            IconButton(
              key: Key('entry-remove-${widget.index}'),
              tooltip: 'Remove entry',
              onPressed: widget.onRemove,
              icon: const Icon(Icons.close_rounded),
            ),
          ],
        ),
      ),
    );
  }
}
