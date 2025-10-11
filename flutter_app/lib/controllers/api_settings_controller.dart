import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

class ApiSettingsController extends ChangeNotifier {
  static const String _apiUrlKey = 'api_url';
  static const String _defaultApiUrl = 'http://localhost:5005';

  String _apiUrl = _defaultApiUrl;

  String get apiUrl => _apiUrl;

  ApiSettingsController() {
    _loadApiUrl();
  }

  Future<void> _loadApiUrl() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      _apiUrl = prefs.getString(_apiUrlKey) ?? _defaultApiUrl;
      notifyListeners();
    } catch (e) {
      if (kDebugMode) {
        print('Error loading API URL: $e');
      }
    }
  }

  Future<void> updateApiUrl(String newUrl) async {
    if (_apiUrl == newUrl) return;

    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_apiUrlKey, newUrl);
      _apiUrl = newUrl;
      notifyListeners();
    } catch (e) {
      if (kDebugMode) {
        print('Error saving API URL: $e');
      }
    }
  }

  Future<void> resetToDefault() async {
    await updateApiUrl(_defaultApiUrl);
  }
}
