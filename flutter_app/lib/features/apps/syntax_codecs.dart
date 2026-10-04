// Pure parse/serialise code for the syntax-string settings that have structured editors. The persisted string formats are owned by the
// server (HaFormat.ParseEntities, BinParser.ParseBins/ParseReminders); these codecs read the same grammar and write it back unchanged
// where the user changed nothing. Fields are kept as raw text so a half-edited or unusual value survives a round trip.

const weekdayNames = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

String _trimTrailingEmpty(List<String> fields) {
  final f = [...fields];
  while (f.length > 1 && f.last.isEmpty) {
    f.removeLast();
  }
  return f.join('|');
}

/// Replaces characters that would break the syntax (separators) inside a free-text field.
String _clean(String text) => text.replaceAll(RegExp(r'[|;,\n\r]'), ' ').trim();

// ---- Home Assistant Tiles entities ----------------------------------------------------------------------------------------

/// One entity of the Home Assistant Tiles `Entities` setting: `id|Label|icon+spark`.
class HaEntityRow {
  const HaEntityRow({required this.id, this.label = '', this.icon = false, this.spark = false});

  final String id;
  final String label;
  final bool icon;
  final bool spark;

  HaEntityRow copyWith({String? label, bool? icon, bool? spark}) =>
      HaEntityRow(id: id, label: label ?? this.label, icon: icon ?? this.icon, spark: spark ?? this.spark);

  @override
  bool operator ==(Object other) =>
      other is HaEntityRow && other.id == id && other.label == label && other.icon == icon && other.spark == spark;

  @override
  int get hashCode => Object.hash(id, label, icon, spark);

  @override
  String toString() => 'HaEntityRow($id, $label, icon: $icon, spark: $spark)';
}

List<HaEntityRow> parseHaEntities(String? text) {
  final rows = <HaEntityRow>[];
  for (final raw in (text ?? '').split(RegExp(r'[,;\n]'))) {
    final part = raw.trim();
    if (part.isEmpty) continue;
    final bar = part.indexOf('|');
    final id = (bar < 0 ? part : part.substring(0, bar)).trim();
    var label = '';
    var icon = false;
    var spark = false;
    if (bar >= 0) {
      final rest = part.substring(bar + 1).split('|');
      label = rest.first.trim();
      for (final group in rest.skip(1)) {
        for (final flag in group.split(RegExp(r'[+ ]')).map((f) => f.trim().toLowerCase()).where((f) => f.isNotEmpty)) {
          if (flag == 'icon' || flag == 'glyph') icon = true;
          if (flag == 'spark' || flag == 'sparkline' || flag == 'history') spark = true;
        }
      }
    }
    // The server ignores anything that is not an entity id (domain.object)
    if (id.contains('.') && id.length > 2) rows.add(HaEntityRow(id: id, label: label, icon: icon, spark: spark));
  }
  return rows;
}

String serializeHaEntities(List<HaEntityRow> rows) => rows.where((r) => r.id.isNotEmpty).map((r) {
      final flags = [if (r.icon) 'icon', if (r.spark) 'spark'].join('+');
      final label = _clean(r.label);
      if (flags.isEmpty && label.isEmpty) return r.id;
      return _trimTrailingEmpty([r.id, label, flags]);
    }).join(', ');

// ---- Bin Day bins ---------------------------------------------------------------------------------------------------------

/// One collection rule of the Bin Day `Bins` setting: `Name|#colour|Day|EveryNWeeks|AnchorDate|skip,skip`.
class BinRow {
  const BinRow({this.name = '', this.colour = '', this.day = 'Mon', this.every = '', this.anchor = '', this.skips = ''});

  final String name;
  final String colour;
  final String day;
  final String every;
  final String anchor;

  /// Comma separated yyyy-MM-dd dates, as written in the setting.
  final String skips;

  BinRow copyWith({String? name, String? colour, String? day, String? every, String? anchor, String? skips}) => BinRow(
      name: name ?? this.name,
      colour: colour ?? this.colour,
      day: day ?? this.day,
      every: every ?? this.every,
      anchor: anchor ?? this.anchor,
      skips: skips ?? this.skips);

  @override
  bool operator ==(Object other) =>
      other is BinRow &&
      other.name == name &&
      other.colour == colour &&
      other.day == day &&
      other.every == every &&
      other.anchor == anchor &&
      other.skips == skips;

  @override
  int get hashCode => Object.hash(name, colour, day, every, anchor, skips);

  @override
  String toString() => 'BinRow($name, $colour, $day, $every, $anchor, $skips)';
}

List<String> _entries(String? text) =>
    (text ?? '').split(RegExp(r'[;\n\r]')).map((e) => e.trim()).where((e) => e.isNotEmpty).toList();

List<BinRow> parseBins(String? text) {
  final rows = <BinRow>[];
  for (final entry in _entries(text)) {
    final f = entry.split('|').map((e) => e.trim()).toList();
    String at(int i) => i < f.length ? f[i] : '';
    rows.add(BinRow(name: at(0), colour: at(1), day: at(2), every: at(3), anchor: at(4), skips: at(5)));
  }
  return rows;
}

String serializeBins(List<BinRow> rows) => rows
    .map((r) => _trimTrailingEmpty([_clean(r.name), r.colour, r.day, r.every, r.anchor, r.skips.split(',').map((s) => s.trim()).where((s) => s.isNotEmpty).join(',')]))
    .join('; ');

