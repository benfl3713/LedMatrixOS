import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/providers.dart';
import 'color_picker.dart';
import 'named_colors.dart';
import 'syntax_codecs.dart';

/// Structured editors the server can suggest through a setting's `editor` hint. They read and write the setting's existing string format.
const structuredEditors = {'ha_entities', 'bins', 'reminders'};

/// Builds the structured editor for [setting] (a string setting whose `editor` is one of [structuredEditors]), or null for a plain text field.
Widget? buildStructuredEditor(AppSetting setting, {required String appId, required ValueChanged<Object> onChanged}) {
  switch (setting.editor) {
    case 'ha_entities':
      return HaEntitiesEditor(setting: setting, appId: appId, onChanged: onChanged);
    case 'bins':
      return BinsEditor(setting: setting, onChanged: onChanged);
    case 'reminders':
      return RemindersEditor(setting: setting, onChanged: onChanged);
  }
  return null;
}

// ---- generic list of rows ------------------------------------------------------------------------------------------------

/// A list of rows with add/remove/reorder that serialises to a string. A row that does not validate shows its message and nothing is
/// saved until every row is fine (the server would ignore the bad entry anyway).
class _RowsEditor<T> extends StatefulWidget {
  const _RowsEditor({
    required this.setting,
    required this.parse,
    required this.serialize,
    required this.validate,
    required this.rowBuilder,
    required this.addBuilder,
    required this.onChanged,
    this.onRowsReplaced,
    this.emptyText = 'Nothing added yet.',
  });

  final AppSetting setting;
  final List<T> Function(String) parse;
  final String Function(List<T>) serialize;
  final String? Function(T) validate;
  final Widget Function(BuildContext context, int index, T row, ValueChanged<T> change) rowBuilder;
  final Widget Function(BuildContext context, void Function(T row) add) addBuilder;
  final ValueChanged<Object> onChanged;
  final VoidCallback? onRowsReplaced;
  final String emptyText;

  @override
  State<_RowsEditor<T>> createState() => _RowsEditorState<T>();
}

class _RowsEditorState<T> extends State<_RowsEditor<T>> {
  late List<T> _rows;
  late List<Object> _ids;
  late String _known; // the last value seen from the settings, or the last one we sent
  int _next = 0;

  String get _current => widget.setting.currentValue?.toString() ?? '';

  @override
  void initState() {
    super.initState();
    _load(_current);
  }

  void _load(String value) {
    _known = value;
    _rows = widget.parse(value);
    _ids = [for (final _ in _rows) _newId()];
  }

  Object _newId() => ValueKey('row-${_next++}');

  @override
  void didUpdateWidget(_RowsEditor<T> old) {
    super.didUpdateWidget(old);
    // Something else changed the value (a reset, another phone): start over from it. Our own edits come back as _known.
    if (_current != _known) {
      _load(_current);
      widget.onRowsReplaced?.call();
    }
  }

  void _commit() {
    setState(() {});
    if (_rows.any((r) => widget.validate(r) != null)) return;
    final text = widget.serialize(_rows);
    _known = text;
    widget.onChanged(text);
  }

  void _change(int i, T row) {
    _rows[i] = row;
    _commit();
  }

  void _add(T row) {
    _rows.add(row);
    _ids.add(_newId());
    _commit();
  }

  void _remove(int i) {
    _rows.removeAt(i);
    _ids.removeAt(i);
    _commit();
  }

  void _move(int i, int by) {
    final j = i + by;
    if (j < 0 || j >= _rows.length) return;
    _rows.insert(j, _rows.removeAt(i));
    _ids.insert(j, _ids.removeAt(i));
    _commit();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.setting;
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(s.name, style: theme.textTheme.bodyLarge),
        if (s.description.isNotEmpty)
          Text(s.description, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        const SizedBox(height: 8),
        if (_rows.isEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(widget.emptyText, key: Key('rows-empty-${s.key}'), style: theme.textTheme.bodySmall),
          ),
        for (var i = 0; i < _rows.length; i++)
          KeyedSubtree(
            key: _ids[i] as Key,
            child: _RowCard(
              error: widget.validate(_rows[i]),
              index: i,
              isFirst: i == 0,
              isLast: i == _rows.length - 1,
              onRemove: () => _remove(i),
              onMove: (by) => _move(i, by),
              child: widget.rowBuilder(context, i, _rows[i], (row) => _change(i, row)),
            ),
          ),
        widget.addBuilder(context, _add),
      ],
    );
  }
}

