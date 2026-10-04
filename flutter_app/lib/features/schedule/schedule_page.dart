import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/schedule_models.dart';
import '../../core/providers.dart';
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
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorView(error: e, onRetry: notifier.reload),
        data: (s) => RefreshIndicator(
          onRefresh: () async {
            await ref.read(scheduleStatusProvider.notifier).refresh();
          },
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              const _StatusCard(),
              if (s.dirty) _UnsavedBanner(saving: s.saving),
              if (s.errors.isNotEmpty) _ValidationErrors(errors: s.errors),
              if (!s.fromDevice) const _NotFromDeviceNote(),
              const SizedBox(height: 8),
              _SectionHeader(
                title: 'Week',
                action: TextButton.icon(
                  key: const Key('add-rule'),
                  onPressed: () => _editRule(context, ref, s, null),
                  icon: const Icon(Icons.add),
                  label: const Text('Add rule'),
                ),
              ),
              _WeekView(rules: s.draft.rules, onTapRule: (i) => _editRule(context, ref, s, i)),
              const SizedBox(height: 8),
              if (s.draft.rules.isEmpty)
                const Padding(padding: EdgeInsets.all(16), child: Center(child: Text('No rules yet. Add one to schedule a playlist.')))
              else
                for (var i = 0; i < s.draft.rules.length; i++)
                  _RuleTile(
                    key: Key('rule-tile-$i'),
                    index: i,
                    rule: s.draft.rules[i],
                    onTap: () => _editRule(context, ref, s, i),
                    onDelete: () => notifier.deleteRule(i),
                  ),
              const SizedBox(height: 16),
              _SectionHeader(
                title: 'Playlists',
                action: TextButton.icon(
                  key: const Key('add-playlist'),
                  onPressed: () => _editPlaylist(context, ref, s, null),
                  icon: const Icon(Icons.add),
                  label: const Text('Add playlist'),
                ),
              ),
              if (s.draft.playlists.isEmpty)
                const Padding(padding: EdgeInsets.all(16), child: Center(child: Text('No playlists yet.')))
              else
                for (var i = 0; i < s.draft.playlists.length; i++)
                  _PlaylistTile(
                    key: Key('playlist-tile-$i'),
                    playlist: s.draft.playlists[i],
                    usedBy: s.draft.rules.where((r) => r.playlistId == s.draft.playlists[i].name).length,
                    onTap: () => _editPlaylist(context, ref, s, i),
                    onDelete: () => notifier.deletePlaylist(i),
                  ),
            ],
          ),
        ),
      ),
    );
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

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.title, required this.action});

  final String title;
  final Widget action;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          Expanded(child: Text(title, style: Theme.of(context).textTheme.titleMedium)),
          action,
        ],
      );
}

class _UnsavedBanner extends StatelessWidget {
  const _UnsavedBanner({required this.saving});

  final bool saving;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      color: scheme.tertiaryContainer,
      child: ListTile(
        key: const Key('unsaved-banner'),
        dense: true,
        leading: Icon(Icons.edit_note_rounded, color: scheme.onTertiaryContainer),
        title: Text(saving ? 'Saving...' : 'Unsaved changes', style: TextStyle(color: scheme.onTertiaryContainer)),
        subtitle: Text('Save sends the whole schedule to the device.', style: TextStyle(color: scheme.onTertiaryContainer)),
      ),
    );
  }
}

class _NotFromDeviceNote extends StatelessWidget {
  const _NotFromDeviceNote();

  @override
  Widget build(BuildContext context) => Card(
        child: ListTile(
          key: const Key('not-from-device'),
          dense: true,
          leading: const Icon(Icons.info_outline_rounded),
          title: const Text('Showing the last schedule saved from this app'),
          subtitle: const Text('The device does not send back its stored schedule. Saving replaces it entirely.'),
        ),
      );
}

class _ValidationErrors extends StatelessWidget {
  const _ValidationErrors({required this.errors});

