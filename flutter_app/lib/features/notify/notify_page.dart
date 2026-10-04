import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/schedule_models.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';
import '../apps/color_picker.dart';
import 'notify_provider.dart';

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
  Color _color = const Color(0xFF000000);
  Color _background = const Color(0xFFFFFFFF);
  double _seconds = 4;
  double _size = 4;
  bool _pulsing = true;
  bool _flashOnly = false;
  bool _sending = false;

  @override
  void dispose() {
    _text.dispose();
    _badgeId.dispose();
    _x.dispose();
    _y.dispose();
    super.dispose();
  }

  void _setMode(NotifyMode mode) {
    setState(() {
      _mode = mode;
      // Sensible default colours per mode.
      switch (mode) {
        case NotifyMode.toast:
          _color = const Color(0xFF000000);
          _background = const Color(0xFFFFFFFF);
        case NotifyMode.badge:
          _color = const Color(0xFFFF3C3C);
        case NotifyMode.alert:
          _color = const Color(0xFF9600FF);
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

  void _applyPreset(NotifyPreset p) {
    final f = p.fields;
    setState(() {
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
    });
  }

  Future<void> _send() async {
    final api = ref.read(apiProvider);
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _sending = true);
    final result = switch (_mode) {
      NotifyMode.toast => await api.sendToast(
          message: _text.text.trim(),
          seconds: _seconds,
          color: colorToHex(_color),
          background: colorToHex(_background),
        ),
      NotifyMode.badge => await api.sendBadge(
          id: _badgeId.text.trim(),
          x: int.tryParse(_x.text.trim()),
          y: int.tryParse(_y.text.trim()),
          size: _size.round(),
          color: colorToHex(_color),
          pulsing: _pulsing,
        ),
      NotifyMode.alert => await api.sendAlert(
          message: _flashOnly ? null : _text.text.trim(),
          color: colorToHex(_color),
        ),
    };
    if (!mounted) return;
    setState(() => _sending = false);
    result.when(
      ok: (_) {
        messenger
          ..hideCurrentSnackBar()
          ..showSnackBar(SnackBar(content: Text('${_mode.name[0].toUpperCase()}${_mode.name.substring(1)} sent')));
        ref.read(overlaysProvider.notifier).refresh();
      },
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  bool get _canSend => switch (_mode) {
        NotifyMode.toast => _text.text.trim().isNotEmpty,
        NotifyMode.badge => _badgeId.text.trim().isNotEmpty,
        NotifyMode.alert => _flashOnly || _text.text.trim().isNotEmpty,
      };

  Future<void> _savePreset() async {
    final name = await showDialog<String>(context: context, builder: (_) => const _PresetNameDialog());
    if (name == null || name.isEmpty || !mounted) return;
    await ref.read(presetsProvider.notifier).save(NotifyPreset(name: name, mode: _mode, fields: _fields()));
  }

  Future<void> _pickColor(Color current, ValueChanged<Color> onPicked, String title) async {
    final c = await showColorPicker(context, initial: current, title: title);
    if (c != null && mounted) onPicked(c);
  }

  Future<void> _confirmClear() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Clear all overlays?'),
        content: const Text('Every toast, badge and alert on the display is removed.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(key: const Key('clear-confirm'), onPressed: () => Navigator.pop(context, true), child: const Text('Clear all')),
        ],
      ),
    );
    if (ok == true && mounted) await ref.read(overlaysProvider.notifier).clearAll();
  }

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Notify')),
      body: RefreshIndicator(
        onRefresh: () => ref.read(overlaysProvider.notifier).refresh(),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            SectionCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
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
                  const SizedBox(height: 16),
                  ..._modeFields(text),
                  const SizedBox(height: 16),
                  Row(
                    children: [
                      Expanded(
                        child: FilledButton.icon(
                          key: const Key('send-button'),
                          onPressed: _sending || !_canSend ? null : _send,
                          icon: _sending
                              ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                              : const Icon(Icons.send_rounded),
                          label: const Text('Send'),
                        ),
                      ),
                      const SizedBox(width: 12),
                      OutlinedButton.icon(
                        key: const Key('save-preset'),
                        onPressed: _savePreset,
                        icon: const Icon(Icons.bookmark_add_outlined),
                        label: const Text('Save preset'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(height: 8),
            _presets(),
            const SizedBox(height: 8),
            _overlays(text),
          ],
        ),
      ),
    );
  }

  List<Widget> _modeFields(TextTheme text) {
    Widget colorTile(String label, Color c, ValueChanged<Color> set, Key key) => ListTile(
          key: key,
          contentPadding: EdgeInsets.zero,
          title: Text(label),
          subtitle: Text(colorToHex(c)),
          trailing: Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(
              color: c,
              shape: BoxShape.circle,
              border: Border.all(color: Theme.of(context).colorScheme.outlineVariant),
            ),
          ),
          onTap: () => _pickColor(c, (v) => setState(() => set(v)), label),
        );

    Widget messageField(String label) => TextField(
          key: const Key('notify-text'),
          controller: _text,
          maxLength: 80,
          decoration: InputDecoration(labelText: label, border: const OutlineInputBorder()),
          onChanged: (_) => setState(() {}),
        );

    switch (_mode) {
      case NotifyMode.toast:
        return [
          messageField('Message'),
          colorTile('Text colour', _color, (v) => _color = v, const Key('color-text')),
          colorTile('Background', _background, (v) => _background = v, const Key('color-background')),
          Text('Duration: ${_seconds.round()}s', style: text.labelLarge),
          Slider(
            key: const Key('toast-seconds'),
            value: _seconds,
            min: 1,
            max: 60,
            divisions: 59,
            label: '${_seconds.round()}s',
            onChanged: (v) => setState(() => _seconds = v),
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
              border: OutlineInputBorder(),
            ),
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: TextField(
                  key: const Key('badge-x'),
                  controller: _x,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'X (blank = top right)', border: OutlineInputBorder()),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: TextField(
                  key: const Key('badge-y'),
                  controller: _y,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'Y (blank = 1)', border: OutlineInputBorder()),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text('Size: ${_size.round()} px', style: text.labelLarge),
          Slider(
            key: const Key('badge-size'),
            value: _size,
            min: 1,
            max: 32,
            divisions: 31,
            label: '${_size.round()}',
            onChanged: (v) => setState(() => _size = v),
          ),
          colorTile('Colour', _color, (v) => _color = v, const Key('color-badge')),
          SwitchListTile(
            key: const Key('badge-pulsing'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Pulsing'),
            value: _pulsing,
            onChanged: (v) => setState(() => _pulsing = v),
          ),
        ];
      case NotifyMode.alert:
        return [
          SwitchListTile(
            key: const Key('alert-flash'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Flash only (no message)'),
            value: _flashOnly,
            onChanged: (v) => setState(() => _flashOnly = v),
          ),
          if (!_flashOnly) ...[
            messageField('Message'),
            colorTile('Colour', _color, (v) => _color = v, const Key('color-alert')),
          ],
        ];
    }
  }

  Widget _presets() {
    final presets = ref.watch(presetsProvider);
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Presets', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          presets.when(
            loading: () => const LinearProgressIndicator(),
            error: (e, _) => Text(e.toString()),
            data: (list) => list.isEmpty
                ? Text('No presets yet. Fill in the composer and tap Save preset.',
                    style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant))
                : Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (final p in list)
                        GestureDetector(
                          key: Key('preset-${p.name}'),
                          onLongPress: () async {
                            await ref.read(presetsProvider.notifier).delete(p.name);
                            if (!mounted) return;
                            ScaffoldMessenger.of(context)
                              ..hideCurrentSnackBar()
                              ..showSnackBar(SnackBar(content: Text('Deleted preset "${p.name}"')));
                          },
                          child: ActionChip(
                            avatar: Icon(switch (p.mode) {
                              NotifyMode.toast => Icons.chat_bubble_outline_rounded,
                              NotifyMode.badge => Icons.circle_notifications_outlined,
                              NotifyMode.alert => Icons.campaign_outlined,
                            }, size: 18),
                            label: Text(p.name),
                            onPressed: () => _applyPreset(p),
                          ),
                        ),
                    ],
                  ),
          ),
          if (presets.value?.isNotEmpty ?? false)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text('Tap to fill, long-press to delete.', style: Theme.of(context).textTheme.bodySmall),
            ),
        ],
      ),
    );
  }

  Widget _overlays(TextTheme text) {
    final overlays = ref.watch(overlaysProvider);
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text('Active overlays', style: text.titleMedium)),
              IconButton(
                tooltip: 'Refresh',
                onPressed: () => ref.read(overlaysProvider.notifier).refresh(),
                icon: const Icon(Icons.refresh),
              ),
              TextButton(
                key: const Key('clear-all'),
                onPressed: (overlays.value?.isEmpty ?? true) ? null : _confirmClear,
                child: const Text('Clear all'),
              ),
            ],
          ),
          overlays.when(
            loading: () => const Padding(padding: EdgeInsets.all(8), child: LinearProgressIndicator()),
            error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(overlaysProvider)),
            data: (list) => list.isEmpty
                ? Padding(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    child: Text('Nothing is showing.', style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
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
        ],
      ),
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
  const _PresetNameDialog();

  @override
  State<_PresetNameDialog> createState() => _PresetNameDialogState();
}

class _PresetNameDialogState extends State<_PresetNameDialog> {
  final _controller = TextEditingController();

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
          decoration: const InputDecoration(labelText: 'Name', border: OutlineInputBorder()),
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
