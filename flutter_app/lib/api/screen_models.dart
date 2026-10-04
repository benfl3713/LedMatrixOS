import 'dart:convert';

/// Value kinds a node property can have (`kind` in the schema).
enum PropKind {
  int,
  string,
  bool,
  color,
  enumeration,
  binding;

  static PropKind parse(String? s) => switch (s) {
        'int' => PropKind.int,
        'bool' => PropKind.bool,
        'color' => PropKind.color,
        'enum' => PropKind.enumeration,
        'binding' => PropKind.binding,
        _ => PropKind.string,
      };
}

class PropSchema {
  const PropSchema({required this.name, required this.kind, this.options = const []});

  final String name;
  final PropKind kind;
  final List<String> options;

  factory PropSchema.fromJson(Map<String, dynamic> j) => PropSchema(
        name: j['name'] as String,
        kind: PropKind.parse(j['kind'] as String?),
        options: ((j['options'] ?? const []) as List).cast<String>(),
      );
}

class NodeTypeSchema {
  const NodeTypeSchema({required this.type, required this.props, required this.slots});

  final String type;
  final List<PropSchema> props;
  final List<String> slots;

  factory NodeTypeSchema.fromJson(Map<String, dynamic> j) => NodeTypeSchema(
        type: j['type'] as String,
        props: [for (final p in (j['props'] ?? const []) as List) PropSchema.fromJson(p as Map<String, dynamic>)],
        slots: ((j['slots'] ?? const []) as List).cast<String>(),
      );
}

/// One entry of the binding key catalogue, e.g. `weather.<field>` with its `fields`.
class BindingKeyInfo {
  const BindingKeyInfo({
    required this.key,
    required this.kind,
    required this.description,
    this.fields = const [],
    this.insideListOnly = false,
  });

  final String key;
  final String kind;
  final String description;
  final List<String> fields;
  final bool insideListOnly;

  factory BindingKeyInfo.fromJson(Map<String, dynamic> j) => BindingKeyInfo(
        key: j['key'] as String,
        kind: (j['kind'] ?? '') as String,
        description: (j['description'] ?? '') as String,
        fields: ((j['fields'] ?? const []) as List).cast<String>(),
        insideListOnly: (j['insideListOnly'] ?? false) as bool,
      );

  /// Concrete keys this entry offers: the key itself, or one per field for `weather.<field>`.
  List<String> get concreteKeys =>
      fields.isNotEmpty ? [for (final f in fields) key.replaceAll(RegExp('<[^>]*>'), f)] : [key];
}

/// The editor schema from `GET /api/screens/schema`.
class ScreenSchema {
  const ScreenSchema({
    required this.maxDepth,
    required this.maxNodes,
    required this.fonts,
    required this.slots,
    required this.commonProps,
    required this.nodeTypes,
    required this.bindingKeys,
    this.templateSyntax = '',
    this.listSourceSyntax = '',
  });

  final int maxDepth;
  final int maxNodes;
  final List<String> fonts;
  final List<String> slots;
  final List<PropSchema> commonProps;
  final List<NodeTypeSchema> nodeTypes;
  final List<BindingKeyInfo> bindingKeys;
  final String templateSyntax;
  final String listSourceSyntax;

  NodeTypeSchema? typeOf(String type) {
    for (final t in nodeTypes) {
      if (t.type == type) return t;
    }
    return null;
  }

  factory ScreenSchema.fromJson(Map<String, dynamic> j) {
    final bindings = (j['bindings'] ?? const {}) as Map<String, dynamic>;
    return ScreenSchema(
      maxDepth: (j['maxDepth'] ?? 8) as int,
      maxNodes: (j['maxNodes'] ?? 200) as int,
      fonts: ((j['fonts'] ?? const []) as List).cast<String>(),
      slots: ((j['slots'] ?? const []) as List).cast<String>(),
      commonProps: [
        for (final p in (j['commonProps'] ?? const []) as List) PropSchema.fromJson(p as Map<String, dynamic>)
      ],
      nodeTypes: [
        for (final t in (j['nodeTypes'] ?? const []) as List) NodeTypeSchema.fromJson(t as Map<String, dynamic>)
      ],
      bindingKeys: [
        for (final k in (bindings['keys'] ?? const []) as List) BindingKeyInfo.fromJson(k as Map<String, dynamic>)
      ],
      templateSyntax: (bindings['templateSyntax'] ?? '') as String,
      listSourceSyntax: (bindings['listSourceSyntax'] ?? '') as String,
    );
  }
}

