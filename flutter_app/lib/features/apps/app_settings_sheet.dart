import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/app_icons.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';
import 'app_settings_provider.dart';
import 'color_picker.dart';

/// Opens the settings of [app] in a draggable sheet. Works for active and inactive apps.
Future<void> showAppSettingsSheet(BuildContext context, MatrixApp app) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (_) => DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.75,
      minChildSize: 0.4,
      maxChildSize: 0.95,
      builder: (context, controller) => AppSettingsView(app: app, scrollController: controller),
    ),
  );
}

class AppSettingsView extends ConsumerWidget {
  const AppSettingsView({super.key, required this.app, this.scrollController});

  final MatrixApp app;
  final ScrollController? scrollController;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final settings = ref.watch(appSettingsProvider(app.id));
    final isActive = ref.watch(appListProvider.select((l) => l.value?.activeApp == app.id));

    return ListView(
      controller: scrollController,
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
      children: [
        Row(
          children: [
            Icon(iconForApp(app.id), size: 32),
            const SizedBox(width: 12),
            Expanded(child: Text(app.name, style: Theme.of(context).textTheme.titleLarge)),
            if (isActive)
              const Chip(label: Text('Active'), avatar: Icon(Icons.check_circle_rounded, size: 18))
            else
              FilledButton.icon(
                onPressed: () => ref.read(appListProvider.notifier).activate(app.id),
                icon: const Icon(Icons.play_arrow_rounded),
                label: const Text('Activate'),
              ),
          ],
        ),
        const SizedBox(height: 16),
        settings.when(
          loading: () => const Padding(
            padding: EdgeInsets.all(32),
            child: Center(child: CircularProgressIndicator()),
          ),
          error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(appSettingsProvider(app.id))),
          data: (list) => list.isEmpty
              ? const Padding(
                  padding: EdgeInsets.all(32),
                  child: Center(child: Text('This app has no settings.')),
                )
              : Column(
                  children: [
                    for (final s in list)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: SettingTile(
                          key: ValueKey(s.key),
                          setting: s,
                          onChanged: (v) => ref.read(appSettingsProvider(app.id).notifier).edit(s.key, v),
                        ),
                      ),
                  ],
                ),
        ),
      ],
    );
  }
}

/// One editor for one setting, chosen by its type.
class SettingTile extends StatelessWidget {
  const SettingTile({super.key, required this.setting, required this.onChanged});

  final AppSetting setting;
  final ValueChanged<Object> onChanged;

  @override
  Widget build(BuildContext context) {
    final s = setting;
    final theme = Theme.of(context);
    final description = s.description.isEmpty
        ? null
        : Text(s.description, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant));

    switch (s.type) {
      case AppSettingType.boolean:
        return SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(s.name),
          subtitle: description,
          value: s.currentValue == true,
          onChanged: onChanged,
        );

      case AppSettingType.integer:
        final value = (s.currentValue is num ? s.currentValue as num : 0).round();
        if (s.minValue != null && s.maxValue != null && s.maxValue! > s.minValue!) {
          final min = s.minValue!.toDouble();
          final max = s.maxValue!.toDouble();
          final span = (max - min).round();
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(children: [Expanded(child: Text(s.name)), Text('$value')]),
              if (description != null) description,
              Slider(
                value: value.toDouble().clamp(min, max),
                min: min,
                max: max,
                divisions: span <= 1000 ? span : null,
                label: '$value',
                onChanged: (v) => onChanged(v.round()),
              ),
            ],
          );
        }
        return _TextSetting(
          setting: s,
          keyboardType: TextInputType.number,
          onSubmitted: (text) {
            final n = int.tryParse(text);
            if (n != null) onChanged(n);
          },
        );

      case AppSettingType.string:
        return _TextSetting(setting: s, onSubmitted: onChanged);

      case AppSettingType.select:
        final options = s.options ?? const <String>[];
        final current = s.currentValue?.toString();
        if (options.isEmpty) return _TextSetting(setting: s, onSubmitted: onChanged);
        return DropdownButtonFormField<String>(
          initialValue: options.contains(current) ? current : null,
          isExpanded: true,
          decoration: InputDecoration(labelText: s.name, helperText: s.description.isEmpty ? null : s.description, border: const OutlineInputBorder()),
          items: [for (final o in options) DropdownMenuItem(value: o, child: Text(o))],
          onChanged: (v) {
            if (v != null) onChanged(v);
          },
        );

      case AppSettingType.color:
        final color = parseHexColor(s.currentValue) ?? Colors.black;
        return ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(s.name),
          subtitle: Text(s.description.isEmpty ? colorToHex(color) : '${colorToHex(color)}  ${s.description}'),
          trailing: Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: color,
              shape: BoxShape.circle,
              border: Border.all(color: theme.colorScheme.outline),
            ),
          ),
          onTap: () async {
            final picked = await showColorPicker(context, initial: color, title: s.name);
            if (picked != null) onChanged(colorToHex(picked));
          },
        );
    }
  }
}

/// Text input that saves on every edit (the notifier debounces) but is not overwritten by
/// live refreshes while the field has focus.
class _TextSetting extends StatefulWidget {
  const _TextSetting({required this.setting, required this.onSubmitted, this.keyboardType});

  final AppSetting setting;
  final ValueChanged<String> onSubmitted;
  final TextInputType? keyboardType;

  @override
  State<_TextSetting> createState() => _TextSettingState();
}

class _TextSettingState extends State<_TextSetting> {
  late final TextEditingController _controller = TextEditingController(text: _value);
  final FocusNode _focus = FocusNode();

  String get _value => widget.setting.currentValue?.toString() ?? '';

  @override
  void didUpdateWidget(_TextSetting old) {
    super.didUpdateWidget(old);
    if (!_focus.hasFocus && _controller.text != _value) _controller.text = _value;
  }

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.setting;
    return TextField(
      controller: _controller,
      focusNode: _focus,
      keyboardType: widget.keyboardType,
      decoration: InputDecoration(
        labelText: s.name,
        helperText: s.description.isEmpty ? null : s.description,
        border: const OutlineInputBorder(),
      ),
      onChanged: widget.onSubmitted,
    );
  }
}