class _RowCard extends StatelessWidget {
  const _RowCard({
    required this.child,
    required this.error,
    required this.index,
    required this.isFirst,
    required this.isLast,
    required this.onRemove,
    required this.onMove,
  });

  final Widget child;
  final String? error;
  final int index;
  final bool isFirst;
  final bool isLast;
  final VoidCallback onRemove;
  final ValueChanged<int> onMove;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  child,
                  if (error != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 6),
                      child: Text(error!, key: Key('row-error-$index'), style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error)),
                    ),
                ],
              ),
            ),
            Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                IconButton(
                    key: Key('row-up-$index'),
                    visualDensity: VisualDensity.compact,
                    tooltip: 'Move up',
                    icon: const Icon(Icons.arrow_upward_rounded),
                    onPressed: isFirst ? null : () => onMove(-1)),
                IconButton(
                    key: Key('row-down-$index'),
                    visualDensity: VisualDensity.compact,
                    tooltip: 'Move down',
                    icon: const Icon(Icons.arrow_downward_rounded),
                    onPressed: isLast ? null : () => onMove(1)),
                IconButton(
                    key: Key('row-remove-$index'),
                    visualDensity: VisualDensity.compact,
                    tooltip: 'Remove',
                    icon: const Icon(Icons.delete_outline_rounded),
                    onPressed: onRemove),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// A text field that follows [value] while it is not being edited and reports every change.
class _SyncedField extends StatefulWidget {
  const _SyncedField({super.key, required this.value, required this.label, required this.onChanged, this.hint, this.suffix, this.keyboardType});

  final String value;
  final String label;
  final String? hint;
  final Widget? suffix;
  final TextInputType? keyboardType;
  final ValueChanged<String> onChanged;

  @override
  State<_SyncedField> createState() => _SyncedFieldState();
}

class _SyncedFieldState extends State<_SyncedField> {
  late final TextEditingController _controller = TextEditingController(text: widget.value);
  final FocusNode _focus = FocusNode();

  @override
  void didUpdateWidget(_SyncedField old) {
    super.didUpdateWidget(old);
    if (!_focus.hasFocus && _controller.text != widget.value) _controller.text = widget.value;
  }

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextField(
        controller: _controller,
        focusNode: _focus,
        keyboardType: widget.keyboardType,
        decoration: InputDecoration(
          labelText: widget.label,
          hintText: widget.hint,
          isDense: true,
          suffixIcon: widget.suffix,
          border: const OutlineInputBorder(),
        ),
        onChanged: widget.onChanged,
      );
}

Widget _addButton(String key, String label, VoidCallback onPressed) => Align(
      alignment: Alignment.centerLeft,
      child: OutlinedButton.icon(key: Key(key), onPressed: onPressed, icon: const Icon(Icons.add_rounded), label: Text(label)),
    );

// ---- Home Assistant entities ---------------------------------------------------------------------------------------------

class HaEntitiesEditor extends StatefulWidget {
  const HaEntitiesEditor({super.key, required this.setting, required this.appId, required this.onChanged});

  final AppSetting setting;
  final String appId;
  final ValueChanged<Object> onChanged;

  @override
  State<HaEntitiesEditor> createState() => _HaEntitiesEditorState();
}