/// The slot that holds a list of children; every other slot holds a single node.
const childrenSlot = 'children';
const singleSlotNames = ['top', 'bottom', 'left', 'right', 'fill', 'item'];

/// A mutable screen node. Props are kept as raw JSON values (flat on the wire) so unknown props survive a round trip.
class ScreenNode {
  ScreenNode({required this.type, Map<String, Object?>? props, List<ScreenNode>? children, Map<String, ScreenNode>? slots})
      : props = props ?? {},
        children = children ?? [],
        slots = slots ?? {};

  String type;
  final Map<String, Object?> props;
  final List<ScreenNode> children;

  /// Single-node slots (top/bottom/left/right/fill/item), keyed by slot name.
  final Map<String, ScreenNode> slots;

  factory ScreenNode.fromJson(Map<String, dynamic> j) {
    final node = ScreenNode(type: (j['type'] ?? '') as String);
    j.forEach((key, value) {
      if (key == 'type') return;
      if (key == childrenSlot && value is List) {
        for (final c in value) {
          if (c is Map<String, dynamic>) node.children.add(ScreenNode.fromJson(c));
        }
      } else if (singleSlotNames.contains(key) && value is Map<String, dynamic>) {
        node.slots[key] = ScreenNode.fromJson(value);
      } else {
        node.props[key] = value;
      }
    });
    return node;
  }

  Map<String, dynamic> toJson() => {
        'type': type,
        ...props,
        if (children.isNotEmpty) childrenSlot: [for (final c in children) c.toJson()],
        for (final e in slots.entries) e.key: e.value.toJson(),
      };

  ScreenNode clone() => ScreenNode.fromJson(jsonDecode(jsonEncode(toJson())) as Map<String, dynamic>);

  /// Every `(slot, index, child)` of this node in display order. `index` is null for single slots.
  Iterable<({String slot, int? index, ScreenNode node})> get childEntries sync* {
    for (var i = 0; i < children.length; i++) {
      yield (slot: childrenSlot, index: i, node: children[i]);
    }
    for (final e in slots.entries) {
      yield (slot: e.key, index: null, node: e.value);
    }
  }
}

class ScreenDefinition {
  ScreenDefinition({required this.id, required this.name, required this.root});

  String id;
  String name;
  ScreenNode root;

  factory ScreenDefinition.fromJson(Map<String, dynamic> j) => ScreenDefinition(
        id: (j['id'] ?? '') as String,
        name: (j['name'] ?? '') as String,
        root: ScreenNode.fromJson((j['root'] ?? const {'type': 'stack'}) as Map<String, dynamic>),
      );

  Map<String, dynamic> toJson() => {'id': id, 'name': name, 'root': root.toJson()};

  String encode() => jsonEncode(toJson());

  ScreenDefinition clone() => ScreenDefinition.fromJson(jsonDecode(encode()) as Map<String, dynamic>);
}

class ScreenSummary {
  const ScreenSummary({required this.id, required this.name});

  final String id;
  final String name;

  factory ScreenSummary.fromJson(Map<String, dynamic> j) =>
      ScreenSummary(id: (j['id'] ?? '') as String, name: (j['name'] ?? '') as String);
}

/// Starting points offered by "New screen".
class ScreenTemplate {
  const ScreenTemplate(this.key, this.label, this.description, this.json);

  final String key;
  final String label;
  final String description;
  final Map<String, dynamic> json;

  ScreenNode buildRoot() => ScreenNode.fromJson(jsonDecode(jsonEncode(json)) as Map<String, dynamic>);
}

const screenTemplates = <ScreenTemplate>[
  ScreenTemplate('clock', 'Clock', 'A big clock with the temperature underneath', {
    'type': 'stack',
    'direction': 'vertical',
    'padding': 4,
    'halign': 'center',
    'children': [
      {'type': 'clock', 'format': 'HH:mm', 'font': 'Big', 'color': '#FFFFFF'},
      {'type': 'label', 'text': '{weather.temp} degrees', 'color': '#FFD24A'},
    ],
  }),
  ScreenTemplate('status', 'Status list', 'A heading and a list of bound values', {
    'type': 'dock',
    'top': {'type': 'label', 'text': 'Status', 'font': 'Small', 'color': '#FFFFFF'},
    'fill': {
      'type': 'list',
      'source': 'Temp|weather.temp;Bin day|bin_day',
      'item': {'type': 'label', 'text': '{item.label}: {item}', 'font': 'Small', 'color': '#9CDCFE'},
    },
  }),
  ScreenTemplate('blank', 'Blank', 'An empty stack to build on', {'type': 'stack', 'direction': 'vertical'}),
];
