import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/schedule_models.dart';
import '../../core/providers.dart';

class RuleEditResult {
  const RuleEditResult.apply(this.rule, {this.singleAppId}) : delete = false;
  const RuleEditResult.delete()
      : rule = null,
        singleAppId = null,
        delete = true;

  final RuleDoc? rule;
  final bool delete;

  /// Set when the rule runs a single app: a one-entry playlist named after the app should exist.
  final String? singleAppId;
}

Future<RuleEditResult?> showRuleEditor(
  BuildContext context, {
  required RuleDoc rule,
  required bool isNew,
  required List<PlaylistDoc> playlists,
}) =>
    showModalBottomSheet<RuleEditResult>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      showDragHandle: true,
      builder: (_) => RuleEditor(initial: rule, isNew: isNew, playlists: playlists),
    );

const _weekOrder = [1, 2, 3, 4, 5, 6, 0];
const _dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

class RuleEditor extends ConsumerStatefulWidget {
  const RuleEditor({super.key, required this.initial, required this.isNew, required this.playlists});

  final RuleDoc initial;
  final bool isNew;
  final List<PlaylistDoc> playlists;

  @override
  ConsumerState<RuleEditor> createState() => _RuleEditorState();
}

class _RuleEditorState extends ConsumerState<RuleEditor> {
  late RuleDoc _rule = widget.initial;
  String? _singleApp;
  late final TextEditingController _condition = TextEditingController(text: widget.initial.condition ?? '');

  @override
  void dispose() {
    _condition.dispose();
    super.dispose();
  }

