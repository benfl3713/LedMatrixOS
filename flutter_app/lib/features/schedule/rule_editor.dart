import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/schedule_models.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import '../apps/app_picker.dart';
import 'editor_scaffold.dart';
import 'playlist_editor.dart';

class RuleEditResult {
  const RuleEditResult.apply(this.rule, {this.singleAppId, this.createdPlaylist}) : delete = false;
  const RuleEditResult.delete()
      : rule = null,
        singleAppId = null,
        createdPlaylist = null,
        delete = true;

  final RuleDoc? rule;
  final bool delete;

  /// Set when the rule runs a single app: a one-entry playlist named after the app should exist.
  final String? singleAppId;

  /// A playlist made from inside the editor ("Create a playlist"); the schedule should add it before the rule.
  final PlaylistDoc? createdPlaylist;
}

enum _Mode { app, playlist }

/// Priority levels offered; a rule saved with another value keeps it as a "Custom" choice.
const _priorityLevels = <(String, int)>[('Low', 25), ('Normal', 50), ('High', 75), ('Top', 100)];

Future<RuleEditResult?> showRuleEditor(
  BuildContext context, {
  required RuleDoc rule,
  required bool isNew,
  required List<PlaylistDoc> playlists,
}) =>
    pushEditor<RuleEditResult>(
      context,
      RuleEditor(initial: rule, isNew: isNew, playlists: playlists),
    );

const _weekOrder = [1, 2, 3, 4, 5, 6, 0];
const _dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const _dayPresets = <(String, int)>[
  ('Every day', allDaysMask),
  ('Weekdays', 62),
  ('Weekends', 65),
];

class RuleEditor extends ConsumerStatefulWidget {
  const RuleEditor({
    super.key,
    required this.initial,
    required this.isNew,
    required this.playlists,
  });

  final RuleDoc initial;
  final bool isNew;
  final List<PlaylistDoc> playlists;

  @override
  ConsumerState<RuleEditor> createState() => _RuleEditorState();
}

class _RuleEditorState extends ConsumerState<RuleEditor> {
  late RuleDoc _rule = widget.initial;
  String? _singleApp;
  bool _touched = false;
  bool _whatTouched = false;
  PlaylistDoc? _created;
  late _Mode _mode = widget.initial.playlistId.isNotEmpty || widget.playlists.isNotEmpty ? _Mode.playlist : _Mode.app;

  /// An existing priority that is not one of the four levels, kept until the user picks another.
  late final int? _customPriority = _priorityLevels.any((l) => l.$2 == widget.initial.priority) ? null : widget.initial.priority;

  List<PlaylistDoc> get _playlists => [...widget.playlists, if (_created != null) _created!];

  Future<void> _createPlaylist() async {
    final result = await showPlaylistEditor(
      context,
      playlist: const PlaylistDoc(name: ''),
      isNew: true,
      otherNames: [for (final p in _playlists) p.name],
    );
    final created = result?.playlist;
    if (created == null || !mounted) return;
    _change(() {
      _whatTouched = true;
      _created = created;
      _rule = _rule.copyWith(playlistId: created.name);
    });
  }
  late final TextEditingController _condition = TextEditingController(
    text: widget.initial.condition ?? '',
  );

  @override
  void dispose() {
    _condition.dispose();
    super.dispose();
  }

  void _change(VoidCallback fn) => setState(() {
        _touched = true;
        fn();
      });

