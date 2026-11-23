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

class AppPrefsNotifier extends Notifier<AppPrefs> {
  bool _touched = false;

  @override
  AppPrefs build() {
    _load();
    return const AppPrefs();
  }

  Future<void> _load() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      if (!ref.mounted || _touched) return;
      state = AppPrefs(
        favourites: prefs.getStringList(_favouritesKey) ?? const [],
        recents: prefs.getStringList(_recentsKey) ?? const [],
      );
    } catch (_) {
      // Unreadable storage just means no favourites yet.
    }
  }

  Future<void> _save() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setStringList(_favouritesKey, state.favourites);
      await prefs.setStringList(_recentsKey, state.recents);
    } catch (_) {}
  }

  void toggleFavourite(String id) {
    _touched = true;
    final favs = [...state.favourites];
    if (!favs.remove(id)) favs.add(id);
    state = AppPrefs(favourites: favs, recents: state.recents);
    _save();
  }

  void recordUse(String id) {
    _touched = true;
    final recents = [id, ...state.recents.where((r) => r != id)].take(maxRecents).toList();
    state = AppPrefs(favourites: state.favourites, recents: recents);
    _save();
  }
}

final appPrefsProvider = NotifierProvider<AppPrefsNotifier, AppPrefs>(AppPrefsNotifier.new);