  final List<String> errors;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
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
              Text('The device rejected the schedule', style: TextStyle(color: scheme.onErrorContainer, fontWeight: FontWeight.w600)),
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
        loading: () => const Padding(padding: EdgeInsets.all(8), child: Center(child: CircularProgressIndicator())),
        error: (e, _) => Row(children: [
          Icon(Icons.cloud_off_rounded, color: scheme.error),
          const SizedBox(width: 12),
          Expanded(child: Text(e.toString())),
          IconButton(onPressed: () => ref.invalidate(scheduleStatusProvider), icon: const Icon(Icons.refresh), tooltip: 'Retry'),
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
                Icon(Icons.schedule_rounded, color: scheme.primary),
                const SizedBox(width: 8),
                Text('Active now', style: text.titleMedium),
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
  const _WeekView({required this.rules, required this.onTapRule});

  final List<RuleDoc> rules;
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
    return out;
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          children: [
            Row(children: [
              const SizedBox(width: 40),
              Expanded(
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    for (final h in ['0', '6', '12', '18', '24'])
                      Text(h, style: text.labelSmall?.copyWith(color: scheme.onSurfaceVariant)),
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
                    SizedBox(width: 40, child: Text(_dayNames[day], style: text.labelMedium)),
                    Expanded(
                      child: LayoutBuilder(builder: (context, c) {
                        final w = c.maxWidth;
                        double x(int minutes) => w * minutes / (24 * 60);
                        return Container(
                          height: 30,
                          decoration: BoxDecoration(
                            color: scheme.surfaceContainerHighest,
                            borderRadius: BorderRadius.circular(6),
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
                                    onTap: () => onTapRule(seg.rule),
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
          ],
        ),
      ),
    );
  }
}

class _Bar extends StatelessWidget {
  const _Bar({super.key, required this.rule, required this.onTap});

  final RuleDoc rule;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    // Higher priority reads stronger.
    final strength = (0.35 + rule.priority.clamp(0, 100) / 100 * 0.65).clamp(0.35, 1.0);
    final label = '${rule.playlistId} P${rule.priority}'
        '${rule.brightnessOverride == null ? '' : ' ${brightnessToPercent(rule.brightnessOverride!)}%'}';
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 0.5),
      child: Material(
        color: scheme.primary.withValues(alpha: strength),
        borderRadius: BorderRadius.circular(4),
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Text(
                label,
                maxLines: 1,
                softWrap: false,
                overflow: TextOverflow.clip,
                style: Theme.of(context).textTheme.labelSmall?.copyWith(color: scheme.onPrimary),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _RuleTile extends StatelessWidget {
  const _RuleTile({super.key, required this.index, required this.rule, required this.onTap, required this.onDelete});

  final int index;
  final RuleDoc rule;
  final VoidCallback onTap;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final extras = [
      'Priority ${rule.priority}',
      if (rule.brightnessOverride != null) 'Brightness ${brightnessToPercent(rule.brightnessOverride!)}%',
      if (rule.condition != null && rule.condition!.isNotEmpty) 'When ${rule.condition}',
    ];
    return Card(
      child: ListTile(
        onTap: onTap,
        leading: const Icon(Icons.event_repeat_rounded),
        title: Text(rule.playlistId.isEmpty ? '(no playlist)' : rule.playlistId),
        subtitle: Text('${describeDays(rule.daysMask)}, ${describeTimes(rule)}\n${extras.join(', ')}'),
        isThreeLine: true,
        trailing: IconButton(key: Key('delete-rule-$index'), tooltip: 'Delete rule', onPressed: onDelete, icon: const Icon(Icons.delete_outline_rounded)),
      ),
    );
  }
}

class _PlaylistTile extends StatelessWidget {
  const _PlaylistTile({super.key, required this.playlist, required this.usedBy, required this.onTap, required this.onDelete});

  final PlaylistDoc playlist;
  final int usedBy;
  final VoidCallback onTap;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final total = playlist.entries.fold<int>(0, (a, e) => a + e.durationMs) ~/ 1000;
    return Card(
      child: ListTile(
        onTap: onTap,
        leading: const Icon(Icons.queue_music_rounded),
        title: Text(playlist.name.isEmpty ? '(unnamed)' : playlist.name),
        subtitle: Text('${playlist.entries.length} entries, ${total}s loop, used by $usedBy rule${usedBy == 1 ? '' : 's'}'),
        trailing: IconButton(tooltip: 'Delete playlist', onPressed: onDelete, icon: const Icon(Icons.delete_outline_rounded)),
      ),
    );
  }
}
