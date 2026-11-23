import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/schedule_models.dart';
import '../../core/connection.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import '../apps/color_picker.dart';
import 'color_swatches.dart';
import 'notify_preview.dart';
import 'notify_provider.dart';

/// Defaults that look good on the panel: white text on a deep accent banner.
const _defaultToastText = Color(0xFFFFFFFF);
const _defaultToastBackground = Color(0xFF00677A);
const _defaultBadge = Color(0xFFFF3C3C);
const _defaultAlert = Color(0xFF9600FF);

class NotifyPage extends ConsumerStatefulWidget {
  const NotifyPage({super.key});

  @override
  ConsumerState<NotifyPage> createState() => _NotifyPageState();
}

class _NotifyPageState extends ConsumerState<NotifyPage> {
  NotifyMode _mode = NotifyMode.toast;
  final _text = TextEditingController();
  final _badgeId = TextEditingController(text: 'badge');
  final _x = TextEditingController();
  final _y = TextEditingController();
  Color _color = _defaultToastText;
  Color _background = _defaultToastBackground;
  double _seconds = 4;
  double _size = 4;
  bool _pulsing = true;
  bool _flashOnly = false;
  bool _sending = false;

  /// True after a successful send until the composer changes again.
  bool _sent = false;

  /// The preset being edited in the composer, if any (Save then updates it in place).
  String? _editingPreset;

  @override
  void dispose() {
    _text.dispose();
    _badgeId.dispose();
    _x.dispose();
    _y.dispose();
    super.dispose();
  }

  /// Any composer edit clears the "Sent" confirmation.
  void _edit(VoidCallback fn) => setState(() {
        _sent = false;
        fn();
      });

  void _setMode(NotifyMode mode) {
    _edit(() {
      _mode = mode;
      // Sensible default colours per mode.
      switch (mode) {
        case NotifyMode.toast:
          _color = _defaultToastText;
          _background = _defaultToastBackground;
        case NotifyMode.badge:
          _color = _defaultBadge;
        case NotifyMode.alert:
          _color = _defaultAlert;
      }
    });
  }

  Map<String, Object?> _fields() => {
        'text': _text.text,
        'color': colorToHex(_color),
        'background': colorToHex(_background),
        'seconds': _seconds,
        'id': _badgeId.text,
        'x': _x.text,
        'y': _y.text,
        'size': _size.round(),
        'pulsing': _pulsing,
        'flashOnly': _flashOnly,
      };

  /// Loads a preset into the composer for editing.
  void _loadPreset(NotifyPreset p) {
    final f = p.fields;
    _edit(() {
      _mode = p.mode;
      _text.text = (f['text'] ?? '') as String;
      _color = parseHexColor(f['color']) ?? _color;
      _background = parseHexColor(f['background']) ?? _background;
      _seconds = f['seconds'] is num ? (f['seconds'] as num).toDouble().clamp(1, 60) : _seconds;
      _badgeId.text = (f['id'] ?? 'badge') as String;
      _x.text = (f['x'] ?? '') as String;
      _y.text = (f['y'] ?? '') as String;
      _size = f['size'] is num ? (f['size'] as num).toDouble().clamp(1, 32) : _size;
      _pulsing = (f['pulsing'] ?? true) as bool;
      _flashOnly = (f['flashOnly'] ?? false) as bool;
      _editingPreset = p.name;
    });
  }

