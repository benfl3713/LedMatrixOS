import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/app_icons.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';
import 'app_settings_provider.dart';
import 'color_picker.dart';
import 'named_colors.dart';
import 'search_setting.dart';
import 'select_settings.dart';

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
    final status = ref.watch(settingsSaveStatusProvider(app.id));
    final anyModified = settings.value?.any((s) => s.isModified) ?? false;
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
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(child: _SaveIndicator(status: status, onRetry: () => ref.read(appSettingsProvider(app.id).notifier).retry())),
            if (anyModified)
              TextButton.icon(
                key: const Key('reset-all'),
                onPressed: () => _confirmResetAll(context, ref),
                icon: const Icon(Icons.restart_alt_rounded),
                label: const Text('Reset all'),
              ),
          ],
        ),
        const SizedBox(height: 8),
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
              : _SettingsBody(app: app, list: list, errors: status.fieldErrors),
        ),
      ],
    );
  }
}

extension on AppSettingsView {
  Future<void> _confirmResetAll(BuildContext context, WidgetRef ref) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Reset all settings?'),
        content: Text('Every ${app.name} setting goes back to its default.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(key: const Key('reset-all-confirm'), onPressed: () => Navigator.pop(context, true), child: const Text('Reset')),
        ],
      ),
    );
    if (ok != true) return;
    ref.read(appSettingsProvider(app.id).notifier).resetAll();
  }
}

/// "Saving...", "Saved" or "Could not save" with a retry, driven by the settings notifier.
class _SaveIndicator extends StatelessWidget {
  const _SaveIndicator({required this.status, required this.onRetry});

  final SaveStatus status;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    switch (status.state) {
      case SaveState.idle:
        return const SizedBox(key: Key('save-idle'), height: 40);
      case SaveState.saving:
        return const SizedBox(
          height: 40,
          child: Row(children: [
            SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)),
            SizedBox(width: 8),
            Text('Saving...', key: Key('save-saving')),
          ]),
        );
      case SaveState.saved:
        return SizedBox(
          height: 40,
          child: Row(children: [
            Icon(Icons.check_circle_outline_rounded, size: 18, color: theme.colorScheme.primary),
            const SizedBox(width: 8),
            const Text('Saved', key: Key('save-saved')),
          ]),
        );
      case SaveState.failed:
        return SizedBox(
          height: 40,
          child: Row(children: [
            Icon(Icons.error_outline_rounded, size: 18, color: theme.colorScheme.error),
            const SizedBox(width: 8),
            Flexible(
              child: Text(status.message == null ? 'Could not save' : 'Could not save: ${status.message}',
                  key: const Key('save-failed'), maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: theme.colorScheme.error)),
            ),
            TextButton(key: const Key('save-retry'), onPressed: onRetry, child: const Text('Retry')),
          ]),
        );
    }
  }
}

class _SettingsBody extends ConsumerWidget {
  const _SettingsBody({required this.app, required this.list, required this.errors});

  final MatrixApp app;
  final List<AppSetting> list;
  final Map<String, String> errors;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(appSettingsProvider(app.id).notifier);

    // Routes belong to one station: a new (or reset) station starts with none picked
    void clearDependents(String key) {
      if (key != 'stationId') return;
      for (final o in list) {
        if (o.browse && (o.currentValue?.toString() ?? '').isNotEmpty) notifier.edit(o.key, '', labels: const []);
      }
    }

    Widget tile(AppSetting s) => Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: SettingTile(
                      key: ValueKey(s.key),
                      setting: s,
                      appId: app.id,
                      // Options may depend on the other settings (the routes of the chosen station)
                      optionsContext: s.type == AppSettingType.search || s.type == AppSettingType.multiSearch
                          ? {for (final o in list) if (o.key != s.key) o.key: o.currentValue?.toString() ?? ''}
                          : const {},
                      onChanged: (v) => notifier.edit(s.key, v),
                      onSearchChanged: (v, {label, labels}) {
                        notifier.edit(s.key, v, label: label, labels: labels);
                        clearDependents(s.key);
                      },
                    ),
                  ),
                  if (s.isModified)
                    IconButton(
                      key: Key('reset-${s.key}'),
                      tooltip: 'Reset to default',
                      icon: const Icon(Icons.restart_alt_rounded),
                      onPressed: () {
                        notifier.reset(s.key);
                        clearDependents(s.key);
                      },
                    ),
                ],
              ),
              if (errors[s.key] != null)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(errors[s.key]!,
                      key: Key('error-${s.key}'),
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(color: Theme.of(context).colorScheme.error)),
                ),
            ],
          ),
        );

    final basic = [for (final s in list) if (!s.advanced) s];
    final advanced = [for (final s in list) if (s.advanced) s];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final s in basic) tile(s),
        if (advanced.isNotEmpty)
          _AdvancedSection(
            // Open from the start when something in there is not at its default
            initiallyExpanded: advanced.any((s) => s.isModified),
            children: [for (final s in advanced) tile(s)],
          ),
      ],
    );
  }
}

