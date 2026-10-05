import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/screen_models.dart';
import '../../core/widgets.dart';
import '../now/preview_card.dart';
import 'node_form.dart';
import 'screens_provider.dart';

/// Edits one screen: live preview, name, node tree with a schema-driven property form, save/activate/delete.
class ScreenEditorPage extends ConsumerStatefulWidget {
  const ScreenEditorPage({super.key, required this.id});

  final String id;

  @override
  ConsumerState<ScreenEditorPage> createState() => _ScreenEditorPageState();
}

class _ScreenEditorPageState extends ConsumerState<ScreenEditorPage> {
  /// The node whose property form is open. Nodes are edited in place, so identity is stable.
  ScreenNode? _selected;
  TextEditingController? _name;

  @override
  void dispose() {
    _name?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final editor = ref.watch(screenEditorProvider(widget.id));
    final schema = ref.watch(screenSchemaProvider);
    final notifier = ref.read(screenEditorProvider(widget.id).notifier);
    final state = editor.value;

    // After a failed save, open the first node that has a problem.
    ref.listen(screenEditorProvider(widget.id), (previous, next) {
      final s = next.value;
      if (s == null || s.errors.isEmpty || identical(previous?.value?.errors, s.errors)) return;
      final first = s.placed.byNode.keys.firstOrNull;
      if (first != null && _selected == null) setState(() => _selected = first);
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(state == null || state.draft.name.isEmpty ? 'Screen' : state.draft.name),
        actions: [
          if (state != null) ...[
            IconButton(
              key: const Key('delete-screen'),
              tooltip: 'Delete screen',
              onPressed: () => _confirmDelete(context, notifier, state),
              icon: const Icon(Icons.delete_outline_rounded),
            ),
          ],
        ],
      ),
      body: editor.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorView(error: e, onRetry: notifier.reload),
        data: (s) => schema.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(screenSchemaProvider)),
          data: (sch) => _body(context, s, sch, notifier),
        ),
      ),
    );
  }

  Widget _body(BuildContext context, ScreenEditState s, ScreenSchema schema, ScreenEditor notifier) {
    _name ??= TextEditingController(text: s.draft.name);
    final placed = s.placed;
    final paths = nodePaths(s.draft.root);
    final scheme = Theme.of(context).colorScheme;
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
      children: [
        const PreviewCard(),
        const SizedBox(height: 4),
        Padding(
          padding: const EdgeInsets.only(left: 4, bottom: 8),
          child: Text(
            s.dirty ? 'Preview shows what is on the device: Save, then Activate, to see your edits.' : 'Preview shows the active app.',
            style: Theme.of(context).textTheme.labelSmall?.copyWith(color: scheme.onSurfaceVariant),
          ),
        ),
        TextField(
          key: const Key('screen-name'),
          controller: _name,
          decoration: InputDecoration(
            labelText: 'Name',
            helperText: 'id: ${s.draft.id}',
            errorText: placed.name.isEmpty ? null : placed.name.join('; '),
            border: const OutlineInputBorder(),
          ),
          onChanged: notifier.setName,
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(
              child: FilledButton.icon(
                key: const Key('save-screen'),
                onPressed: s.saving || !s.dirty ? null : () => notifier.save(),
                icon: s.saving
                    ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.save_rounded),
                label: Text(s.isNew ? 'Save new screen' : 'Save'),
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: FilledButton.tonalIcon(
                key: const Key('activate-screen'),
                onPressed: s.saving ? null : () => notifier.activate(),
                icon: const Icon(Icons.play_circle_rounded),
                label: Text(s.dirty ? 'Save and activate' : 'Activate'),
              ),
            ),
          ],
        ),
        if (s.dirty && !s.isNew)
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              key: const Key('discard-screen'),
              onPressed: s.saving
                  ? null
                  : () {
                      _name?.dispose();
                      _name = null;
                      setState(() => _selected = null);
                      notifier.discard();
                    },
              child: const Text('Discard changes'),
            ),
          ),
        if (placed.general.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Card(
              color: scheme.errorContainer,
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Text(placed.general.join('\n'), style: TextStyle(color: scheme.onErrorContainer)),
              ),
            ),
          ),
        const SizedBox(height: 12),
        Text('Layout', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 4),
        _TreeNode(
          node: s.draft.root,
          parent: null,
          slot: '',
          index: null,
          depth: 0,
          insideList: false,
          paths: paths,
          editorState: s,
          schema: schema,
          notifier: notifier,
          selected: _selected,
          onSelect: (n) => setState(() => _selected = identical(_selected, n) ? null : n),
          onDeleted: (n) {
            final sel = _selected;
            if (sel != null && nodePaths(n).containsKey(sel)) setState(() => _selected = null);
          },
        ),
      ],
    );
  }

  Future<void> _confirmDelete(BuildContext context, ScreenEditor notifier, ScreenEditState s) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Delete screen?'),
        content: Text(s.isNew ? 'Discard this unsaved screen?' : 'Delete "${s.draft.name}" from the device? This cannot be undone.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Cancel')),
          FilledButton(key: const Key('confirm-delete-screen'), onPressed: () => Navigator.pop(dialogContext, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok != true) return;
    if (await notifier.delete() && context.mounted) context.pop();
  }
}

class _TreeNode extends StatelessWidget {
  const _TreeNode({
    super.key,
    required this.node,
    required this.parent,
    required this.slot,
    required this.index,
    required this.depth,
    required this.insideList,
    required this.paths,
    required this.editorState,
    required this.schema,
    required this.notifier,
    required this.selected,
    required this.onSelect,
    required this.onDeleted,
  });

