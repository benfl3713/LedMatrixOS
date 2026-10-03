import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/schedule_models.dart';
import '../../core/providers.dart';

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
}) =>
    showModalBottomSheet<PlaylistEditResult>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      showDragHandle: true,
      builder: (_) => PlaylistEditor(initial: playlist, isNew: isNew),
    );

/// One row being edited. A stable [key] lets the reorderable list keep each row's text field state.
class _Row {
  _Row(this.entry) : key = UniqueKey();

  EntryDoc entry;
  final Key key;
}

class PlaylistEditor extends ConsumerStatefulWidget {
  const PlaylistEditor({super.key, required this.initial, required this.isNew});

  final PlaylistDoc initial;
  final bool isNew;

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

  void _add() {
    final apps = ref.read(appListProvider).value?.apps ?? const <MatrixApp>[];
    setState(() => _rows.add(_Row(EntryDoc(appId: apps.isEmpty ? '' : apps.first.id))));
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

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    final scheme = Theme.of(context).colorScheme;
    final apps = ref.watch(appListProvider).value?.apps ?? const <MatrixApp>[];
    final transitions = ref.watch(transitionsProvider).value?.transitions ?? const <String>[];

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(widget.isNew ? 'New playlist' : 'Edit playlist', style: text.titleLarge),
            const SizedBox(height: 16),
            TextField(
              key: const Key('playlist-name'),
              controller: _name,
              decoration: const InputDecoration(labelText: 'Name', border: OutlineInputBorder()),
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Skip unavailable apps'),
              value: _skip,
              onChanged: (v) => setState(() => _skip = v),
            ),
            Row(children: [
              Expanded(child: Text('Entries (drag to reorder)', style: text.labelLarge)),
              TextButton.icon(
                key: const Key('add-entry'),
                onPressed: _add,
                icon: const Icon(Icons.add),
                label: const Text('Add app'),
              ),
            ]),
            if (_rows.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 16),
                child: Text('A playlist needs at least one entry.', style: TextStyle(color: scheme.error)),
              ),
            ReorderableListView(
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              buildDefaultDragHandles: false,
              onReorderItem: (from, to) => setState(() => _rows.insert(to, _rows.removeAt(from))),
              children: [
                for (var i = 0; i < _rows.length; i++)
                  _EntryCard(
                    key: _rows[i].key,
                    index: i,
                    entry: _rows[i].entry,
                    apps: apps,
                    transitions: transitions,
                    onChanged: (e) => setState(() => _rows[i].entry = e),
                    onRemove: () => setState(() => _rows.removeAt(i)),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                if (!widget.isNew)
                  TextButton.icon(
                    key: const Key('playlist-delete'),
                    onPressed: () => Navigator.pop(context, const PlaylistEditResult.delete()),
                    icon: const Icon(Icons.delete_outline_rounded),
                    label: const Text('Delete'),
                    style: TextButton.styleFrom(foregroundColor: scheme.error),
                  ),
                const Spacer(),
                TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
                const SizedBox(width: 8),
                FilledButton(
                  key: const Key('playlist-apply'),
                  onPressed: _apply,
                  child: const Text('Apply'),
                ),
              ],
            ),
          ],
        ),
      ),
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
                    decoration: const InputDecoration(labelText: 'App', isDense: true, border: OutlineInputBorder()),
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
                              labelText: 'Seconds', isDense: true, border: OutlineInputBorder()),
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
                          decoration: const InputDecoration(labelText: 'Transition', isDense: true, border: OutlineInputBorder()),
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
