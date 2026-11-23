import '../../api/models.dart';
import '../../core/app_categories.dart';

/// Apps grouped by category, in [appCategories] order; empty categories are omitted.
List<(AppCategoryStyle, List<MatrixApp>)> groupByCategory(Iterable<MatrixApp> apps) {
  final byKey = <String, List<MatrixApp>>{};
  for (final a in apps) {
    byKey.putIfAbsent(categoryOf(a).key, () => []).add(a);
  }
  return [
    for (final c in appCategories)
      if (byKey[c.key] != null) (c, byKey[c.key]!),
  ];
}

/// Case-insensitive search over name, description, keywords and category, ranked: name prefix, name contains, description,
/// keywords, category. Ties keep the server order.
List<MatrixApp> searchApps(Iterable<MatrixApp> apps, String query) {
  final q = query.trim().toLowerCase();
  if (q.isEmpty) return apps.toList();
  final scored = <(int, int, MatrixApp)>[];
  var i = 0;
  for (final a in apps) {
    final name = a.name.toLowerCase();
    int? score;
    if (name.startsWith(q)) {
      score = 0;
    } else if (name.contains(q)) {
      score = 1;
    } else if (a.description.toLowerCase().contains(q)) {
      score = 2;
    } else if (a.keywords.any((k) => k.toLowerCase().contains(q))) {
      score = 3;
    } else if (a.category.toLowerCase().contains(q) || categoryOf(a).label.toLowerCase().contains(q)) {
      score = 4;
    }
    if (score != null) scored.add((score, i, a));
    i++;
  }
  scored.sort((x, y) => x.$1 != y.$1 ? x.$1.compareTo(y.$1) : x.$2.compareTo(y.$2));
  return [for (final s in scored) s.$3];
}

/// Apps for the Now page quick switch: favourites first, then recents, then the first apps, at most [limit].
List<MatrixApp> quickSwitchApps(List<MatrixApp> all, List<String> favourites, List<String> recents, {int limit = 8}) {
  final byId = {for (final a in all) a.id: a};
  final out = <MatrixApp>[];
  final seen = <String>{};
  void add(MatrixApp? a) {
    if (a != null && out.length < limit && seen.add(a.id)) out.add(a);
  }

  for (final id in favourites) {
    add(byId[id]);
  }
  for (final id in recents) {
    add(byId[id]);
  }
  for (final a in all) {
    add(a);
  }
  return out;
}
