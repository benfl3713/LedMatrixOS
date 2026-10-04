import 'package:flutter/material.dart';

import '../../api/models.dart';
import 'named_colors.dart';

/// Selects with more options than this open a searchable picker instead of a dropdown.
const searchableSelectThreshold = 8;

/// Looks like a dropdown, opens a bottom sheet with a search field and the filtered options.
class SearchableSelect extends StatelessWidget {
  const SearchableSelect({super.key, required this.setting, required this.options, required this.onChanged});

  final AppSetting setting;
  final List<String> options;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final s = setting;
    return InkWell(
      key: Key('select-${s.key}'),
      borderRadius: BorderRadius.circular(4),
      onTap: () async {
        final picked = await showModalBottomSheet<String>(
          context: context,
          isScrollControlled: true,
          useSafeArea: true,
          showDragHandle: true,
          builder: (_) => _OptionPickerSheet(title: s.name, options: options, current: s.currentValue?.toString()),
        );
        if (picked != null) onChanged(picked);
      },
      child: InputDecorator(
        decoration: InputDecoration(
          labelText: s.name,
          helperText: s.description.isEmpty ? null : s.description,
          helperMaxLines: 6,
          border: const OutlineInputBorder(),
          suffixIcon: const Icon(Icons.search_rounded),
        ),
        child: Text(s.currentValue?.toString() ?? ''),
      ),
    );
  }
}

class _OptionPickerSheet extends StatefulWidget {
  const _OptionPickerSheet({required this.title, required this.options, required this.current});

  final String title;
  final List<String> options;
  final String? current;

  @override
  State<_OptionPickerSheet> createState() => _OptionPickerSheetState();
}

class _OptionPickerSheetState extends State<_OptionPickerSheet> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final q = _query.trim().toLowerCase();
    final shown = [for (final o in widget.options) if (q.isEmpty || o.toLowerCase().contains(q)) o];
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * 0.7,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              child: TextField(
                key: const Key('option-search'),
                autofocus: true,
                decoration: InputDecoration(
                  labelText: 'Search ${widget.title}',
                  prefixIcon: const Icon(Icons.search_rounded),
                  border: const OutlineInputBorder(),
                ),
                onChanged: (v) => setState(() => _query = v),
              ),
            ),
            Expanded(
              child: shown.isEmpty
                  ? const Center(child: Text('No matches'))
                  : ListView.builder(
                      itemCount: shown.length,
                      itemBuilder: (context, i) => ListTile(
                        key: Key('option-${shown[i]}'),
                        title: Text(shown[i]),
                        trailing: shown[i] == widget.current ? const Icon(Icons.check_rounded) : null,
                        onTap: () => Navigator.pop(context, shown[i]),
                      ),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

/// A select whose options are colour names, shown as a grid of swatches. Sends the same name strings as the dropdown did.
class ColourSelect extends StatelessWidget {
  const ColourSelect({super.key, required this.setting, required this.options, required this.onChanged});

  final AppSetting setting;
  final List<String> options;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final s = setting;
    final theme = Theme.of(context);
    final current = s.currentValue?.toString();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(children: [Expanded(child: Text(s.name)), Text(current ?? '', style: theme.textTheme.bodySmall)]),
        if (s.description.isNotEmpty)
          Text(s.description, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final o in options)
              _Swatch(
                key: Key('colour-${s.key}-$o'),
                name: o,
                color: namedColors[o.toLowerCase()],
                selected: o == current,
                onTap: () => onChanged(o),
              ),
          ],
        ),
      ],
    );
  }
}

class _Swatch extends StatelessWidget {
  const _Swatch({super.key, required this.name, required this.color, required this.selected, required this.onTap});

  final String name;
  final Color? color;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final border = Border.all(
      color: selected ? theme.colorScheme.primary : theme.colorScheme.outlineVariant,
      width: selected ? 3 : 1,
    );
    final swatch = color;
    final child = swatch == null
        // Not a plain colour (e.g. "Palette", "Rainbow"): a labelled chip
        ? Container(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            height: 44,
            alignment: Alignment.center,
            decoration: BoxDecoration(borderRadius: BorderRadius.circular(22), border: border),
            child: Text(name, style: theme.textTheme.labelLarge),
          )
        : Container(
            width: 44,
            height: 44,
            decoration: BoxDecoration(color: swatch, shape: BoxShape.circle, border: border),
            child: selected
                ? Icon(
                    Icons.check_rounded,
                    color: ThemeData.estimateBrightnessForColor(swatch) == Brightness.dark ? Colors.white : Colors.black,
                  )
                : null,
          );
    return Tooltip(message: name, child: InkResponse(onTap: onTap, radius: 28, child: child));
  }
}
