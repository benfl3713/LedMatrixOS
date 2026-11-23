import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/schedule_models.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'playlist_editor.dart';
import 'rule_editor.dart';
import 'schedule_provider.dart';

/// Display order of the week (Monday first) as day indexes where 0 = Sunday.
const _weekOrder = [1, 2, 3, 4, 5, 6, 0];
const _dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

String describeDays(int mask) {
  if (mask == allDaysMask) return 'Every day';
  if (mask == 62) return 'Weekdays';
  if (mask == 65) return 'Weekends';
  return [for (final d in _weekOrder) if (mask & (1 << d) != 0) _dayNames[d]].join(' ');
}

String describeTimes(RuleDoc r) {
  if (r.isAllDay) return 'All day';
  final s = r.startTime ?? '00:00';
  final e = r.endTime ?? '24:00';
  return '$s - $e${r.wraps ? ' (+1 day)' : ''}';
}

/// Distinct, LED-friendly colours; a playlist keeps its colour by its position in the schedule.
const _playlistPalette = <Color>[
  Color(0xFF22E5FF),
  Color(0xFFFFB84D),
  Color(0xFF7CFFB2),
  Color(0xFFFF7AA8),
  Color(0xFFB794FF),
  Color(0xFF6FA8FF),
  Color(0xFFFFE066),
  Color(0xFFFF8F5A),
];

Color playlistColor(String name, List<PlaylistDoc> playlists) {
  var i = playlists.indexWhere((p) => p.name == name);
  if (i < 0) i = name.hashCode.abs();
  return _playlistPalette[i % _playlistPalette.length];
}

Color _onColor(Color c) => c.computeLuminance() > 0.45 ? const Color(0xFF06141A) : Colors.white;

