import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:dynamic_color/dynamic_color.dart';
import 'package:tofu_expressive/tofu_expressive.dart';
import 'dart:async';
import 'api_service.dart';
import 'widgets/app_settings_bottom_sheet.dart';
import 'widgets/live_preview_widget.dart';
import 'widgets/responsive_app_grid.dart';
import 'widgets/display_settings_widget.dart';
import 'widgets/audio_stream_widget.dart';
import 'utils/app_icon_helper.dart';
import 'controllers/api_settings_controller.dart';
import 'pages/settings_page.dart';

void main() {
  runApp(
    MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => ThemeController()),
        ChangeNotifierProvider(create: (_) => ApiSettingsController()),
      ],
      child: const MyApp(),
    ),
  );
}

class MyApp extends StatelessWidget {
  const MyApp({super.key});

  @override
  Widget build(BuildContext context) {
    final themeController = Provider.of<ThemeController>(context);

    return DynamicColorBuilder(
      builder: (lightDynamic, darkDynamic) {
        return MaterialApp(
          title: 'LED Matrix',
          debugShowCheckedModeBanner: false,
          theme: TofuTheme.light(
            seedColor: themeController.seedColor,
          ),
          darkTheme: TofuTheme.dark(
            seedColor: themeController.seedColor,
          ),
          themeMode: themeController.themeMode,
          home: const HomePage(),
        );
      }
    );
  }
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  late LedMatrixApi _api;

  List<MatrixApp> _apps = [];
  String? _activeAppId;
  MatrixSettings? _settings;
  List<AppSetting> _appSettings = [];
  bool _loading = true;
  String? _error;
  Timer? _previewTimer;
  String _previewImageKey = '';

  // Debounce timers for API calls
  Timer? _brightnessDebounce;
  final Map<String, Timer?> _settingDebounce = {};

  @override
  void initState() {
    super.initState();
    _initializeApi();
    _loadData();
    _startPreviewTimer();
  }

  void _initializeApi() {
    final apiController = Provider.of<ApiSettingsController>(context, listen: false);
    _api = LedMatrixApi(baseUrl: apiController.apiUrl);
    
    // Listen to API URL changes
    apiController.addListener(_onApiUrlChanged);
  }

  void _onApiUrlChanged() {
    final apiController = Provider.of<ApiSettingsController>(context, listen: false);
    _api = LedMatrixApi(baseUrl: apiController.apiUrl);
    _loadData(); // Reload data with new API URL
  }

  @override
  void dispose() {
    _previewTimer?.cancel();
    _brightnessDebounce?.cancel();
    for (var timer in _settingDebounce.values) {
      timer?.cancel();
    }
    // Remove API URL change listener
    final apiController = Provider.of<ApiSettingsController>(context, listen: false);
    apiController.removeListener(_onApiUrlChanged);
    super.dispose();
  }

  void _startPreviewTimer() {
    _previewTimer = Timer.periodic(const Duration(milliseconds: 200), (timer) {
      if (mounted) {
        setState(() {
          _previewImageKey = DateTime.now().millisecondsSinceEpoch.toString();
        });
      }
    });
  }

  Future<void> _loadData() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final futures = await Future.wait([
        _api.getApps(),
        _api.getSettings(),
      ]);

      final appsData = futures[0];
      final settingsData = futures[1];

      setState(() {
        _apps = (appsData['apps'] as List)
            .map((app) => MatrixApp.fromJson(app))
            .toList();
        _activeAppId = appsData['activeApp'];
        _settings = MatrixSettings.fromJson(settingsData);
        _loading = false;
      });

