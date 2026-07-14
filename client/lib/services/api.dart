import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:ledmatrix/models/matrix_app.dart';
import 'package:ledmatrix/models/matrix_settings.dart';

class LedMatrixApi {
  final String _baseUrl = "http://radarlights:5005";

  Future<MatrixSettings> getMatrixSettings() async {
    final response = await http.get(Uri.parse("$_baseUrl/api/settings"));

    if (response.statusCode == 200) {
      print('Matrix settings fetched successfully: ${response.body}');
      return MatrixSettings.fromJson(jsonDecode(response.body));
    } else {
      print('Failed to load matrix settings: ${response.statusCode}');
      throw Exception('Failed to load matrix settings');
    }
  }

  Future<void> setIsEnabled(bool isEnabled) async {
    final response = await http.post(
      Uri.parse("$_baseUrl/api/settings/power/$isEnabled"),
      headers: {'Content-Type': 'application/json'},
    );

    if (response.statusCode != 200) {
      throw Exception('Failed to update isEnabled');
    }
  }

  Future<void> setBrightness(int brightness) async {
    final response = await http.post(
      Uri.parse("$_baseUrl/api/settings/brightness/$brightness"),
      headers: {'Content-Type': 'application/json'},
    );

    if (response.statusCode != 200) {
      throw Exception('Failed to update brightness');
    }
  }

  Future<InstalledApps> getInstalledApps() async {
    final response = await http.get(Uri.parse("$_baseUrl/api/apps"));

    if (response.statusCode == 200) {
      Map<String, dynamic> appsJson = jsonDecode(response.body);
      return InstalledApps.fromJson(appsJson);
    } else {
      throw Exception('Failed to load installed apps');
    }
  }

  Future<void> setActiveApp(String appId) async {
    final response = await http.post(
      Uri.parse("$_baseUrl/api/apps/$appId"),
      headers: {'Content-Type': 'application/json'},
    );

    if (response.statusCode != 200) {
      throw Exception('Failed to set active app');
    }
  }
}