  final ScreenNode node;
  final ScreenNode? parent;
  final String slot;
  final int? index;
  final int depth;
  final bool insideList;
  final Map<ScreenNode, String> paths;
  final ScreenEditState editorState;
  final ScreenSchema schema;
  final ScreenEditor notifier;
  final ScreenNode? selected;
  final ValueChanged<ScreenNode> onSelect;
  final ValueChanged<ScreenNode> onDeleted;

  String get _path => paths[node] ?? '';

  /// Slots of this node's type that can take another node right now.
  List<String> _openSlots() {
    final type = schema.typeOf(node.type);
    if (type == null) return const [];
    return [
      for (final s in type.slots)
        if (s == childrenSlot || !node.slots.containsKey(s)) s,
    ];
  }

  String _summary() {
    for (final k in const ['text', 'format', 'source', 'value', 'values', 'interval_ms']) {
      final v = node.props[k];
      if (v != null) return '$v';
    }
    return '';
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final theme = Theme.of(context);
    final problems = editorState.placed.byNode[node] ?? const <NodeProblem>[];
    final isSelected = identical(selected, node);
    final open = _openSlots();
    final title = [if (slot.isNotEmpty && slot != childrenSlot) slot, node.type].join(': ');
    final summary = _summary();
    final siblings = parent?.children.length ?? 0;
    final nodeInsideList = insideList || node.type == 'list';

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: EdgeInsets.only(left: depth * 14.0),
          child: Card(
            margin: const EdgeInsets.symmetric(vertical: 2),
            color: isSelected ? scheme.primaryContainer : null,
            clipBehavior: Clip.antiAlias,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                InkWell(
                  key: Key('node-$_path'),
                  onTap: () => onSelect(node),
                  child: Padding(
                    padding: const EdgeInsets.only(left: 12),
                    child: Row(
                      children: [
                        if (problems.isNotEmpty) Padding(
                          padding: const EdgeInsets.only(right: 8),
                          child: Icon(Icons.error_outline, size: 18, color: scheme.error),
                        ),
                        Expanded(
                          child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 10),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(title, style: theme.textTheme.titleSmall),
                                if (summary.isNotEmpty)
                                  Text(summary, maxLines: 1, overflow: TextOverflow.ellipsis, style: theme.textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
                              ],
                            ),
                          ),
                        ),
                        if (open.isNotEmpty)
                          IconButton(
                            key: Key('add-$_path'),
                            tooltip: 'Add a node',
                            onPressed: () => _add(context, open),
                            icon: const Icon(Icons.add_circle_outline_rounded),
                          ),
                        if (parent != null && slot == childrenSlot && siblings > 1) ...[
                          IconButton(
                            key: Key('up-$_path'),
                            tooltip: 'Move up',
                            visualDensity: VisualDensity.compact,
                            onPressed: index! > 0 ? () => notifier.moveChild(parent!, index!, -1) : null,
                            icon: const Icon(Icons.arrow_upward_rounded, size: 20),
                          ),
                          IconButton(
                            key: Key('down-$_path'),
                            tooltip: 'Move down',
                            visualDensity: VisualDensity.compact,
                            onPressed: index! < siblings - 1 ? () => notifier.moveChild(parent!, index!, 1) : null,
                            icon: const Icon(Icons.arrow_downward_rounded, size: 20),
                          ),
                        ],
                        if (parent != null)
                          IconButton(
                            key: Key('delete-$_path'),
                            tooltip: 'Delete node',
                            visualDensity: VisualDensity.compact,
                            onPressed: () {
                              notifier.removeNode(parent!, slot, index);
                              onDeleted(node);
                            },
                            icon: const Icon(Icons.delete_outline_rounded, size: 20),
                          )
                        else
                          const SizedBox(width: 8),
                      ],
                    ),
                  ),
                ),
                if (!isSelected && problems.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(12, 0, 12, 8),
                    child: Text(problems.join('\n'), key: Key('problems-$_path'), style: TextStyle(color: scheme.error, fontSize: 12)),
                  ),
                if (isSelected)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(12, 4, 12, 8),
                    child: NodeForm(
                      node: node,
                      schema: schema,
                      problems: problems,
                      insideList: nodeInsideList,
                      onSet: (prop, value) => notifier.setProp(node, prop, value),
                    ),
                  ),
              ],
            ),
          ),
        ),
        for (final e in node.childEntries)
          _TreeNode(
            key: ObjectKey(e.node),
            node: e.node,
            parent: node,
            slot: e.slot,
            index: e.index,
            depth: depth + 1,
            insideList: nodeInsideList,
            paths: paths,
            editorState: editorState,
            schema: schema,
            notifier: notifier,
            selected: selected,
            onSelect: onSelect,
            onDeleted: onDeleted,
          ),
      ],
    );
  }

  Future<void> _add(BuildContext context, List<String> open) async {
    var target = open.first;
    if (open.length > 1) {
      final picked = await showModalBottomSheet<String>(
        context: context,
        showDragHandle: true,
        builder: (sheetContext) => SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const ListTile(title: Text('Add to slot')),
              for (final s in open)
                ListTile(
                  key: Key('slot-$s'),
                  title: Text(s == childrenSlot ? 'children (add another)' : s),
                  onTap: () => Navigator.pop(sheetContext, s),
                ),
            ],
          ),
        ),
      );
      if (picked == null || !context.mounted) return;
      target = picked;
    }
    final type = await showDialog<String>(
      context: context,
      builder: (dialogContext) => SimpleDialog(
        title: const Text('Add node'),
        children: [
          for (final t in schema.nodeTypes)
            SimpleDialogOption(
              key: Key('type-${t.type}'),
              onPressed: () => Navigator.pop(dialogContext, t.type),
              child: Text(t.type),
            ),
        ],
      ),
    );
    if (type == null) return;
    final added = notifier.addNode(node, target, type);
    if (added != null) onSelect(added);
  }
}
