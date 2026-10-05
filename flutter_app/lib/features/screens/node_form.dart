import 'package:flutter/material.dart';

import '../../api/screen_models.dart';
import '../apps/color_picker.dart';
import 'screens_provider.dart';

typedef SetProp = void Function(String prop, Object? value);

/// Property form for one node, generated from the schema: the node type's props first, the common layout props
/// in a collapsible section. Problems from a failed save show next to the field they belong to.
class NodeForm extends StatelessWidget {
  const NodeForm({
    super.key,
    required this.node,
    required this.schema,
    required this.problems,
    required this.insideList,
    required this.onSet,
  });

  final ScreenNode node;
  final ScreenSchema schema;
  final List<NodeProblem> problems;
  final bool insideList;
  final SetProp onSet;

  String? _problem(String prop) {
    final messages = [for (final p in problems) if (p.prop == prop) p.message];
    return messages.isEmpty ? null : messages.join('; ');
  }

  @override
  Widget build(BuildContext context) {
    final type = schema.typeOf(node.type);
    final typeProps = type?.props ?? const <PropSchema>[];
    final known = {...typeProps.map((p) => p.name), ...schema.commonProps.map((p) => p.name)};
    final unknown = [for (final k in node.props.keys) if (!known.contains(k)) k];
    final shownProblemProps = {...known};
    // Problems about props the schema does not list, or about the node itself.
    final stray = [for (final p in problems) if (!shownProblemProps.contains(p.prop)) p];
    final commonHasProblem = schema.commonProps.any((p) => _problem(p.name) != null);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final p in stray)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(p.toString(), style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ),
        for (final p in typeProps) _field(context, p),
        if (schema.commonProps.isNotEmpty)
          ExpansionTile(
            key: Key('layout-${identityHashCode(node)}-$commonHasProblem'),
            tilePadding: EdgeInsets.zero,
            initiallyExpanded: commonHasProblem,
            title: const Text('Layout'),
            children: [for (final p in schema.commonProps) _field(context, p)],
          ),
        if (unknown.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(
              'Other properties (kept as is): ${unknown.join(', ')}',
              style: Theme.of(context).textTheme.labelSmall,
            ),
          ),
      ],
    );
  }

  Widget _field(BuildContext context, PropSchema p) {
    final value = node.props[p.name];
    final error = _problem(p.name);
    final k = ObjectKey((node, p.name));
    final Widget field = switch (p.kind) {
      PropKind.int => _TextProp(
          key: k,
          label: p.name,
          value: value?.toString() ?? '',
          error: error,
          keyboardType: TextInputType.number,
          // Non-numbers are sent as typed so the device can say what is wrong.
          onChanged: (t) => onSet(p.name, t.trim().isEmpty ? null : (int.tryParse(t.trim()) ?? t)),
        ),
      PropKind.string => _TextProp(
          key: k,
          label: p.name,
          value: value?.toString() ?? '',
          error: error,
          onChanged: (t) => onSet(p.name, t.isEmpty ? null : t),
        ),
      PropKind.bool => _ChoiceProp(
          key: k,
          label: p.name,
          options: const ['true', 'false'],
          value: value is bool ? '$value' : null,
          error: error,
          onChanged: (v) => onSet(p.name, v == null ? null : v == 'true'),
        ),
      PropKind.enumeration => _ChoiceProp(
          key: k,
          label: p.name,
          options: p.options,
          value: value is String ? value : null,
          error: error,
          onChanged: (v) => onSet(p.name, v),
        ),
      PropKind.color => _ColorProp(
          key: k,
          label: p.name,
          value: value is String ? value : null,
          error: error,
          onChanged: (v) => onSet(p.name, v),
        ),
      PropKind.binding => _BindingProp(
          key: k,
          label: p.name,
          value: value,
          error: error,
          keys: schema.bindingKeys,
          insideList: insideList,
          onChanged: (v) => onSet(p.name, v),
        ),
    };
    return Padding(padding: const EdgeInsets.only(bottom: 10), child: field);
  }
}