      if (_activeAppId != null) {
        _loadAppSettings(_activeAppId!);
      }
    } catch (e) {
      setState(() {
        _error = e.toString();
        _loading = false;
      });
    }
  }

  Future<void> _loadAppSettings(String appId) async {
    try {
      final settingsData = await _api.getAppSettings(appId);
      if (settingsData != null && settingsData['settings'] != null) {
        setState(() {
          _appSettings = (settingsData['settings'] as List)
              .map((setting) => AppSetting.fromJson(setting))
              .toList();
        });
      } else {
        setState(() {
          _appSettings = [];
        });
      }
    } catch (e) {
      setState(() {
        _appSettings = [];
      });
    }
  }

  Future<void> _activateApp(String appId) async {
    try {
      final success = await _api.activateApp(appId);
      if (success) {
        setState(() {
          _activeAppId = appId;
          _appSettings = [];
        });
        _loadAppSettings(appId);
      }
    } catch (e) {
      // Silent error handling
    }
  }

  Future<void> _updateAppSetting(String key, dynamic value) async {
    if (_activeAppId == null) return;

    // Update UI immediately for responsiveness
    setState(() {
      final settingIndex = _appSettings.indexWhere((s) => s.key == key);
      if (settingIndex >= 0) {
        final setting = _appSettings[settingIndex];
        _appSettings[settingIndex] = AppSetting(
          key: setting.key,
          name: setting.name,
          description: setting.description,
          type: setting.type,
          defaultValue: setting.defaultValue,
          currentValue: value,
          minValue: setting.minValue,
          maxValue: setting.maxValue,
          options: setting.options,
        );
      }
    });

    // Cancel existing debounce timer for this setting
    _settingDebounce[key]?.cancel();

    // Set new debounce timer
    _settingDebounce[key] = Timer(const Duration(milliseconds: 500), () async {
      try {
        await _api.updateAppSettings(_activeAppId!, {key: value});
      } catch (e) {
        // Silent error handling
      }
    });
  }

  Future<void> _setBrightness(double brightness) async {
    // Update UI immediately for responsiveness
    setState(() {
      _settings = MatrixSettings(
        width: _settings!.width,
        height: _settings!.height,
        brightness: brightness.round(),
        isRunning: _settings!.isRunning,
        isEnabled: _settings!.isEnabled,
      );
    });

    // Cancel existing debounce timer
    _brightnessDebounce?.cancel();

    // Set new debounce timer
    _brightnessDebounce = Timer(const Duration(milliseconds: 500), () async {
      try {
        await _api.setBrightness(brightness.round());
      } catch (e) {
        // Silent error handling
      }
    });
  }

  Future<void> _setPower(bool enabled) async {
    // Update UI immediately for responsiveness
    setState(() {
      _settings = MatrixSettings(
        width: _settings!.width,
        height: _settings!.height,
        brightness: _settings!.brightness,
        isRunning: _settings!.isRunning,
        isEnabled: enabled,
      );
    });

    try {
      await _api.setPower(enabled);
    } catch (e) {
      // Silent error handling, revert UI state on error
      setState(() {
        _settings = MatrixSettings(
          width: _settings!.width,
          height: _settings!.height,
          brightness: _settings!.brightness,
          isRunning: _settings!.isRunning,
          isEnabled: !enabled,
        );
      });
    }
  }

  void _showAppSettingsBottomSheet(MatrixApp app) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (context) => AppSettingsBottomSheet(
        app: app,
        appSettings: _appSettings,
        onUpdateSetting: _updateAppSetting,
        getAppIcon: AppIconHelper.getAppIcon,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Led Matrix'),
        scrolledUnderElevation: 0,
        actions: [
          if (_settings != null)
            IconButton(
              icon: Icon(
                _settings!.isEnabled ? Icons.power_settings_new : Icons.power_off,
                color: _settings!.isEnabled 
                  ? Theme.of(context).colorScheme.primary
                  : Theme.of(context).colorScheme.onSurfaceVariant,
              ),
              onPressed: () => _setPower(!_settings!.isEnabled),
              tooltip: _settings!.isEnabled ? 'Turn Off Display' : 'Turn On Display',
            ),
          IconButton(
            icon: const Icon(Icons.settings),
            onPressed: () {
              Navigator.push(
                context,
                MaterialPageRoute(
                  builder: (context) => const SettingsPage(),
                ),
              );
            },
          ),
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _loadData,
          ),
        ],
      ),
      body: _loading
          ? const Center(
              child: CircularProgressIndicator(),
            )
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.error_outline, size: 64),
                      const SizedBox(height: 16),
                      Text('Error: $_error'),
                      const SizedBox(height: 24),
                      FilledButton.icon(
                        onPressed: _loadData,
                        icon: const Icon(Icons.refresh),
                        label: const Text('Retry'),
                      ),
                    ],
                  ),
                )
              : ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    // Preview Card
                    LivePreviewWidget(
                      api: _api,
                      previewImageKey: _previewImageKey,
                    ),


                    // Audio Streaming Widget (only show for equalizer app)
                    if (_activeAppId == 'equalizer') ...[
                      const AudioStreamWidget(),
                      const SizedBox(height: 16),
                    ],
                    const SizedBox(height: 16),

                    // Apps Section
                    Text(
                      'Applications',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 8),
                    ResponsiveAppGrid(
                      apps: _apps,
                      activeAppId: _activeAppId,
                      onActivateApp: _activateApp,
                      onShowSettings: _showAppSettingsBottomSheet,
                    ),

                    const SizedBox(height: 16),

                    // Display Settings
                    if (_settings != null) ...[
                      Text(
                        'Display Settings',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 8),
                      DisplaySettingsWidget(
                        settings: _settings!,
                        onBrightnessChanged: _setBrightness,
                      ),
                    ],
                  ],
                ),
    );
  }
}
