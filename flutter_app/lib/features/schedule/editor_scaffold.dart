import 'package:flutter/material.dart';

import '../../core/tokens.dart';

/// Pushes a full-screen editor on the root navigator (so it covers the bottom navigation bar).
Future<T?> pushEditor<T>(BuildContext context, Widget editor) =>
    Navigator.of(context, rootNavigator: true).push<T>(MaterialPageRoute<T>(fullscreenDialog: true, builder: (_) => editor));

/// Shared frame for the rule and playlist editors: a close button, a sticky Apply bar, and a guard that asks before
/// throwing away unsaved edits on back, close or swipe.
class EditorScaffold extends StatelessWidget {
  const EditorScaffold({
    super.key,
    required this.title,
    required this.dirty,
    required this.children,
    required this.onApply,
    required this.applyKey,
    this.applyLabel = 'Apply',
    this.problem,
    this.onDelete,
    this.deleteKey,
    this.deleteLabel = 'Delete',
  });

  final String title;

  /// True once the user changed something; back then asks for confirmation.
  final bool dirty;
  final List<Widget> children;

  /// Null disables Apply (the form is invalid); [problem] says why.
  final VoidCallback? onApply;
  final Key applyKey;
  final String applyLabel;
  final String? problem;
  final VoidCallback? onDelete;
  final Key? deleteKey;
  final String deleteLabel;

  Future<void> _confirmDiscard(BuildContext context) async {
    final discard = await showDialog<bool>(
      context: context,
      useRootNavigator: true,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Discard changes?'),
        content: const Text('What you changed here has not been applied.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Keep editing')),
          FilledButton(
            key: const Key('editor-discard-confirm'),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Discard'),
          ),
        ],
      ),
    );
    if (discard == true && context.mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return PopScope(
      canPop: !dirty,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _confirmDiscard(context);
      },
      child: Scaffold(
        appBar: AppBar(
          leading: IconButton(
            key: const Key('editor-close'),
            tooltip: 'Close',
            icon: const Icon(Icons.close_rounded),
            onPressed: () => Navigator.maybePop(context),
          ),
          title: Text(title),
        ),
        body: ListView(
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.xl),
          children: children,
        ),
        bottomNavigationBar: Material(
          color: scheme.surfaceContainerLowest,
          child: SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.sm),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (problem != null)
                    Padding(
                      padding: const EdgeInsets.only(bottom: Space.sm),
                      child: Row(
                        children: [
                          Icon(Icons.info_outline_rounded, size: 16, color: scheme.error),
                          const SizedBox(width: 6),
                          Expanded(
                            child: Text(problem!, key: const Key('editor-problem'), style: TextStyle(color: scheme.error)),
                          ),
                        ],
                      ),
                    ),
                  Row(
                    children: [
                      if (onDelete != null) ...[
                        TextButton.icon(
                          key: deleteKey,
                          onPressed: onDelete,
                          icon: const Icon(Icons.delete_outline_rounded),
                          label: Text(deleteLabel),
                          style: TextButton.styleFrom(foregroundColor: scheme.error),
                        ),
                        const SizedBox(width: Space.sm),
                      ],
                      Expanded(
                        child: FilledButton(key: applyKey, onPressed: onApply, child: Text(applyLabel)),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// A titled block inside an editor.
class EditorSection extends StatelessWidget {
  const EditorSection({super.key, required this.title, required this.child, this.subtitle});

  final String title;
  final String? subtitle;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 0, 4, Space.sm),
            child: Semantics(
              header: true,
              child: Text(title.toUpperCase(),
                  style: text.labelMedium?.copyWith(color: scheme.primary, letterSpacing: 1, fontWeight: FontWeight.w700)),
            ),
          ),
          Card(child: Padding(padding: const EdgeInsets.all(Space.lg), child: child)),
          if (subtitle != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(4, Space.sm, 4, 0),
              child: Text(subtitle!, style: text.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
            ),
        ],
      ),
    );
  }
}
