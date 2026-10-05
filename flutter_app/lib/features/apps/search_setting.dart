import 'dart:async';

import 'package:flutter/foundation.dart' show mapEquals;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/providers.dart';

/// Reports a pick: the new value plus the label(s) to show until the settings reload.
typedef SearchChanged = void Function(Object value, {String? label, List<String>? labels});

/// Debounced typeahead for Search (one id) and MultiSearch (comma separated ids) settings.
/// Works for inactive apps because the options endpoint is stateless.
class SearchSetting extends ConsumerStatefulWidget {
  const SearchSetting({super.key, required this.setting, required this.appId, required this.onChanged, this.context = const {}});

  final AppSetting setting;
  final String appId;
  final SearchChanged onChanged;

  /// Current values of the app's other settings, sent with every options request (`ctx.<key>`). A `browse` setting
  /// (such as the routes of a station) lists its options straight away and reloads when this changes.
  final Map<String, String> context;

  static const debounce = Duration(milliseconds: 300);

  @override
  ConsumerState<SearchSetting> createState() => _SearchSettingState();
}

class _SearchSettingState extends ConsumerState<SearchSetting> {
  final _controller = TextEditingController();
  Timer? _timer;
  int _seq = 0; // last query wins
  List<SettingOption> _results = const [];
  bool _loading = false;

  bool get _multi => widget.setting.type == AppSettingType.multiSearch;
  bool get _browse => widget.setting.browse;

  @override
  void initState() {
    super.initState();
    if (_browse) {
      _loading = true;
      _run('', ++_seq);
    }
  }

  @override
  void didUpdateWidget(SearchSetting old) {
    super.didUpdateWidget(old);
    // The options depend on other settings (the station behind a list of routes): reload when one of them changed
    if (_browse && !mapEquals(old.context, widget.context)) {
      _timer?.cancel();
      _loading = true;
      _run(_controller.text.trim(), ++_seq);
    }
  }

  /// Picked (id, label) pairs.
  List<(String, String)> get _picked {
    final s = widget.setting;
    if (_multi) {
      final ids = s.currentIds;
      final labels = s.currentLabels ?? const <String>[];
      return [for (var i = 0; i < ids.length; i++) (ids[i], i < labels.length && labels[i].isNotEmpty ? labels[i] : ids[i])];
    }
    final id = s.currentValue?.toString() ?? '';
    if (id.isEmpty) return const [];
    final label = s.currentLabel;
    return [(id, label == null || label.isEmpty ? id : label)];
  }

  int? get _max {
    final m = widget.setting.maxValue;
    return m != null && m > 0 ? m.round() : null;
  }

  bool get _full => _multi && _max != null && _picked.length >= _max!;

  @override
  void dispose() {
    _timer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _onText(String text) {
    _timer?.cancel();
    final seq = ++_seq; // invalidates any query still in flight
    final q = text.trim();
    if (q.length < 2 && !_browse) {
      setState(() {
        _results = const [];
        _loading = false;
      });
      return;
    }
    setState(() => _loading = true);
    _timer = Timer(SearchSetting.debounce, () => _run(q, seq));
  }

  Future<void> _run(String q, int seq) async {
    final result = await ref.read(apiProvider).getSettingOptions(widget.appId, widget.setting.key, q, context: widget.context);
    if (!mounted || seq != _seq) return; // stale
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

  void _reset() {
    _timer?.cancel();
    _seq++;
    _controller.clear();
    if (_browse) {
      // The list stays; only the filter text goes
      _run('', _seq);
      return;
    }
    setState(() {
      _results = const [];
      _loading = false;
    });
  }

  void _pick(SettingOption o) {
    if (_multi) {
      final picked = _picked;
      if (picked.any((p) => p.$1 == o.value) || _full) return;
      final next = [...picked, (o.value, o.label)];
      widget.onChanged(next.map((p) => p.$1).join(','), labels: next.map((p) => p.$2).toList());
    } else {
      widget.onChanged(o.value, label: o.label);
    }
    _reset();
  }

  void _remove(String id) {
    final next = _picked.where((p) => p.$1 != id).toList();
    widget.onChanged(next.map((p) => p.$1).join(','), labels: next.map((p) => p.$2).toList());
  }

  void _clear() => widget.onChanged('', label: '');

  @override
  Widget build(BuildContext context) {
    final s = widget.setting;
    final theme = Theme.of(context);
    final picked = _picked;
    final pickedIds = {for (final p in picked) p.$1};
    final shown = _full ? <SettingOption>[] : _results.where((o) => !_multi || !pickedIds.contains(o.value)).toList();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(s.name, style: theme.textTheme.bodyLarge),
        if (s.description.isNotEmpty)
          Text(s.description, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        const SizedBox(height: 8),
        if (_multi && picked.isNotEmpty)
          Wrap(
            spacing: 8,
            runSpacing: 4,
            children: [
              for (final p in picked)
                InputChip(
                  key: ValueKey('chip-${p.$1}'),
                  label: Text(p.$2),
                  onDeleted: () => _remove(p.$1),
                ),
            ],
          ),
        if (!_multi && picked.isNotEmpty)
          ListTile(
            contentPadding: EdgeInsets.zero,
            dense: true,
            leading: const Icon(Icons.check_rounded),
            title: Text(picked.first.$2, key: const ValueKey('search-current')),
            trailing: IconButton(
              key: const ValueKey('search-clear'),
              tooltip: 'Clear',
              icon: const Icon(Icons.close_rounded),
              onPressed: _clear,
            ),
          ),
        if (!_full)
          TextField(
            key: ValueKey('search-field-${s.key}'),
            controller: _controller,
            onChanged: _onText,
            decoration: InputDecoration(
              labelText: _browse ? 'Filter...' : (_multi ? 'Add...' : (picked.isEmpty ? 'Search...' : 'Change...')),
              prefixIcon: const Icon(Icons.search_rounded),
              suffixIcon: _loading
                  ? const Padding(
                      padding: EdgeInsets.all(12),
                      child: SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2)),
                    )
                  : null,
              border: const OutlineInputBorder(),
            ),
          )
        else
          Text('Maximum of ${_max!} reached. Remove one to add another.',
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        if (_browse && !_loading && !_full && shown.isEmpty)
          Text(_results.isEmpty ? 'Nothing to pick yet. Choose the station first.' : 'Everything is already picked.',
              key: const ValueKey('browse-empty'),
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        for (final o in shown)
          ListTile(
            key: ValueKey('option-${o.value}'),
            dense: true,
            contentPadding: EdgeInsets.zero,
            title: Text(o.label),
            subtitle: o.subtitle == null ? null : Text(o.subtitle!),
            onTap: () => _pick(o),
          ),
      ],
    );
  }
}