class _HaEntitiesEditorState extends State<HaEntitiesEditor> {
  // Friendly names of entities picked in this session (the setting itself only holds ids)
  final Map<String, String> _names = {};

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return _RowsEditor<HaEntityRow>(
      setting: widget.setting,
      parse: parseHaEntities,
      serialize: serializeHaEntities,
      validate: (_) => null,
      onChanged: widget.onChanged,
      emptyText: 'No entities yet. Search below to add one.',
      rowBuilder: (context, i, row, change) {
        final name = _names[row.id];
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(name ?? row.id, key: Key('ha-title-$i'), style: theme.textTheme.titleSmall),
            if (name != null && name != row.id) Text(row.id, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
            const SizedBox(height: 8),
            _SyncedField(key: Key('ha-label-$i'), value: row.label, label: 'Label', hint: name ?? 'Shown on the tile', onChanged: (v) => change(row.copyWith(label: v))),
            const SizedBox(height: 4),
            Wrap(
              spacing: 8,
              children: [
                FilterChip(key: Key('ha-icon-$i'), label: const Text('Icon'), selected: row.icon, onSelected: (v) => change(row.copyWith(icon: v))),
                FilterChip(key: Key('ha-spark-$i'), label: const Text('Sparkline'), selected: row.spark, onSelected: (v) => change(row.copyWith(spark: v))),
              ],
            ),
          ],
        );
      },
      addBuilder: (context, add) => _EntityPicker(
        appId: widget.appId,
        settingKey: widget.setting.key,
        exclude: {for (final r in parseHaEntities(widget.setting.currentValue?.toString())) r.id},
        onPick: (o) {
          _names[o.value] = o.label;
          add(HaEntityRow(id: o.value));
        },
      ),
    );
  }
}

/// Typeahead over the entity options endpoint (Browse: the list shows straight away, typing narrows it).
class _EntityPicker extends ConsumerStatefulWidget {
  const _EntityPicker({required this.appId, required this.settingKey, required this.exclude, required this.onPick});

  final String appId;
  final String settingKey;
  final Set<String> exclude;
  final ValueChanged<SettingOption> onPick;

  static const debounce = Duration(milliseconds: 300);

  @override
  ConsumerState<_EntityPicker> createState() => _EntityPickerState();
}

class _EntityPickerState extends ConsumerState<_EntityPicker> {
  final _controller = TextEditingController();
  Timer? _timer;
  int _seq = 0;
  List<SettingOption> _results = const [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _run('', ++_seq);
  }

  @override
  void dispose() {
    _timer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _onText(String text) {
    _timer?.cancel();
    final seq = ++_seq;
    setState(() => _loading = true);
    _timer = Timer(_EntityPicker.debounce, () => _run(text.trim(), seq));
  }

  Future<void> _run(String q, int seq) async {
    final result = await ref.read(apiProvider).getSettingOptions(widget.appId, widget.settingKey, q);
    if (!mounted || seq != _seq) return;
    result.when(
      ok: (list) => setState(() {
        _results = list;
        _loading = false;
      }),
      err: (e) {
        setState(() => _loading = false);
        ref.read(errorBusProvider.notifier).report(e);
      },
    );
  }

  void _pick(SettingOption o) {
    widget.onPick(o);
    _timer?.cancel();
    _controller.clear();
    _run('', ++_seq);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final shown = [for (final o in _results) if (o.value.isEmpty || !widget.exclude.contains(o.value)) o];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        TextField(
          key: const Key('ha-entity-search'),
          controller: _controller,
          onChanged: _onText,
          decoration: InputDecoration(
            labelText: 'Add entity...',
            prefixIcon: const Icon(Icons.search_rounded),
            suffixIcon: _loading
                ? const Padding(padding: EdgeInsets.all(12), child: SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2)))
                : null,
            border: const OutlineInputBorder(),
          ),
        ),
        for (final o in shown)
          if (o.value.isEmpty)
            // Not a pick: Home Assistant is not set up or cannot be reached
            ListTile(
              key: const Key('ha-option-info'),
              dense: true,
              contentPadding: EdgeInsets.zero,
              leading: Icon(Icons.info_outline_rounded, color: theme.colorScheme.error),
              title: Text(o.label),
              subtitle: o.subtitle == null ? null : Text(o.subtitle!),
            )
          else
            ListTile(
              key: Key('ha-option-${o.value}'),
              dense: true,
              contentPadding: EdgeInsets.zero,
              title: Text(o.label),
              subtitle: o.subtitle == null || o.subtitle == o.label ? null : Text(o.subtitle!),
              onTap: () => _pick(o),
            ),
        if (!_loading && shown.isEmpty)
          Padding(padding: const EdgeInsets.only(top: 8), child: Text('No matching entities.', style: theme.textTheme.bodySmall)),
      ],
    );
  }
}

