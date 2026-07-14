import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

class ApiSettingsController extends ChangeNotifier {
  static const String _apiUrlKey = 'api_url';
  static const String _useMockModeKey = 'use_mock_mode';
  static const String _defaultApiUrl = 'http://localhost:5005';

  String _apiUrl = _defaultApiUrl;
  bool _useMockMode = false;

  String get apiUrl => _apiUrl;
  bool get useMockMode => _useMockMode;

  ApiSettingsController() {
    _loadSettings();
  }

  Future<void> _loadSettings() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      _apiUrl = prefs.getString(_apiUrlKey) ?? _defaultApiUrl;
      _useMockMode = prefs.getBool(_useMockModeKey) ?? false;
      notifyListeners();
    } catch (e) {
      if (kDebugMode) {
        print('Error loading settings: $e');
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

  Future<void> updateUseMockMode(bool value) async {
    if (_useMockMode == value) return;

    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(_useMockModeKey, value);
      _useMockMode = value;
      notifyListeners();
    } catch (e) {
      if (kDebugMode) {
        print('Error saving mock mode setting: $e');
      }
    }
  }

  Future<void> resetToDefault() async {
    await updateApiUrl(_defaultApiUrl);
    await updateUseMockMode(false);
  }
}