  Future<void> _pickTime({required bool start}) async {
    final current = parseHm(start ? _rule.startTime : _rule.endTime) ?? (start ? 0 : 0);
    final picked = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(hour: current ~/ 60, minute: current % 60),
      builder: (context, child) =>
          MediaQuery(data: MediaQuery.of(context).copyWith(alwaysUse24HourFormat: true), child: child!),
    );
    if (picked == null || !mounted) return;
    final text = formatHm(picked.hour * 60 + picked.minute);
    setState(() => _rule = start ? _rule.copyWith(startTime: text) : _rule.copyWith(endTime: text));
  }

  void _apply() {
    final condition = _condition.text.trim();
    var rule = condition.isEmpty ? _rule.copyWith(clearCondition: true) : _rule.copyWith(condition: condition);
    if (_singleApp != null) rule = rule.copyWith(playlistId: _singleApp);
    Navigator.pop(context, RuleEditResult.apply(rule, singleAppId: _singleApp));
  }

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    final scheme = Theme.of(context).colorScheme;
    final apps = ref.watch(appListProvider).value?.apps ?? const <MatrixApp>[];
    final names = [for (final p in widget.playlists) p.name];
    final playlistValue = _singleApp != null ? null : (_rule.playlistId.isEmpty ? null : _rule.playlistId);
    final playlistItems = {...names, if (playlistValue != null) playlistValue}.toList();
    final brightness = _rule.brightnessOverride;

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(widget.isNew ? 'New rule' : 'Edit rule', style: text.titleLarge),
            const SizedBox(height: 16),
            DropdownButtonFormField<String>(
              key: const Key('rule-playlist'),
              initialValue: playlistValue,
              decoration: const InputDecoration(labelText: 'Playlist', border: OutlineInputBorder()),
              items: [for (final n in playlistItems) DropdownMenuItem(value: n, child: Text(n))],
              onChanged: (v) => setState(() {
                _singleApp = null;
                if (v != null) _rule = _rule.copyWith(playlistId: v);
              }),
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              key: const Key('rule-single-app'),
              initialValue: _singleApp,
              decoration: const InputDecoration(
                labelText: 'Or run a single app',
                helperText: 'Creates a one-entry playlist named after the app.',
                border: OutlineInputBorder(),
              ),
              items: [for (final a in apps) DropdownMenuItem(value: a.id, child: Text(a.name))],
              onChanged: (v) => setState(() => _singleApp = v),
            ),
            const SizedBox(height: 16),
            Text('Days', style: text.labelLarge),
            const SizedBox(height: 4),
            Wrap(
              spacing: 6,
              children: [
                for (final d in _weekOrder)
                  FilterChip(
                    key: Key('day-chip-$d'),
                    label: Text(_dayNames[d]),
                    selected: _rule.appliesOn(d),
                    onSelected: (on) => setState(() {
                      final mask = on ? _rule.daysMask | (1 << d) : _rule.daysMask & ~(1 << d);
                      _rule = _rule.copyWith(daysMask: mask);
                    }),
                  ),
              ],
            ),
            if (_rule.daysMask == 0)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('Pick at least one day.', style: TextStyle(color: scheme.error)),
              ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _TimeButton(
                    key: const Key('rule-start'),
                    label: 'Starts',
                    value: _rule.startTime ?? 'Midnight',
                    onTap: () => _pickTime(start: true),
                    onClear: _rule.startTime == null ? null : () => setState(() => _rule = _rule.copyWith(clearStart: true)),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _TimeButton(
                    key: const Key('rule-end'),
                    label: 'Ends',
                    value: _rule.endTime ?? 'End of day',
                    onTap: () => _pickTime(start: false),
                    onClear: _rule.endTime == null ? null : () => setState(() => _rule = _rule.copyWith(clearEnd: true)),
                  ),
                ),
              ],
            ),
            if (_rule.wraps)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('Runs past midnight into the next day.', style: text.bodySmall?.copyWith(color: scheme.primary)),
              ),
            const SizedBox(height: 16),
            Text('Priority: ${_rule.priority}', style: text.labelLarge),
            Slider(
              key: const Key('rule-priority'),
              value: _rule.priority.clamp(0, 100).toDouble(),
              min: 0,
              max: 100,
              divisions: 100,
              label: '${_rule.priority}',
              onChanged: (v) => setState(() => _rule = _rule.copyWith(priority: v.round())),
            ),
            SwitchListTile(
              key: const Key('rule-brightness-switch'),
              contentPadding: EdgeInsets.zero,
              title: Text(brightness == null ? 'Brightness override' : 'Brightness override: ${brightnessToPercent(brightness)}%'),
              value: brightness != null,
              onChanged: (on) => setState(() => _rule = on ? _rule.copyWith(brightnessOverride: 128) : _rule.copyWith(clearBrightness: true)),
            ),
            if (brightness != null)
              Slider(
                key: const Key('rule-brightness'),
                value: brightnessToPercent(brightness).toDouble(),
                min: 0,
                max: 100,
                divisions: 100,
                label: '${brightnessToPercent(brightness)}%',
                onChanged: (v) => setState(() => _rule = _rule.copyWith(brightnessOverride: percentToBrightness(v.round()))),
              ),
            const SizedBox(height: 8),
            TextField(
              key: const Key('rule-condition'),
              controller: _condition,
              decoration: const InputDecoration(
                labelText: 'Condition (optional)',
                helperText: 'Supported: $conditionHint',
                helperMaxLines: 3,
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              children: [
                for (final sample in ['spotify_playing', 'line_disrupted:', 'bus_due:', 'ha_state:', 'bin_day', 'road_disrupted:any'])
                  ActionChip(
                    label: Text(sample),
                    onPressed: () => setState(() {
                      _condition.text = sample;
                      _condition.selection = TextSelection.collapsed(offset: sample.length);
                    }),
                  ),
              ],
            ),
            const SizedBox(height: 20),
            Row(
              children: [
                if (!widget.isNew)
                  TextButton.icon(
                    key: const Key('rule-delete'),
                    onPressed: () => Navigator.pop(context, const RuleEditResult.delete()),
                    icon: const Icon(Icons.delete_outline_rounded),
                    label: const Text('Delete'),
                    style: TextButton.styleFrom(foregroundColor: scheme.error),
                  ),
                const Spacer(),
                TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
                const SizedBox(width: 8),
                FilledButton(
                  key: const Key('rule-apply'),
                  onPressed: _rule.daysMask == 0 || (_rule.playlistId.isEmpty && _singleApp == null) ? null : _apply,
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

class _TimeButton extends StatelessWidget {
  const _TimeButton({super.key, required this.label, required this.value, required this.onTap, this.onClear});

  final String label;
  final String value;
  final VoidCallback onTap;
  final VoidCallback? onClear;

  @override
  Widget build(BuildContext context) => OutlinedButton(
        onPressed: onTap,
        style: OutlinedButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14)),
        child: Row(
          children: [
            Expanded(child: Text('$label $value', overflow: TextOverflow.ellipsis)),
            if (onClear != null)
              InkResponse(onTap: onClear, child: const Icon(Icons.close_rounded, size: 18)),
          ],
        ),
      );
}