/// 0 = Mon .. 6 = Sun from "Mon", "monday", ... (first three letters, as the server reads it), or null.
int? parseWeekday(String? s) {
  if (s == null || s.length < 3) return null;
  final i = weekdayNames.indexWhere((d) => d.toLowerCase() == s.substring(0, 3).toLowerCase());
  return i < 0 ? null : i;
}

/// "#rgb" / "#rrggbb" (the # is optional) as the server reads it.
bool isValidBinColour(String? s) {
  if (s == null) return false;
  final c = s.startsWith('#') ? s.substring(1) : s;
  return (c.length == 3 || c.length == 6) && int.tryParse(c, radix: 16) != null;
}

/// A strict yyyy-MM-dd date, or null.
DateTime? parseIsoDate(String? s) {
  if (s == null || !RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(s)) return null;
  final d = DateTime.tryParse(s);
  if (d == null || d.toIso8601String().substring(0, 10) != s) return null;
  return d;
}

String formatIsoDate(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

/// The server's message for a bin rule it would reject (BinParser.ParseBins), or null when it is fine.
String? validateBin(BinRow r) {
  final label = r.name.isEmpty ? '?' : r.name;
  if (r.name.isEmpty) return 'Give the bin a name';
  if (!isValidBinColour(r.colour)) return '$label: pick a colour';
  final day = parseWeekday(r.day);
  if (day == null) return '$label: pick a day';
  var every = 1;
  if (r.every.isNotEmpty) {
    final n = int.tryParse(r.every);
    if (n == null || n < 1 || n > 4) return '$label: weeks must be 1-4';
    every = n;
  }
  DateTime? anchor;
  if (r.anchor.isNotEmpty) {
    anchor = parseIsoDate(r.anchor);
    if (anchor == null) return '$label: anchor date must be yyyy-MM-dd';
  }
  if (anchor != null && anchor.weekday - 1 != day) return '$label: anchor date is not a ${weekdayNames[day]}';
  if (every > 1 && anchor == null) return '$label: needs an anchor date';
  for (final s in r.skips.split(',').map((e) => e.trim()).where((e) => e.isNotEmpty)) {
    if (parseIsoDate(s) == null) return '$label: skip dates must be yyyy-MM-dd';
  }
  return null;
}

// ---- Bin Day reminders ----------------------------------------------------------------------------------------------------

/// One entry of the Bin Day `Reminders` setting: `Text|HH:mm|HH:mm|Mon,Tue,...` (days omitted or "daily" = every day).
class ReminderRow {
  const ReminderRow({this.text = '', this.start = '', this.end = '', this.days = ''});

  final String text;
  final String start;
  final String end;

  /// As written: empty / "daily" for every day, else comma separated day names.
  final String days;

  /// The selected weekdays (0 = Mon). Empty or "daily" means all seven; unreadable names are ignored.
  Set<int> get daySet {
    if (days.isEmpty || days.toLowerCase() == 'daily') return {0, 1, 2, 3, 4, 5, 6};
    return {for (final d in days.split(',')) if (parseWeekday(d.trim()) != null) parseWeekday(d.trim())!};
  }

  ReminderRow withDays(Set<int> selected) => copyWith(
      days: selected.length == 7 ? '' : (selected.toList()..sort()).map((i) => weekdayNames[i]).join(','));

  ReminderRow copyWith({String? text, String? start, String? end, String? days}) =>
      ReminderRow(text: text ?? this.text, start: start ?? this.start, end: end ?? this.end, days: days ?? this.days);

  @override
  bool operator ==(Object other) =>
      other is ReminderRow && other.text == text && other.start == start && other.end == end && other.days == days;

  @override
  int get hashCode => Object.hash(text, start, end, days);

  @override
  String toString() => 'ReminderRow($text, $start, $end, $days)';
}

List<ReminderRow> parseReminders(String? text) {
  final rows = <ReminderRow>[];
  for (final entry in _entries(text)) {
    final f = entry.split('|').map((e) => e.trim()).toList();
    String at(int i) => i < f.length ? f[i] : '';
    rows.add(ReminderRow(text: at(0), start: at(1), end: at(2), days: at(3)));
  }
  return rows;
}

String serializeReminders(List<ReminderRow> rows) =>
    rows.map((r) => _trimTrailingEmpty([_clean(r.text), r.start, r.end, r.days])).join('; ');

/// Minutes since midnight for "H:mm" / "HH:mm", or null.
int? parseClock(String? s) {
  final m = RegExp(r'^(\d{1,2}):(\d{2})$').firstMatch(s ?? '');
  if (m == null) return null;
  final h = int.parse(m[1]!);
  final min = int.parse(m[2]!);
  return h > 23 || min > 59 ? null : h * 60 + min;
}

String formatClock(int hour, int minute) => '${hour.toString().padLeft(2, '0')}:${minute.toString().padLeft(2, '0')}';

/// The server's message for a reminder it would reject (BinParser.ParseReminders), or null when it is fine.
String? validateReminder(ReminderRow r) {
  final label = r.text.isEmpty ? '?' : r.text;
  if (r.text.isEmpty) return 'Give the reminder some text';
  final start = parseClock(r.start);
  final end = parseClock(r.end);
  if (start == null || end == null) return '$label: times must be HH:mm';
  if (start == end) return '$label: the window is empty';
  if (r.daySet.isEmpty) return '$label: pick at least one day';
  return null;
}