// ---- Bin Day bins -----------------------------------------------------------------------------------------------------------

class BinsEditor extends StatelessWidget {
  const BinsEditor({super.key, required this.setting, required this.onChanged});

  final AppSetting setting;
  final ValueChanged<Object> onChanged;

  @override
  Widget build(BuildContext context) {
    return _RowsEditor<BinRow>(
      setting: setting,
      parse: parseBins,
      serialize: serializeBins,
      validate: validateBin,
      onChanged: onChanged,
      emptyText: 'No bins yet.',
      rowBuilder: (context, i, row, change) => _BinRowForm(index: i, row: row, change: change),
      addBuilder: (context, add) =>
          _addButton('add-bin', 'Add bin', () => add(const BinRow(colour: '#808080', day: 'Mon'))),
    );
  }
}

class _BinRowForm extends StatelessWidget {
  const _BinRowForm({required this.index, required this.row, required this.change});

  final int index;
  final BinRow row;
  final ValueChanged<BinRow> change;

  @override
  Widget build(BuildContext context) {
    final i = index;
    final day = parseWeekday(row.day);
    final every = int.tryParse(row.every);
    final current = parseHexColor(row.colour);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _SyncedField(key: Key('bin-name-$i'), value: row.name, label: 'Bin name', hint: 'e.g. Recycling', onChanged: (v) => change(row.copyWith(name: v))),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            for (final e in namedColors.entries)
              _BinSwatch(
                key: Key('bin-colour-$i-${e.key}'),
                color: e.value,
                selected: current != null && current.toARGB32() == e.value.toARGB32(),
                onTap: () => change(row.copyWith(colour: colorToHex(e.value))),
              ),
            IconButton(
              key: Key('bin-colour-custom-$i'),
              tooltip: 'Custom colour',
              icon: const Icon(Icons.palette_outlined),
              onPressed: () async {
                final picked = await showColorPicker(context, initial: current ?? Colors.grey, title: 'Bin colour');
                if (picked != null) change(row.copyWith(colour: colorToHex(picked)));
              },
            ),
          ],
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: DropdownButtonFormField<String>(
                key: ValueKey('bin-day-$i-${row.day}'),
                initialValue: day == null ? null : weekdayNames[day],
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Day', isDense: true, border: OutlineInputBorder()),
                items: [for (final d in weekdayNames) DropdownMenuItem(value: d, child: Text(d))],
                onChanged: (v) {
                  if (v != null) change(row.copyWith(day: v));
                },
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: DropdownButtonFormField<String>(
                key: ValueKey('bin-every-$i-${row.every}'),
                initialValue: every != null && every >= 1 && every <= 4 ? '$every' : null,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Every', isDense: true, border: OutlineInputBorder()),
                items: [for (final n in [1, 2, 3, 4]) DropdownMenuItem(value: '$n', child: Text(n == 1 ? 'week' : '$n weeks'))],
                onChanged: (v) {
                  if (v != null) change(row.copyWith(every: v));
                },
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        _SyncedField(
          key: Key('bin-anchor-$i'),
          value: row.anchor,
          label: 'A known collection date',
          hint: 'yyyy-MM-dd (needed for every 2+ weeks)',
          keyboardType: TextInputType.datetime,
          onChanged: (v) => change(row.copyWith(anchor: v.trim())),
          suffix: IconButton(
            key: Key('bin-anchor-pick-$i'),
            icon: const Icon(Icons.calendar_today_rounded),
            onPressed: () async {
              final picked = await showDatePicker(
                context: context,
                initialDate: parseIsoDate(row.anchor) ?? DateTime.now(),
                firstDate: DateTime(2020),
                lastDate: DateTime(2100),
              );
              if (picked != null) change(row.copyWith(anchor: formatIsoDate(picked)));
            },
          ),
        ),
        const SizedBox(height: 8),
        _SyncedField(
          key: Key('bin-skips-$i'),
          value: row.skips,
          label: 'Skip dates (optional)',
          hint: 'e.g. 2026-12-28,2027-01-04',
          onChanged: (v) => change(row.copyWith(skips: v)),
        ),
      ],
    );
  }
}