  /// Sends [mode] with [f] (the composer's or a preset's fields). Returns whether it went out.
  Future<bool> _dispatch(NotifyMode mode, Map<String, Object?> f) async {
    final api = ref.read(apiProvider);
    String s(String key) => (f[key] ?? '').toString().trim();
    final color = colorToHex(parseHexColor(f['color']) ?? _defaultToastText);
    final background = colorToHex(parseHexColor(f['background']) ?? _defaultToastBackground);
    final result = switch (mode) {
      NotifyMode.toast => await api.sendToast(
          message: s('text'),
          seconds: f['seconds'] is num ? (f['seconds'] as num).toDouble() : 4,
          color: color,
          background: background,
        ),
      NotifyMode.badge => await api.sendBadge(
          id: s('id'),
          x: int.tryParse(s('x')),
          y: int.tryParse(s('y')),
          size: f['size'] is num ? (f['size'] as num).round() : 4,
          color: color,
          pulsing: (f['pulsing'] ?? true) as bool,
        ),
      NotifyMode.alert => await api.sendAlert(
          message: f['flashOnly'] == true ? null : s('text'),
          color: color,
        ),
    };
    if (!mounted) return false;
    var ok = false;
    result.when(
      ok: (_) {
        ok = true;
        ref.read(overlaysProvider.notifier).refresh();
      },
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
    return ok;
  }

  String get _modeLabel => '${_mode.name[0].toUpperCase()}${_mode.name.substring(1)}';

  Future<void> _send() async {
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _sending = true);
    final ok = await _dispatch(_mode, _fields());
    if (!mounted) return;
    setState(() {
      _sending = false;
      _sent = ok;
    });
    if (ok) {
      messenger
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text('$_modeLabel sent to the display')));
    }
  }

  Future<void> _sendPreset(NotifyPreset p) async {
    final messenger = ScaffoldMessenger.of(context);
    final ok = await _dispatch(p.mode, p.fields);
    if (!mounted || !ok) return;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text('Sent "${p.name}"')));
  }

  bool get _canSend => switch (_mode) {
        NotifyMode.toast => _text.text.trim().isNotEmpty,
        NotifyMode.badge => _badgeId.text.trim().isNotEmpty,
        NotifyMode.alert => _flashOnly || _text.text.trim().isNotEmpty,
      };

  String? get _cannotSendHint => _canSend
      ? null
      : switch (_mode) {
          NotifyMode.toast => 'Type a message to send a toast.',
          NotifyMode.badge => 'Give the badge an id.',
          NotifyMode.alert => 'Type a message, or turn on Flash only.',
        };

  Future<void> _savePreset() async {
    final name = await showDialog<String>(
      context: context,
      useRootNavigator: true,
      builder: (_) => _PresetNameDialog(initial: _editingPreset ?? ''),
    );
    if (name == null || name.isEmpty || !mounted) return;
    // Renaming while editing replaces the old preset.
    final previous = _editingPreset;
    final notifier = ref.read(presetsProvider.notifier);
    if (previous != null && previous != name) await notifier.delete(previous);
    await notifier.save(NotifyPreset(name: name, mode: _mode, fields: _fields()));
    if (!mounted) return;
    setState(() => _editingPreset = null);
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text('Saved preset "$name"')));
  }

  Future<void> _presetMenu(NotifyPreset p) async {
    final action = await showModalBottomSheet<String>(
      context: context,
      useRootNavigator: true,
      builder: (sheetContext) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          ListTile(title: Text(p.name, style: Theme.of(context).textTheme.titleMedium), subtitle: Text(_describe(p))),
          ListTile(
            key: const Key('preset-menu-edit'),
            leading: const Icon(Icons.edit_outlined),
            title: const Text('Edit in composer'),
            onTap: () => Navigator.pop(sheetContext, 'edit'),
          ),
          ListTile(
            key: const Key('preset-menu-delete'),
            leading: Icon(Icons.delete_outline_rounded, color: Theme.of(context).colorScheme.error),
            title: const Text('Delete preset'),
            onTap: () => Navigator.pop(sheetContext, 'delete'),
          ),
        ]),
      ),
    );
    if (!mounted || action == null) return;
    if (action == 'edit') {
      _loadPreset(p);
    } else if (action == 'delete') {
      final ok = await confirmDestructive(
        context,
        title: 'Delete "${p.name}"?',
        message: 'This preset is removed from this phone.',
        confirmKey: const Key('preset-delete-confirm'),
      );
      if (ok && mounted) await ref.read(presetsProvider.notifier).delete(p.name);
    }
  }

  static String _describe(NotifyPreset p) {
    final text = (p.fields['text'] ?? '').toString();
    final kind = '${p.mode.name[0].toUpperCase()}${p.mode.name.substring(1)}';
    return text.isEmpty ? kind : '$kind: $text';
  }

  Future<void> _confirmClear() async {
    final ok = await confirmDestructive(
      context,
      title: 'Clear all overlays?',
      message: 'Every toast, badge and alert on the display is removed.',
      confirmLabel: 'Clear all',
      confirmKey: const Key('clear-confirm'),
    );
    if (ok && mounted) await ref.read(overlaysProvider.notifier).clearAll();
  }

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Notify')),
      body: RefreshIndicator(
        onRefresh: () => ref.read(overlaysProvider.notifier).refresh(),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            const SectionHeader('Compose'),
            _composer(text),
            const SizedBox(height: Space.xl),
            _presets(),
            const SizedBox(height: Space.xl),
            _overlays(text),
          ],
        ),
      ),
    );
  }

  Widget _composer(TextTheme text) {
    final scheme = Theme.of(context).colorScheme;
    final offline = ref.watch(connectionProvider).isOffline;
    final hint = offline ? 'The display is offline.' : _cannotSendHint;
    final canPress = !_sending && _canSend && !offline;
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SegmentedButton<NotifyMode>(
            key: const Key('mode-selector'),
            segments: const [
              ButtonSegment(value: NotifyMode.toast, label: Text('Toast'), icon: Icon(Icons.chat_bubble_outline_rounded)),
              ButtonSegment(value: NotifyMode.badge, label: Text('Badge'), icon: Icon(Icons.circle_notifications_outlined)),
              ButtonSegment(value: NotifyMode.alert, label: Text('Alert'), icon: Icon(Icons.campaign_outlined)),
            ],
            selected: {_mode},
            onSelectionChanged: (s) => _setMode(s.first),
          ),
          const SizedBox(height: Space.xs),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: Space.sm),
            child: Text(
              switch (_mode) {
                NotifyMode.toast => 'A banner across the top of the display for a few seconds.',
                NotifyMode.badge => 'A small dot that stays until dismissed. Same id replaces it.',
                NotifyMode.alert => 'Takes over the whole display with a big message.',
              },
              style: text.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
            ),
          ),
          NotifyPreview(
            mode: _mode,
            text: _text.text.trim(),
            color: _color,
            background: _background,
            badgeSize: _size.round(),
            badgeX: int.tryParse(_x.text.trim()),
            badgeY: int.tryParse(_y.text.trim()),
            flashOnly: _flashOnly,
          ),
          const SizedBox(height: Space.lg),
          ..._modeFields(text),
          const SizedBox(height: Space.lg),
          FilledButton.icon(
            key: const Key('send-button'),
            style: _sent ? FilledButton.styleFrom(backgroundColor: scheme.tertiary, foregroundColor: scheme.onTertiary) : null,
            onPressed: canPress ? _send : null,
            icon: _sending
                ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                : Icon(_sent ? Icons.check_rounded : Icons.send_rounded),
            label: Text(_sent ? 'Sent! Tap to send again' : 'Send $_modeLabel'),
          ),
          if (hint != null)
            Padding(
              padding: const EdgeInsets.only(top: Space.sm),
              child: Text(hint, textAlign: TextAlign.center, style: text.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
            ),
          const SizedBox(height: Space.xs),
          TextButton.icon(
            key: const Key('save-preset'),
            onPressed: _savePreset,
            icon: const Icon(Icons.bookmark_add_outlined),
            label: Text(_editingPreset == null ? 'Save as preset' : 'Update preset "$_editingPreset"'),
          ),
        ],
      ),
    );
  }

  List<Widget> _modeFields(TextTheme text) {
    Widget messageField(String label) => TextField(
          key: const Key('notify-text'),
          controller: _text,
          maxLength: 80,
          textCapitalization: TextCapitalization.sentences,
          decoration: InputDecoration(labelText: label),
          onChanged: (_) => _edit(() {}),
        );

    Widget colors(String label, Color c, ValueChanged<Color> set, Key key) =>
        ColorSwatchRow(key: key, label: label, color: c, onChanged: (v) => _edit(() => set(v)));

    switch (_mode) {
      case NotifyMode.toast:
        return [
          messageField('Message'),
          const SizedBox(height: Space.sm),
          colors('Text colour', _color, (v) => _color = v, const Key('color-text')),
          const SizedBox(height: Space.md),
          colors('Background', _background, (v) => _background = v, const Key('color-background')),
          const SizedBox(height: Space.md),
          Row(children: [
            Expanded(child: Text('Duration', style: text.labelLarge)),
            Text('${_seconds.round()}s', style: numericStyle(text.titleSmall)),
          ]),
          Slider(
            key: const Key('toast-seconds'),
            value: _seconds,
            min: 1,
            max: 60,
            divisions: 59,
            label: '${_seconds.round()}s',
            semanticFormatterCallback: (v) => '${v.round()} seconds',
            onChanged: (v) => _edit(() => _seconds = v),
          ),
        ];
      case NotifyMode.badge:
        return [
          TextField(
            key: const Key('badge-id'),
            controller: _badgeId,
            decoration: const InputDecoration(
              labelText: 'Badge id',
              helperText: 'Sending the same id again replaces that badge.',
            ),
            onChanged: (_) => _edit(() {}),
          ),
          const SizedBox(height: Space.md),
          Row(
            children: [
              Expanded(
                child: TextField(
                  key: const Key('badge-x'),
                  controller: _x,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'X', helperText: 'Blank = top right'),
                  onChanged: (_) => _edit(() {}),
                ),
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: TextField(
                  key: const Key('badge-y'),
                  controller: _y,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'Y', helperText: 'Blank = 1'),
                  onChanged: (_) => _edit(() {}),
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.md),
          Row(children: [
            Expanded(child: Text('Size', style: text.labelLarge)),
            Text('${_size.round()} px', style: numericStyle(text.titleSmall)),
          ]),
          Slider(
            key: const Key('badge-size'),
            value: _size,
            min: 1,
            max: 32,
            divisions: 31,
            label: '${_size.round()}',
            semanticFormatterCallback: (v) => '${v.round()} pixels',
            onChanged: (v) => _edit(() => _size = v),
          ),
          colors('Colour', _color, (v) => _color = v, const Key('color-badge')),
          SwitchListTile(
            key: const Key('badge-pulsing'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Pulsing'),
            value: _pulsing,
            onChanged: (v) => _edit(() => _pulsing = v),
          ),
        ];
      case NotifyMode.alert:
        return [
          SwitchListTile(
            key: const Key('alert-flash'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Flash only (no message)'),
            value: _flashOnly,
            onChanged: (v) => _edit(() => _flashOnly = v),
          ),
          if (!_flashOnly) messageField('Message'),
          const SizedBox(height: Space.sm),
          colors('Colour', _color, (v) => _color = v, const Key('color-alert')),
        ];
    }
  }

  Widget _presets() {
    final scheme = Theme.of(context).colorScheme;
    final presets = ref.watch(presetsProvider);
    final offline = ref.watch(connectionProvider).isOffline;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SectionHeader('Presets'),
        SectionCard(
          child: presets.when(
            loading: () => const Skeleton(height: 40),
            error: (e, _) => Text("Couldn't read saved presets.", style: TextStyle(color: scheme.onSurfaceVariant)),
            data: (list) => list.isEmpty
                ? Row(children: [
                    Icon(Icons.bookmark_border_rounded, color: scheme.onSurfaceVariant),
                    const SizedBox(width: Space.md),
                    Expanded(
                      child: Text(
                        'Save a message you send often, like "Dinner is ready", and send it again with one tap.',
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: scheme.onSurfaceVariant),
                      ),
                    ),
                  ])
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (final p in list)
                            GestureDetector(
                              key: Key('preset-${p.name}'),
                              behavior: HitTestBehavior.opaque,
                              onLongPress: () => _presetMenu(p),
                              child: ActionChip(
                                avatar: Icon(switch (p.mode) {
                                  NotifyMode.toast => Icons.chat_bubble_outline_rounded,
                                  NotifyMode.badge => Icons.circle_notifications_outlined,
                                  NotifyMode.alert => Icons.campaign_outlined,
                                }, size: 18),
                                label: Text(p.name),
                                onPressed: offline ? null : () => _sendPreset(p),
                              ),
                            ),
                        ],
                      ),
                      const SizedBox(height: Space.sm),
                      Text(
                        'Tap to send right away. Long-press to edit or delete.',
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
                      ),
                    ],
                  ),
          ),
        ),
      ],
    );
  }

  Widget _overlays(TextTheme text) {
    final overlays = ref.watch(overlaysProvider);
    final scheme = Theme.of(context).colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionHeader(
          'On the display now',
          trailing: Row(mainAxisSize: MainAxisSize.min, children: [
            IconButton(
              tooltip: 'Refresh',
              onPressed: () => ref.read(overlaysProvider.notifier).refresh(),
              icon: const Icon(Icons.refresh_rounded),
            ),
            TextButton(
              key: const Key('clear-all'),
              onPressed: (overlays.value?.isEmpty ?? true) ? null : _confirmClear,
              child: const Text('Clear all'),
            ),
          ]),
        ),
        SectionCard(
          child: overlays.when(
            loading: () => const Column(children: [Skeleton(height: 40), SizedBox(height: 8), Skeleton(height: 40)]),
            error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(overlaysProvider)),
            data: (list) => list.isEmpty
                ? Padding(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    child: Text('Nothing is showing.', style: TextStyle(color: scheme.onSurfaceVariant)),
                  )
                : Column(
                    children: [
                      for (final o in list)
                        ListTile(
                          key: Key('overlay-${o.id}'),
                          contentPadding: EdgeInsets.zero,
                          leading: Icon(_kindIcon(o.kind)),
                          title: Text(o.text == null || o.text!.isEmpty ? o.id : o.text!, maxLines: 1, overflow: TextOverflow.ellipsis),
                          subtitle: Text('${o.kind}, priority ${o.priority}, ${_remaining(o)}'),
                          trailing: IconButton(
                            key: Key('dismiss-${o.id}'),
                            tooltip: 'Dismiss',
                            onPressed: () => ref.read(overlaysProvider.notifier).dismiss(o.id),
                            icon: const Icon(Icons.close_rounded),
                          ),
                        ),
                    ],
                  ),
          ),
        ),
      ],
    );
  }

  static IconData _kindIcon(String kind) => switch (kind) {
        'toast' => Icons.chat_bubble_outline_rounded,
        'badge' => Icons.circle_notifications_outlined,
        'alert' => Icons.campaign_outlined,
        _ => Icons.layers_outlined,
      };

  static String _remaining(OverlayInfo o) {
    if (o.exiting) return 'leaving';
    final s = o.remainingSeconds;
    if (s == null) return 'until dismissed';
    return '${s.ceil()}s left';
  }
}

class _PresetNameDialog extends StatefulWidget {
  const _PresetNameDialog({this.initial = ''});

  final String initial;

  @override
  State<_PresetNameDialog> createState() => _PresetNameDialogState();
}

class _PresetNameDialogState extends State<_PresetNameDialog> {
  late final _controller = TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('Save preset'),
        content: TextField(
          key: const Key('preset-name'),
          controller: _controller,
          autofocus: true,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(labelText: 'Name', helperText: 'Saving over an existing name replaces it.'),
          onSubmitted: (v) => Navigator.pop(context, v.trim()),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(
            key: const Key('preset-save-confirm'),
            onPressed: () => Navigator.pop(context, _controller.text.trim()),
            child: const Text('Save'),
          ),
        ],
      );
}