  Future<void> _pickTime({required bool start}) async {
    final current = parseHm(start ? _rule.startTime : _rule.endTime) ?? 0;
    final picked = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(hour: current ~/ 60, minute: current % 60),
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(alwaysUse24HourFormat: true),
        child: child!,
      ),
    );
    if (picked == null || !mounted) return;
    final text = formatHm(picked.hour * 60 + picked.minute);
    _change(
      () => _rule = start ? _rule.copyWith(startTime: text) : _rule.copyWith(endTime: text),
    );
  }

  void _apply() {
    final condition = _condition.text.trim();
    var rule = condition.isEmpty ? _rule.copyWith(clearCondition: true) : _rule.copyWith(condition: condition);
    final single = _mode == _Mode.app ? _singleApp : null;
    if (single != null) rule = rule.copyWith(playlistId: single);
    final created = _mode == _Mode.playlist && _created != null && _created!.name == rule.playlistId ? _created : null;
    Navigator.pop(context, RuleEditResult.apply(rule, singleAppId: single, createdPlaylist: created));
  }

  Future<void> _delete() async {
    final ok = await confirmDestructive(
      context,
      title: 'Delete this rule?',
      message: 'It stops applying as soon as you save the schedule. You can still discard the change before saving.',
      confirmKey: const Key('rule-delete-confirm'),
    );
    if (ok && mounted) Navigator.pop(context, const RuleEditResult.delete());
  }

  /// Why the rule cannot be applied yet, or null when it is valid.
  String? get _problem {
    if (_mode == _Mode.app) {
      if (_singleApp == null) return 'Choose an app to show.';
    } else if (_rule.playlistId.isEmpty) {
      return _playlists.isEmpty ? 'Create a playlist to show.' : 'Choose a playlist to show.';
    }
    if (_rule.daysMask == 0) return 'Pick at least one day.';
    return null;
  }

  String get _summary {
    final days = _rule.daysMask == 0 ? 'No days' : _daysText(_rule.daysMask);
    final s = _rule.startTime, e = _rule.endTime;
    final when = _rule.isAllDay
        ? 'all day'
        : 'from ${s ?? 'midnight'} to ${e ?? 'end of day'}${_rule.wraps ? ' (into the next day)' : ''}';
    return '$days, $when';
  }

  static String _daysText(int mask) {
    if (mask == allDaysMask) return 'Every day';
    if (mask == 62) return 'Weekdays';
    if (mask == 65) return 'Weekends';
    return [
      for (final d in _weekOrder)
        if (mask & (1 << d) != 0) _dayNames[d],
    ].join(', ');
  }

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    final scheme = Theme.of(context).colorScheme;
    final names = [for (final p in _playlists) p.name];
    final playlistValue = _rule.playlistId.isEmpty ? null : _rule.playlistId;
    final brightness = _rule.brightnessOverride;
    final problem = _problem;

    return EditorScaffold(
      title: widget.isNew ? 'New rule' : 'Edit rule',
      dirty: _touched,
      applyKey: const Key('rule-apply'),
      onApply: problem == null ? _apply : null,
      problem: problem,
      problemIsError: _rule.daysMask == 0 || _whatTouched,
      onDelete: widget.isNew ? null : _delete,
      deleteKey: const Key('rule-delete'),
      children: [
        _SummaryBanner(text: _summary),
        EditorSection(
          title: 'What to show',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SegmentedButton<_Mode>(
                key: const Key('rule-mode'),
                showSelectedIcon: false,
                style: const ButtonStyle(minimumSize: WidgetStatePropertyAll(Size(0, 48))),
                segments: const [
                  ButtonSegment(value: _Mode.app, label: Text('One app'), icon: Icon(Icons.apps_rounded)),
                  ButtonSegment(value: _Mode.playlist, label: Text('Playlist'), icon: Icon(Icons.queue_music_rounded)),
                ],
                selected: {_mode},
                onSelectionChanged: (v) => _change(() {
                  _whatTouched = true;
                  _mode = v.first;
                }),
              ),
              const SizedBox(height: Space.md),
              if (_mode == _Mode.app) ...[
                AppPickerField(
                  key: const Key('rule-single-app'),
                  appId: _singleApp,
                  label: 'App',
                  title: 'Choose an app to show',
                  onChanged: (id) => _change(() {
                    _whatTouched = true;
                    _singleApp = id;
                  }),
                ),
                Padding(
                  padding: const EdgeInsets.only(top: Space.sm),
                  child: Text('Creates a one-entry playlist named after the app.',
                      style: text.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
                ),
              ] else if (names.isEmpty) ...[
                Text('You have no playlists yet. A playlist cycles through several apps.',
                    style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant)),
                const SizedBox(height: Space.sm),
                OutlinedButton.icon(
                  key: const Key('rule-create-playlist'),
                  style: OutlinedButton.styleFrom(minimumSize: const Size(0, 48)),
                  onPressed: _createPlaylist,
                  icon: const Icon(Icons.add_rounded),
                  label: const Text('Create a playlist'),
                ),
              ] else
                DropdownButtonFormField<String>(
                  key: const Key('rule-playlist'),
                  initialValue: playlistValue,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Playlist'),
                  items: [
                    for (final n in {...names, if (playlistValue != null) playlistValue})
                      DropdownMenuItem(value: n, child: Text(n)),
                  ],
                  onChanged: (v) => _change(() {
                    _whatTouched = true;
                    if (v != null) _rule = _rule.copyWith(playlistId: v);
                  }),
                ),
            ],
          ),
        ),
        EditorSection(
          title: 'When',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Wrap(
                spacing: 8,
                runSpacing: 4,
                children: [
                  for (final (label, mask) in _dayPresets)
                    ChoiceChip(
                      key: Key('day-preset-$mask'),
                      label: Text(label),
                      selected: _rule.daysMask == mask,
                      onSelected: (_) => _change(() => _rule = _rule.copyWith(daysMask: mask)),
                    ),
                ],
              ),
              const SizedBox(height: Space.md),
              Row(
                children: [
                  for (final d in _weekOrder)
                    Expanded(
                      child: _DayChip(
                        key: Key('day-chip-$d'),
                        label: _dayNames[d],
                        selected: _rule.appliesOn(d),
                        onChanged: (on) => _change(() {
                          final mask = on ? _rule.daysMask | (1 << d) : _rule.daysMask & ~(1 << d);
                          _rule = _rule.copyWith(daysMask: mask);
                        }),
                      ),
                    ),
                ],
              ),
              if (_rule.daysMask == 0)
                Padding(
                  padding: const EdgeInsets.only(top: Space.sm),
                  child: Text(
                    'Pick at least one day.',
                    style: TextStyle(color: scheme.error),
                  ),
                ),
              const SizedBox(height: Space.lg),
              Row(
                children: [
                  Expanded(
                    child: _TimeTile(
                      key: const Key('rule-start'),
                      label: 'Starts',
                      value: _rule.startTime ?? 'Midnight',
                      isSet: _rule.startTime != null,
                      onTap: () => _pickTime(start: true),
                      onClear: _rule.startTime == null
                          ? null
                          : () => _change(
                                () => _rule = _rule.copyWith(clearStart: true),
                              ),
                    ),
                  ),
                  const SizedBox(width: Space.md),
                  Expanded(
                    child: _TimeTile(
                      key: const Key('rule-end'),
                      label: 'Ends',
                      value: _rule.endTime ?? 'End of day',
                      isSet: _rule.endTime != null,
                      onTap: () => _pickTime(start: false),
                      onClear: _rule.endTime == null
                          ? null
                          : () => _change(
                                () => _rule = _rule.copyWith(clearEnd: true),
                              ),
                    ),
                  ),
                ],
              ),
              if (_rule.wraps)
                Padding(
                  padding: const EdgeInsets.only(top: Space.sm),
                  child: Text(
                    'Runs past midnight into the next day.',
                    style: text.bodySmall?.copyWith(color: scheme.primary),
                  ),
                ),
            ],
          ),
        ),
        EditorSection(
          title: 'Priority and brightness',
          subtitle: 'When two rules overlap, the higher priority wins.',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Priority', style: text.labelLarge),
              const SizedBox(height: Space.sm),
              SegmentedButton<int>(
                key: const Key('rule-priority'),
                showSelectedIcon: false,
                emptySelectionAllowed: true,
                style: const ButtonStyle(minimumSize: WidgetStatePropertyAll(Size(0, 48))),
                segments: [for (final (label, value) in _priorityLevels) ButtonSegment(value: value, label: Text(label))],
                selected: {if (_priorityLevels.any((l) => l.$2 == _rule.priority)) _rule.priority},
                onSelectionChanged: (v) {
                  if (v.isNotEmpty) _change(() => _rule = _rule.copyWith(priority: v.first));
                },
              ),
              if (_customPriority != null)
                Padding(
                  padding: const EdgeInsets.only(top: Space.sm),
                  child: ChoiceChip(
                    key: const Key('rule-priority-custom'),
                    label: Text('Custom ($_customPriority)'),
                    selected: _rule.priority == _customPriority,
                    onSelected: (_) => _change(() => _rule = _rule.copyWith(priority: _customPriority)),
                  ),
                ),
              SwitchListTile(
                key: const Key('rule-brightness-switch'),
                contentPadding: EdgeInsets.zero,
                title: Text(
                  brightness == null
                      ? 'Brightness override'
                      : 'Brightness override: ${brightnessToPercent(brightness)}%',
                ),
                subtitle: const Text(
                  'Dim or brighten the display while this rule is active.',
                ),
                value: brightness != null,
                onChanged: (on) => _change(
                  () => _rule = on ? _rule.copyWith(brightnessOverride: 50) : _rule.copyWith(clearBrightness: true),
                ),
              ),
              if (brightness != null)
                Slider(
                  key: const Key('rule-brightness'),
                  value: brightnessToPercent(brightness).toDouble(),
                  min: 0,
                  max: 100,
                  divisions: 100,
                  label: '${brightnessToPercent(brightness)}%',
                  semanticFormatterCallback: (v) => 'Brightness ${v.round()} percent',
                  onChanged: (v) => _change(
                    () => _rule = _rule.copyWith(
                      brightnessOverride: percentToBrightness(v.round()),
                    ),
                  ),
                ),
            ],
          ),
        ),
        EditorSection(
          title: 'Condition (optional)',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextField(
                key: const Key('rule-condition'),
                controller: _condition,
                onChanged: (_) => setState(() => _touched = true),
                decoration: const InputDecoration(
                  labelText: 'Only when',
                  helperText: 'Supported: $conditionHint',
                  helperMaxLines: 3,
                ),
              ),
              const SizedBox(height: Space.sm),
              Wrap(
                spacing: 8,
                runSpacing: 4,
                children: [
                  for (final sample in [
                    'spotify_playing',
                    'line_disrupted:',
                    'bus_due:',
                    'ha_state:',
                    'bin_day',
                    'road_disrupted:any',
                  ])
                    ActionChip(
                      label: Text(sample),
                      onPressed: () => _change(() {
                        _condition.text = sample;
                        _condition.selection = TextSelection.collapsed(
                          offset: sample.length,
                        );
                      }),
                    ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _SummaryBanner extends StatelessWidget {
  const _SummaryBanner({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(bottom: Space.lg),
      padding: const EdgeInsets.all(Space.md),
      decoration: BoxDecoration(
        color: scheme.primaryContainer,
        borderRadius: BorderRadius.circular(Radii.md),
      ),
      child: Row(
        children: [
          Icon(
            Icons.schedule_rounded,
            color: scheme.onPrimaryContainer,
            size: 20,
          ),
          const SizedBox(width: Space.sm),
          Expanded(
            child: Text(
              text,
              key: const Key('rule-summary'),
              style: TextStyle(
                color: scheme.onPrimaryContainer,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A round single-letter-ish day toggle with a 48px tap target.
class _DayChip extends StatelessWidget {
  const _DayChip({
    super.key,
    required this.label,
    required this.selected,
    required this.onChanged,
  });

  final String label;
  final bool selected;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      child: InkResponse(
        onTap: () => onChanged(!selected),
        radius: 26,
        child: SizedBox(
          height: 48,
          child: Center(
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 120),
              constraints: const BoxConstraints(maxWidth: 38, maxHeight: 38),
              width: 38,
              height: 38,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: selected ? scheme.primary : scheme.surfaceContainerHigh,
                border: Border.all(
                  color: selected ? scheme.primary : scheme.outlineVariant,
                ),
              ),
              child: ExcludeSemantics(
                child: FittedBox(
                  fit: BoxFit.scaleDown,
                  child: Text(
                    label.substring(0, 2),
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(
                          color: selected ? scheme.onPrimary : scheme.onSurfaceVariant,
                        ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _TimeTile extends StatelessWidget {
  const _TimeTile({
    super.key,
    required this.label,
    required this.value,
    required this.isSet,
    required this.onTap,
    this.onClear,
  });

  final String label;
  final String value;
  final bool isSet;
  final VoidCallback onTap;
  final VoidCallback? onClear;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Semantics(
      button: true,
      label: '$label $value',
      child: Material(
        color: scheme.surfaceContainer,
        borderRadius: BorderRadius.circular(Radii.md),
        child: InkWell(
          borderRadius: BorderRadius.circular(Radii.md),
          onTap: onTap,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 64),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(
                Space.md,
                Space.sm,
                4,
                Space.sm,
              ),
              child: Row(
                children: [
                  Expanded(
                    child: ExcludeSemantics(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            label,
                            style: text.labelSmall?.copyWith(
                              color: scheme.onSurfaceVariant,
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            value,
                            overflow: TextOverflow.ellipsis,
                            style: numericStyle(
                              text.titleMedium?.copyWith(
                                color: isSet ? scheme.onSurface : scheme.onSurfaceVariant,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (onClear != null)
                    IconButton(
                      tooltip: 'Clear $label',
                      onPressed: onClear,
                      icon: const Icon(Icons.close_rounded, size: 18),
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