class SchedulePage extends ConsumerWidget {
  const SchedulePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final editor = ref.watch(scheduleEditorProvider);
    final state = editor.value;
    final notifier = ref.read(scheduleEditorProvider.notifier);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Schedule'),
        actions: [
          if (state != null && state.dirty) ...[
            TextButton(key: const Key('discard-schedule'), onPressed: state.saving ? null : notifier.discard, child: const Text('Discard')),
            Padding(
              padding: const EdgeInsets.only(right: 12, left: 4),
              child: FilledButton(
                key: const Key('save-schedule'),
                onPressed: state.saving ? null : notifier.save,
                child: state.saving
                    ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('Save'),
              ),
            ),
          ],
        ],
      ),
      body: editor.when(
        loading: () => const ListSkeleton(rows: 3, rowHeight: 110),
        error: (e, _) => ErrorView(error: e, onRetry: notifier.reload),
        data: (s) => RefreshIndicator(
          onRefresh: () async {
            await ref.read(scheduleStatusProvider.notifier).refresh();
          },
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              const _StatusCard(),
              if (s.dirty) _UnsavedBanner(saving: s.saving),
              if (s.errors.isNotEmpty) _ValidationErrors(errors: s.errors),
              if (!s.fromDevice && s.draft.rules.isNotEmpty) const _NotFromDeviceNote(),
              const SizedBox(height: Space.lg),
              SectionHeader(
                'Week',
                trailing: s.draft.rules.isEmpty
                    ? null
                    : TextButton.icon(
                        key: const Key('add-rule'),
                        onPressed: () => _editRule(context, ref, s, null),
                        icon: const Icon(Icons.add_rounded),
                        label: const Text('Add rule'),
                      ),
              ),
              if (s.draft.rules.isEmpty)
                SectionCard(
                  key: const Key('schedule-empty'),
                  child: EmptyState(
                    icon: Icons.calendar_month_rounded,
                    title: 'Put the display on a timetable',
                    message: 'A rule says what to show and when: a clock in the morning, weather at breakfast, '
                        'a dimmed screen overnight. Rules repeat every week.',
                    actionLabel: 'Create your first rule',
                    actionKey: const Key('add-rule'),
                    onAction: () => _editRule(context, ref, s, null),
                  ),
                )
              else ...[
                _WeekView(rules: s.draft.rules, playlists: s.draft.playlists, onTapRule: (i) => _editRule(context, ref, s, i)),
                const SizedBox(height: Space.md),
                for (var i = 0; i < s.draft.rules.length; i++)
                  _RuleTile(
                    key: Key('rule-tile-$i'),
                    index: i,
                    rule: s.draft.rules[i],
                    color: playlistColor(s.draft.rules[i].playlistId, s.draft.playlists),
                    onTap: () => _editRule(context, ref, s, i),
                    onDelete: () => notifier.deleteRule(i),
                  ),
              ],
              const SizedBox(height: Space.xl),
              SectionHeader(
                'Playlists',
                trailing: TextButton.icon(
                  key: const Key('add-playlist'),
                  onPressed: () => _editPlaylist(context, ref, s, null),
                  icon: const Icon(Icons.add_rounded),
                  label: const Text('Add playlist'),
                ),
              ),
              if (s.draft.playlists.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 4, vertical: Space.sm),
                  child: Text(
                    'A playlist is a loop of apps that rotate on the display. Rules pick which playlist runs when.',
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant),
                  ),
                )
              else
                for (var i = 0; i < s.draft.playlists.length; i++)
                  _PlaylistTile(
                    key: Key('playlist-tile-$i'),
                    playlist: s.draft.playlists[i],
                    color: playlistColor(s.draft.playlists[i].name, s.draft.playlists),
                    usedBy: s.draft.rules.where((r) => r.playlistId == s.draft.playlists[i].name).length,
                    onTap: () => _editPlaylist(context, ref, s, i),
                    onDelete: () => _deletePlaylist(context, ref, s, i),
                  ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _deletePlaylist(BuildContext context, WidgetRef ref, ScheduleEditState s, int i) async {
    final playlist = s.draft.playlists[i];
    final used = s.draft.rules.where((r) => r.playlistId == playlist.name).length;
    if (used > 0) {
      final ok = await confirmDestructive(
        context,
        title: 'Delete "${playlist.name}"?',
        message: 'It is used by $used rule${used == 1 ? '' : 's'}, which will have nothing to show.',
        confirmKey: const Key('playlist-tile-delete-confirm'),
      );
      if (!ok) return;
    }
    ref.read(scheduleEditorProvider.notifier).deletePlaylist(i);
  }

  Future<void> _editRule(BuildContext context, WidgetRef ref, ScheduleEditState s, int? index) async {
    final existing = index == null ? null : s.draft.rules[index];
    final result = await showRuleEditor(
      context,
      rule: existing ??
          RuleDoc(playlistId: s.draft.playlists.isEmpty ? '' : s.draft.playlists.first.name),
      isNew: existing == null,
      playlists: s.draft.playlists,
    );
    if (result == null) return;
    final notifier = ref.read(scheduleEditorProvider.notifier);
    if (result.delete) {
      if (index != null) notifier.deleteRule(index);
      return;
    }
    final created = result.createdPlaylist;
    if (created != null) notifier.upsertPlaylist(created);
    final singleApp = result.singleAppId;
    if (singleApp != null) notifier.ensureSingleAppPlaylist(singleApp);
    notifier.upsertRule(result.rule!, index: index);
  }

  Future<void> _editPlaylist(BuildContext context, WidgetRef ref, ScheduleEditState s, int? index) async {
    final existing = index == null ? null : s.draft.playlists[index];
    final result = await showPlaylistEditor(
      context,
      playlist: existing ?? const PlaylistDoc(name: ''),
      isNew: existing == null,
      otherNames: [
        for (var i = 0; i < s.draft.playlists.length; i++)
          if (i != index) s.draft.playlists[i].name,
      ],
    );
    if (result == null) return;
    final notifier = ref.read(scheduleEditorProvider.notifier);
    if (result.delete) {
      if (index != null) notifier.deletePlaylist(index);
      return;
    }
    notifier.upsertPlaylist(result.playlist!, index: index);
  }
}

class _UnsavedBanner extends StatelessWidget {
  const _UnsavedBanner({required this.saving});

  final bool saving;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(top: Space.md),
      child: Card(
        color: scheme.tertiaryContainer,
        child: ListTile(
          key: const Key('unsaved-banner'),
          dense: true,
          leading: Icon(Icons.edit_note_rounded, color: scheme.onTertiaryContainer),
          title: Text(saving ? 'Saving...' : 'Unsaved changes', style: TextStyle(color: scheme.onTertiaryContainer)),
          subtitle: Text('Save sends the whole schedule to the device.', style: TextStyle(color: scheme.onTertiaryContainer)),
        ),
      ),
    );
  }
}