class _AdvancedSection extends StatelessWidget {
  const _AdvancedSection({required this.initiallyExpanded, required this.children});

  final bool initiallyExpanded;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return ExpansionTile(
      key: const Key('advanced-section'),
      initiallyExpanded: initiallyExpanded,
      tilePadding: EdgeInsets.zero,
      childrenPadding: const EdgeInsets.only(top: 4),
      shape: const Border(),
      collapsedShape: const Border(),
      title: const Text('Advanced'),
      children: children,
    );
  }
}

/// One editor for one setting, chosen by its type.
class SettingTile extends StatelessWidget {
  const SettingTile({super.key, required this.setting, required this.onChanged, this.appId, this.onSearchChanged, this.optionsContext = const {}});

  final AppSetting setting;
  final ValueChanged<Object> onChanged;

  /// Current values of the app's other settings, sent with option requests of Search/MultiSearch settings.
  final Map<String, String> optionsContext;

  /// Needed for Search/MultiSearch settings, which query the options endpoint.
  final String? appId;
  final SearchChanged? onSearchChanged;

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
          validate: (text) {
            final n = int.tryParse(text.trim());
            if (n == null) return 'Enter a whole number';
            if (s.minValue != null && n < s.minValue!) return 'Must be at least ${s.minValue!.round()}';
            if (s.maxValue != null && n > s.maxValue!) return 'Must be at most ${s.maxValue!.round()}';
            return null;
          },
          onSubmitted: (text) {
            final n = int.tryParse(text.trim());
            if (n != null) onChanged(n);
          },
        );

      case AppSettingType.string:
        return _TextSetting(setting: s, onSubmitted: onChanged);

      case AppSettingType.select:
        final options = s.options ?? const <String>[];
        final current = s.currentValue?.toString();
        if (options.isEmpty) return _TextSetting(setting: s, onSubmitted: onChanged);
        if (looksLikeColourSelect(options)) return ColourSelect(setting: s, options: options, onChanged: onChanged);
        if (options.length > searchableSelectThreshold) return SearchableSelect(setting: s, options: options, onChanged: onChanged);
        return DropdownButtonFormField<String>(
          // initialValue is only read on creation; re-key so changed options/values rebuild it.
          key: ValueKey('${s.key}|$current|${options.join(',')}'),
          initialValue: options.contains(current) ? current : null,
          isExpanded: true,
          decoration: InputDecoration(labelText: s.name, helperText: s.description.isEmpty ? null : s.description, helperMaxLines: 6, border: const OutlineInputBorder()),
          items: [for (final o in options) DropdownMenuItem(value: o, child: Text(o))],
          onChanged: (v) {
            if (v != null) onChanged(v);
          },
        );

      case AppSettingType.search:
      case AppSettingType.multiSearch:
        if (appId == null) return _TextSetting(setting: s, onSubmitted: onChanged);
        return SearchSetting(
          setting: s,
          appId: appId!,
          context: optionsContext,
          onChanged: onSearchChanged ?? (v, {label, labels}) => onChanged(v),
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
  const _TextSetting({required this.setting, required this.onSubmitted, this.keyboardType, this.validate});

  final AppSetting setting;
  final ValueChanged<String> onSubmitted;
  final TextInputType? keyboardType;

  /// Returns a message for text that must not be saved (shown under the field), or null when it is fine.
  final String? Function(String text)? validate;

  @override
  State<_TextSetting> createState() => _TextSettingState();
}

class _TextSettingState extends State<_TextSetting> {
  late final TextEditingController _controller = TextEditingController(text: _value);
  final FocusNode _focus = FocusNode();
  String? _error;

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
        helperMaxLines: 6,
        errorText: _error,
        border: const OutlineInputBorder(),
      ),
      onChanged: (text) {
        final message = widget.validate?.call(text);
        setState(() => _error = message);
        if (message == null) widget.onSubmitted(text);
      },
    );
  }
}
