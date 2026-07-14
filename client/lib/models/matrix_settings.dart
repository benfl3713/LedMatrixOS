class MatrixSettings {
  bool isEnabled;
  int brightness;

  MatrixSettings({required this.isEnabled, required this.brightness});

  Map<String, dynamic> toJson() {
    return {
      'isEnabled': isEnabled,
      'brightness': brightness,
    };
  }

  factory MatrixSettings.fromJson(Map<String, dynamic> json) {
    return MatrixSettings(
      isEnabled: json['isEnabled'],
      brightness: json['brightness'],
    );
  }
}