class _NotFromDeviceNote extends StatelessWidget {
  const _NotFromDeviceNote();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: Space.md),
        child: Card(
          child: ListTile(
            key: const Key('not-from-device'),
            dense: true,
            leading: const Icon(Icons.info_outline_rounded),
            title: const Text('Showing the last schedule saved from this app'),
            subtitle: const Text('The device does not send back its stored schedule. Saving replaces it entirely.'),
          ),
        ),
      );
}

class _ValidationErrors extends StatelessWidget {
  const _ValidationErrors({required this.errors});

  final List<String> errors;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(top: Space.md),
      child: Card(
        key: const Key('validation-errors'),
        color: scheme.errorContainer,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(children: [
                Icon(Icons.error_outline_rounded, color: scheme.onErrorContainer),
                const SizedBox(width: 8),
                Expanded(
                  child: Text('The device rejected the schedule',
                      style: TextStyle(color: scheme.onErrorContainer, fontWeight: FontWeight.w600)),
                ),
              ]),
              const SizedBox(height: 8),
              for (final e in errors)
                Padding(
                  padding: const EdgeInsets.only(top: 2),
                  child: Text('• $e', style: TextStyle(color: scheme.onErrorContainer)),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Active now
// ---------------------------------------------------------------------------

class _StatusCard extends ConsumerWidget {
  const _StatusCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final status = ref.watch(scheduleStatusProvider);
    final apps = ref.watch(appListProvider).value;
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return SectionCard(
      child: status.when(
        loading: () => const Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Skeleton(height: 14, width: 90),
            SizedBox(height: 10),
            Skeleton(height: 24, width: 180),
            SizedBox(height: 8),
            Skeleton(height: 12),
          ],
        ),
        error: (e, _) => Row(children: [
          Icon(Icons.cloud_off_rounded, color: scheme.onSurfaceVariant),
          const SizedBox(width: 12),
          Expanded(child: Text("Can't read what is active right now.", style: TextStyle(color: scheme.onSurfaceVariant))),
          IconButton(onPressed: () => ref.invalidate(scheduleStatusProvider), icon: const Icon(Icons.refresh_rounded), tooltip: 'Retry'),
        ]),
        data: (s) {
          String appName(String? id) {
            if (id == null) return '';
            for (final MatrixApp a in apps?.apps ?? const []) {
              if (a.id == id) return a.name;
            }
            return id;
          }

          final rule = s.activeRule;
          return Column(
            key: const Key('status-card'),
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(children: [
                Icon(Icons.schedule_rounded, color: scheme.primary, size: 20),
                const SizedBox(width: 8),
                Text('Active now', style: text.titleSmall?.copyWith(color: scheme.primary)),
              ]),
              const SizedBox(height: 8),
              if (rule == null)
                Text('No rule is active.', style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant))
              else ...[
                Text(
                  s.playlist ?? rule.playlistId,
                  style: text.headlineSmall,
                ),
                const SizedBox(height: 4),
                Text(
                  'Rule ${rule.index + 1}, priority ${rule.priority}${rule.condition == null ? '' : ', when ${rule.condition}'}',
                  style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant),
                ),
                if (s.appId != null)
                  Text(
                    'Showing ${appName(s.appId)}${s.entryIndex != null && s.entryCount != null ? ' (entry ${s.entryIndex! + 1} of ${s.entryCount})' : ''}',
                  ),
              ],
              if (s.nextChange != null) ...[
                const SizedBox(height: 4),
                Text(
                  'Next change ${_whenText(s.nextChange!)}${s.nextChangeReason == null ? '' : ' (${s.nextChangeReason})'}',
                  style: text.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
                ),
              ],
            ],
          );
        },
      ),
    );
  }

  static String _whenText(DateTime t) {
    final diff = t.difference(DateTime.now());
    final at = formatHm(t.hour * 60 + t.minute);
    if (diff.isNegative || diff.inSeconds < 5) return 'at $at';
    final rel = diff.inMinutes >= 60
        ? 'in ${diff.inHours}h ${diff.inMinutes % 60}m'
        : diff.inMinutes >= 1
            ? 'in ${diff.inMinutes}m'
            : 'in ${diff.inSeconds}s';
    return 'at $at ($rel)';
  }
}