class _TextProp extends StatefulWidget {
  const _TextProp({
    super.key,
    required this.label,
    required this.value,
    required this.onChanged,
    this.error,
    this.keyboardType,
  });

  final String label;
  final String value;
  final String? error;
  final TextInputType? keyboardType;
  final ValueChanged<String> onChanged;

  @override
  State<_TextProp> createState() => _TextPropState();
}

class _TextPropState extends State<_TextProp> {
  late final TextEditingController _c = TextEditingController(text: widget.value);

  @override
  void didUpdateWidget(_TextProp old) {
    super.didUpdateWidget(old);
    // Only an outside change (not the user's own typing) rewrites the text.
    if (widget.value != old.value && widget.value != _c.text) _c.text = widget.value;
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextField(
        key: Key('prop-${widget.label}'),
        controller: _c,
        keyboardType: widget.keyboardType,
        decoration: InputDecoration(labelText: widget.label, errorText: widget.error, border: const OutlineInputBorder(), isDense: true),
        onChanged: widget.onChanged,
      );
}

class _ChoiceProp extends StatelessWidget {
  const _ChoiceProp({
    super.key,
    required this.label,
    required this.options,
    required this.value,
    required this.onChanged,
    this.error,
  });

  final String label;
  final List<String> options;
  final String? value;
  final String? error;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) {
    // A value the schema does not list (hand-edited file) stays visible and selectable.
    final all = [...options, if (value != null && !options.contains(value)) value!];
    return DropdownButtonFormField<String?>(
      key: Key('prop-$label-${value ?? ''}'),
      initialValue: value,
      isExpanded: true,
      isDense: true,
      decoration: InputDecoration(labelText: label, errorText: error, border: const OutlineInputBorder(), isDense: true),
      items: [
        const DropdownMenuItem<String?>(value: null, child: Text('(default)')),
        for (final o in all) DropdownMenuItem<String?>(value: o, child: Text(o)),
      ],
      onChanged: onChanged,
    );
  }
}

class _ColorProp extends StatelessWidget {
  const _ColorProp({super.key, required this.label, required this.value, required this.onChanged, this.error});

  final String label;
  final String? value;
  final String? error;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) {
    final color = parseHexColor(value);
    final scheme = Theme.of(context).colorScheme;
    return InputDecorator(
      decoration: InputDecoration(labelText: label, errorText: error, border: const OutlineInputBorder(), isDense: true),
      child: Row(
        children: [
          InkWell(
            key: Key('prop-$label'),
            onTap: () async {
              final picked = await showColorPicker(context, initial: color ?? Colors.white, title: label);
              if (picked != null) onChanged(colorToHex(picked));
            },
            child: Row(
              children: [
                Container(
                  width: 28,
                  height: 28,
                  decoration: BoxDecoration(
                    color: color ?? Colors.transparent,
                    shape: BoxShape.circle,
                    border: Border.all(color: scheme.outline),
                  ),
                  child: color == null ? Icon(Icons.format_color_fill, size: 16, color: scheme.outline) : null,
                ),
                const SizedBox(width: 10),
                Text(value ?? '(default)'),
              ],
            ),
          ),
          const Spacer(),
          if (value != null)
            IconButton(
              key: Key('clear-$label'),
              tooltip: 'Clear $label',
              visualDensity: VisualDensity.compact,
              onPressed: () => onChanged(null),
              icon: const Icon(Icons.clear, size: 18),
            ),
        ],
      ),
    );
  }
}

/// A binding prop: free text with `{key}` templates, or a `{"bind":"key"}` object chosen from the catalogue.
class _BindingProp extends StatelessWidget {
  const _BindingProp({
    super.key,
    required this.label,
    required this.value,
    required this.keys,
    required this.insideList,
    required this.onChanged,
    this.error,
  });

  final String label;
  final Object? value;
  final String? error;
  final List<BindingKeyInfo> keys;
  final bool insideList;
  final ValueChanged<Object?> onChanged;