class _BinSwatch extends StatelessWidget {
  const _BinSwatch({super.key, required this.color, required this.selected, required this.onTap});

  final Color color;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return InkResponse(
      onTap: onTap,
      radius: 24,
      child: Container(
        width: 36,
        height: 36,
        decoration: BoxDecoration(
          color: color,
          shape: BoxShape.circle,
          border: Border.all(color: selected ? theme.colorScheme.primary : theme.colorScheme.outlineVariant, width: selected ? 3 : 1),
        ),
        child: selected
            ? Icon(Icons.check_rounded, size: 18, color: ThemeData.estimateBrightnessForColor(color) == Brightness.dark ? Colors.white : Colors.black)
            : null,
      ),
    );
  }
}

// ---- Bin Day reminders ----------------------------------------------------------------------------------------------------

class RemindersEditor extends StatelessWidget {
  const RemindersEditor({super.key, required this.setting, required this.onChanged});

  final AppSetting setting;
  final ValueChanged<Object> onChanged;

  @override
  Widget build(BuildContext context) {
    return _RowsEditor<ReminderRow>(
      setting: setting,
      parse: parseReminders,
      serialize: serializeReminders,
      validate: validateReminder,
      onChanged: onChanged,
      emptyText: 'No reminders yet.',
      rowBuilder: (context, i, row, change) => _ReminderRowForm(index: i, row: row, change: change),
      addBuilder: (context, add) => _addButton('add-reminder', 'Add reminder', () => add(const ReminderRow(start: '08:00', end: '20:00'))),
    );
  }
}

class _ReminderRowForm extends StatelessWidget {
  const _ReminderRowForm({required this.index, required this.row, required this.change});

  final int index;
  final ReminderRow row;
  final ValueChanged<ReminderRow> change;

  Widget _time(BuildContext context, String key, String label, String value, ValueChanged<String> set) {
    return Expanded(
      child: _SyncedField(
        key: Key(key),
        value: value,
        label: label,
        hint: 'HH:mm',
        keyboardType: TextInputType.datetime,
        onChanged: (v) => set(v.trim()),
        suffix: IconButton(
          key: Key('$key-pick'),
          icon: const Icon(Icons.access_time_rounded),
          onPressed: () async {
            final minutes = parseClock(value) ?? 0;
            final picked = await showTimePicker(context: context, initialTime: TimeOfDay(hour: minutes ~/ 60, minute: minutes % 60));
            if (picked != null) set(formatClock(picked.hour, picked.minute));
          },
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final i = index;
    final selected = row.daySet;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _SyncedField(key: Key('rem-text-$i'), value: row.text, label: 'Text', hint: 'e.g. Take pills', onChanged: (v) => change(row.copyWith(text: v))),
        const SizedBox(height: 8),
        Row(
          children: [
            _time(context, 'rem-start-$i', 'From', row.start, (v) => change(row.copyWith(start: v))),
            const SizedBox(width: 8),
            _time(context, 'rem-end-$i', 'Until', row.end, (v) => change(row.copyWith(end: v))),
          ],
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 6,
          children: [
            for (var d = 0; d < 7; d++)
              FilterChip(
                key: Key('rem-day-$i-${weekdayNames[d]}'),
                label: Text(weekdayNames[d]),
                selected: selected.contains(d),
                onSelected: (on) {
                  final next = {...selected};
                  on ? next.add(d) : next.remove(d);
                  if (next.isEmpty) return; // at least one day
                  change(row.withDays(next));
                },
              ),
          ],
        ),
      ],
    );
  }
}
