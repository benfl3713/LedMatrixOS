import 'package:flutter/material.dart';
import 'package:ledmatrix/services/api.dart';

class HomeViewModel extends ChangeNotifier {
  HomeViewModel() {
    loadingApps = true;
    reload();
  }

  final _api = LedMatrixApi(baseUrl: 'http://localhost:5172');
  List<MatrixApp> apps = [];
  bool loadingApps = false;
  String? error = null;

  bool get poweredOn => true;

  Future<dynamic> reload() async {
    error = null;
    notifyListeners();
    return Future.wait([fetchApps()]);
  }

  Future<void> fetchApps() async {
    // try {
    //   apps = await _api.getApps();
    // } on Exception catch (e) {
    //   error = e.toString();
    // }
    apps = [MatrixApp(id: 't', name: 'Fred', hasSettings: false)];
    loadingApps = false;
    notifyListeners();
  }
}