  @override
  Widget build(BuildContext context) {
    final bound = value is Map ? (value as Map)['bind']?.toString() : null;
    final text = value is Map ? '' : (value?.toString() ?? '');
    final picker = IconButton(
      key: Key('bind-$label'),
      tooltip: 'Insert a binding',
      onPressed: () async {
        final choice = await showBindingPicker(context, keys, insideList: insideList);
        if (choice == null) return;
        if (choice.asBind) {
          onChanged({'bind': choice.key});
        } else {
          onChanged('$text{${choice.key}}');
        }
      },
      icon: const Icon(Icons.link_rounded),
    );
    if (bound != null) {
      return InputDecorator(
        decoration: InputDecoration(labelText: label, errorText: error, border: const OutlineInputBorder(), isDense: true, suffixIcon: picker),
        child: Row(
          children: [
            Expanded(child: Text('bound to $bound', key: Key('prop-$label'))),
            IconButton(
              key: Key('clear-$label'),
              tooltip: 'Unbind $label',
              visualDensity: VisualDensity.compact,
              onPressed: () => onChanged(null),
              icon: const Icon(Icons.clear, size: 18),
            ),
          ],
        ),
      );
    }
    return Stack(
      alignment: Alignment.centerRight,
      children: [
        _TextProp(
          label: label,
          value: text,
          error: error,
          onChanged: (t) => onChanged(t.isEmpty ? null : t),
        ),
        Padding(padding: EdgeInsets.only(bottom: error == null ? 0 : 20), child: picker),
      ],
    );
  }
}

typedef BindingChoice = ({String key, bool asBind});

/// Dialog with the binding key catalogue and a free-text key field (for `tube.<line-id>` and `ha:<entity_id>`).
Future<BindingChoice?> showBindingPicker(BuildContext context, List<BindingKeyInfo> keys, {required bool insideList}) =>
    showDialog<BindingChoice>(
      context: context,
      builder: (_) => _BindingPickerDialog(keys: keys, insideList: insideList),
    );

class _BindingPickerDialog extends StatefulWidget {
  const _BindingPickerDialog({required this.keys, required this.insideList});

  final List<BindingKeyInfo> keys;
  final bool insideList;

  @override
  State<_BindingPickerDialog> createState() => _BindingPickerDialogState();
}

class _BindingPickerDialogState extends State<_BindingPickerDialog> {
  final _key = TextEditingController();

  @override
  void dispose() {
    _key.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AlertDialog(
      title: const Text('Insert a binding'),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextField(
              key: const Key('binding-key-field'),
              controller: _key,
              autofocus: false,
              decoration: const InputDecoration(labelText: 'Key (pick one below or type it)', border: OutlineInputBorder()),
              onChanged: (_) => setState(() {}),
            ),
            const SizedBox(height: 8),
            Flexible(
              child: ListView(
                shrinkWrap: true,
                children: [
                  for (final info in widget.keys) ...[
                    if (info.fields.isEmpty)
                      ListTile(
                        key: Key('binding-${info.key}'),
                        dense: true,
                        enabled: !info.insideListOnly || widget.insideList,
                        title: Text(info.key),
                        subtitle: Text(info.insideListOnly ? '${info.description} (inside a list only)' : info.description),
                        onTap: () => setState(() => _key.text = info.key),
                      )
                    else
                      for (final k in info.concreteKeys)
                        ListTile(
                          key: Key('binding-$k'),
                          dense: true,
                          title: Text(k),
                          subtitle: Text(info.description),
                          onTap: () => setState(() => _key.text = k),
                        ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        TextButton(
          key: const Key('binding-use-bind'),
          onPressed: _key.text.trim().isEmpty ? null : () => Navigator.pop(context, (key: _key.text.trim(), asBind: true)),
          child: const Text('Bind value'),
        ),
        FilledButton(
          key: const Key('binding-use-template'),
          onPressed: _key.text.trim().isEmpty ? null : () => Navigator.pop(context, (key: _key.text.trim(), asBind: false)),
          child: Text(_key.text.trim().isEmpty ? 'Insert' : 'Insert {${_key.text.trim()}}', style: theme.textTheme.labelLarge?.copyWith(color: theme.colorScheme.onPrimary)),
        ),
      ],
    );
  }
}