// ---------------------------------------------------------------------------
// Week view
// ---------------------------------------------------------------------------

class _Segment {
  const _Segment(this.rule, this.startMin, this.endMin, this.key);
  final int rule;
  final int startMin;
  final int endMin;
  final Key key;
}

class _WeekView extends StatelessWidget {
  const _WeekView({required this.rules, required this.playlists, required this.onTapRule});

  final List<RuleDoc> rules;
  final List<PlaylistDoc> playlists;
  final ValueChanged<int> onTapRule;

  List<_Segment> _segmentsFor(int day) {
    final out = <_Segment>[];
    for (var i = 0; i < rules.length; i++) {
      final r = rules[i];
      final s = parseHm(r.startTime) ?? 0;
      final e = r.endTime == null ? 24 * 60 : (parseHm(r.endTime) ?? 24 * 60);
      if (r.appliesOn(day)) {
        if (r.wraps || e < s) {
          out.add(_Segment(i, s, 24 * 60, Key('rule-bar-$i-$day')));
        } else {
          out.add(_Segment(i, s, e == s && !r.isAllDay ? s + 1 : e, Key('rule-bar-$i-$day')));
        }
      }
      // The part of yesterday's wrapping rule that spills into this day.
      final yesterday = (day + 6) % 7;
      if (r.wraps && r.appliesOn(yesterday)) {
        out.add(_Segment(i, 0, parseHm(r.endTime) ?? 0, Key('rule-bar-$i-$day-tail')));
      }
    }
    // Lower priority first so the winner is painted on top where rules overlap.
    out.sort((a, b) => rules[a.rule].priority.compareTo(rules[b.rule].priority));
    return out;
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final now = DateTime.now();
    final today = now.weekday % 7; // DateTime: Mon=1..Sun=7; ours: Sun=0
    final nowMin = now.hour * 60 + now.minute;
    final used = {for (final r in rules) if (r.playlistId.isNotEmpty) r.playlistId}.toList();
    return SectionCard(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(children: [
            const SizedBox(width: 40),
            Expanded(
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  for (final h in ['0', '6', '12', '18', '24'])
                    Text(h, style: numericStyle(text.labelSmall?.copyWith(color: scheme.onSurfaceVariant))),
                ],
              ),
            ),
          ]),
          const SizedBox(height: 4),
          for (final day in _weekOrder)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 2),
              child: Row(
                children: [
                  SizedBox(
                    width: 40,
                    child: Text(
                      _dayNames[day],
                      style: text.labelMedium?.copyWith(
                        color: day == today ? scheme.primary : scheme.onSurfaceVariant,
                        fontWeight: day == today ? FontWeight.w800 : FontWeight.w600,
                      ),
                    ),
                  ),
                  Expanded(
                    child: LayoutBuilder(builder: (context, c) {
                      final w = c.maxWidth;
                      double x(int minutes) => w * minutes / (24 * 60);
                      return Container(
                        height: 36,
                        decoration: BoxDecoration(
                          color: scheme.surfaceContainerHighest,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        clipBehavior: Clip.antiAlias,
                        child: Stack(
                          children: [
                            for (final seg in _segmentsFor(day))
                              Positioned(
                                left: x(seg.startMin),
                                width: (x(seg.endMin) - x(seg.startMin)).clamp(4, w),
                                top: 0,
                                bottom: 0,
                                child: _Bar(
                                  key: seg.key,
                                  rule: rules[seg.rule],
                                  color: playlistColor(rules[seg.rule].playlistId, playlists),
                                  onTap: () => onTapRule(seg.rule),
                                ),
                              ),
                            if (day == today)
                              Positioned(
                                left: (x(nowMin) - 1).clamp(0, w - 2),
                                top: 0,
                                bottom: 0,
                                child: IgnorePointer(
                                  child: Container(
                                    key: const Key('now-marker'),
                                    width: 2,
                                    decoration: BoxDecoration(
                                      color: Brand.live,
                                      boxShadow: [BoxShadow(color: Brand.live.withValues(alpha: 0.7), blurRadius: 6)],
                                    ),
                                  ),
                                ),
                              ),
                          ],
                        ),
                      );
                    }),
                  ),
                ],
              ),
            ),
          const SizedBox(height: Space.md),
          Wrap(
            spacing: 14,
            runSpacing: 6,
            children: [
              for (final name in used)
                Row(mainAxisSize: MainAxisSize.min, children: [
                  Container(
                    width: 10,
                    height: 10,
                    decoration: BoxDecoration(color: playlistColor(name, playlists), borderRadius: BorderRadius.circular(3)),
                  ),
                  const SizedBox(width: 6),
                  Text(name, style: text.labelMedium?.copyWith(color: scheme.onSurfaceVariant)),
                ]),
              Row(mainAxisSize: MainAxisSize.min, children: [
                Container(width: 2, height: 12, color: Brand.live),
                const SizedBox(width: 6),
                Text('Now', style: text.labelMedium?.copyWith(color: scheme.onSurfaceVariant)),
              ]),
            ],
          ),
        ],
      ),
    );
  }
}

