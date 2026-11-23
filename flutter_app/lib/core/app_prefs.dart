import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _favouritesKey = 'app_favourites';
const _recentsKey = 'app_recents';
const maxRecents = 8;

/// The user's favourite apps and the last [maxRecents] they put on the display, newest first.
class AppPrefs {
  const AppPrefs({this.favourites = const [], this.recents = const []});

  final List<String> favourites;
  final List<String> recents;

  bool isFavourite(String id) => favourites.contains(id);
}

/// Loads favourites and recents from storage. Changes made before the load finishes (the provider is created lazily,
/// often by the very action that changes it) are applied straight away and replayed on top of the stored values once
/// they arrive, so they never overwrite what was saved.
class AppPrefsNotifier extends Notifier<AppPrefs> {
  bool _loaded = false;
  final List<AppPrefs Function(AppPrefs)> _pending = [];

  @override
  AppPrefs build() {
    _load();
    return const AppPrefs();
  }

  Future<void> _load() async {
    var loaded = const AppPrefs();
    try {
      final prefs = await SharedPreferences.getInstance();
      loaded = AppPrefs(
        favourites: prefs.getStringList(_favouritesKey) ?? const [],
        recents: prefs.getStringList(_recentsKey) ?? const [],
      );
    } catch (_) {
      // Unreadable storage just means no favourites yet.
    }
    if (!ref.mounted) return;
    for (final change in _pending) {
      loaded = change(loaded);
    }
    _loaded = true;
    state = loaded;
    if (_pending.isNotEmpty) {
      _pending.clear();
      _save();
    }
  }

  Future<void> _save() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setStringList(_favouritesKey, state.favourites);
      await prefs.setStringList(_recentsKey, state.recents);
    } catch (_) {}
  }

  void _apply(AppPrefs Function(AppPrefs) change) {
    state = change(state);
    if (_loaded) {
      _save();
    } else {
      _pending.add(change);
    }
  }

  void toggleFavourite(String id) => _apply((p) {
        final favs = [...p.favourites];
        if (!favs.remove(id)) favs.add(id);
        return AppPrefs(favourites: favs, recents: p.recents);
      });

  void recordUse(String id) => _apply((p) => AppPrefs(
        favourites: p.favourites,
        recents: [id, ...p.recents.where((r) => r != id)].take(maxRecents).toList(),
      ));
}

final appPrefsProvider = NotifierProvider<AppPrefsNotifier, AppPrefs>(AppPrefsNotifier.new);
