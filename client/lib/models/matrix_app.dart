class InstalledApps {
  final List<MatrixApp> apps;
  final String activeApp;

  InstalledApps({required this.apps, required this.activeApp});

  factory InstalledApps.fromJson(Map<String, dynamic> json) {

    var appsJson = json['apps'] as List<dynamic>;
    var appsList = appsJson
        .map((appJson) => MatrixApp.fromJson(appJson))
        .toList();
    var activeApp = json['activeApp'] as String;
    return InstalledApps(apps: appsList, activeApp: activeApp);
  }
}

class MatrixApp {
  final String id;
  final String name;

  MatrixApp({required this.id, required this.name});

  factory MatrixApp.fromJson(Map<String, dynamic> json) {
    return MatrixApp(id: json['id'], name: json['name']);
  }
}