class _Bar extends StatelessWidget {
  const _Bar({super.key, required this.rule, required this.color, required this.onTap});

  final RuleDoc rule;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final label = rule.playlistId.isEmpty ? '?' : rule.playlistId;
    return Semantics(
      button: true,
      label: '$label, ${describeDays(rule.daysMask)}, ${describeTimes(rule)}. Edit rule',
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 0.5),
        child: Material(
          color: color,
          borderRadius: BorderRadius.circular(5),
          child: InkWell(
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 6),
              child: Align(
                alignment: Alignment.centerLeft,
                child: ExcludeSemantics(
                  child: Text(
                    label,
                    maxLines: 1,
                    softWrap: false,
                    overflow: TextOverflow.clip,
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(color: _onColor(color), fontWeight: FontWeight.w700),
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

class _RuleTile extends StatelessWidget {
  const _RuleTile({
    super.key,
    required this.index,
    required this.rule,
    required this.color,
    required this.onTap,
    required this.onDelete,
  });

  final int index;
  final RuleDoc rule;
  final Color color;
  final VoidCallback onTap;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final extras = [
      'Priority ${rule.priority}',
      if (rule.brightnessOverride != null) 'Brightness ${brightnessToPercent(rule.brightnessOverride!)}%',
      if (rule.condition != null && rule.condition!.isNotEmpty) 'When ${rule.condition}',
    ];
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.sm),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Container(width: 5, color: color),
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(Space.md, Space.md, 0, Space.md),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(rule.playlistId.isEmpty ? '(no playlist)' : rule.playlistId, style: Theme.of(context).textTheme.titleSmall),
                        const SizedBox(height: 2),
                        Text('${describeDays(rule.daysMask)}, ${describeTimes(rule)}'),
                        Text(extras.join(', '),
                            style: Theme.of(context).textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
                      ],
                    ),
                  ),
                ),
                IconButton(
                  key: Key('delete-rule-$index'),
                  tooltip: 'Delete rule',
                  onPressed: onDelete,
                  icon: const Icon(Icons.delete_outline_rounded),
                ),
                const SizedBox(width: 4),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PlaylistTile extends StatelessWidget {
  const _PlaylistTile({
    super.key,
    required this.playlist,
    required this.color,
    required this.usedBy,
    required this.onTap,
    required this.onDelete,
  });

  final PlaylistDoc playlist;
  final Color color;
  final int usedBy;
  final VoidCallback onTap;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final total = playlist.entries.fold<int>(0, (a, e) => a + e.durationMs) ~/ 1000;
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.sm),
      child: Card(
        child: ListTile(
          onTap: onTap,
          leading: Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(color: color.withValues(alpha: 0.18), borderRadius: BorderRadius.circular(10)),
            child: Icon(Icons.queue_music_rounded, color: color, size: 20),
          ),
          title: Text(playlist.name.isEmpty ? '(unnamed)' : playlist.name),
          subtitle: Text('${playlist.entries.length} entries, ${total}s loop, used by $usedBy rule${usedBy == 1 ? '' : 's'}'),
          trailing: IconButton(tooltip: 'Delete playlist', onPressed: onDelete, icon: const Icon(Icons.delete_outline_rounded)),
        ),
      ),
    );
  }
}
