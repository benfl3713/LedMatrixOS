import 'package:flutter/material.dart';
import 'package:ledmatrix/models/matrix_app.dart';
import 'package:ledmatrix/models/matrix_settings.dart';
import 'package:ledmatrix/services/api.dart';

class MatrixViewModel extends ChangeNotifier {
   final LedMatrixApi _api = LedMatrixApi();
   MatrixSettings? _matrixSettings;
   InstalledApps? _installedApps;
 
   MatrixSettings? get matrixSettings => _matrixSettings;
   List<MatrixApp>? get installedApps => _installedApps?.apps;
   MatrixApp? get activeApp => _installedApps?.apps.firstWhere((app) => app.id == _installedApps?.activeApp);

   Future<void> initialize() {
    return Future.wait([
      fetchMatrixSettings(),
      fetchInstalledApps(),
    ]);
   }
 
   Future<void> fetchMatrixSettings() async {
    print('Fetching matrix settings...');
     try {
       _matrixSettings = await _api.getMatrixSettings();
       notifyListeners();
     } catch (e) {
       print('Error fetching matrix settings: $e');
     }
   }

  void setIsEnabled(bool value) async {
    try {
      _matrixSettings?.isEnabled = value;
      notifyListeners();
      await _api.setIsEnabled(value);
    } catch (e) {
      _matrixSettings?.isEnabled = !value;
      notifyListeners();
      print('Error setting isEnabled: $e');
    }
  }

  void setBrightness(int value) async {
    try {
      await _api.setBrightness(value);
      _matrixSettings?.brightness = value;
      notifyListeners();
    } catch (e) {
      print('Error setting brightness: $e');
    }
  }

  Future<void> fetchInstalledApps() async {
    try {
      _installedApps = await _api.getInstalledApps();
      notifyListeners();
    } catch (e) {
      print('Error fetching installed apps: $e');
    }
  }

  Future<void> setActiveApp(String appId) async {
    try {
      await _api.setActiveApp(appId);
    } catch (e) {
      print('Error setting active app: $e');
    }
  }
}
