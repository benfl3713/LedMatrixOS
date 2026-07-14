import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:dynamic_color/dynamic_color.dart';
import 'package:tofu_expressive/tofu_expressive.dart';
import 'dart:async';
import 'api_service.dart';
import 'widgets/app_settings_bottom_sheet.dart';
import 'widgets/responsive_app_grid.dart';
import 'widgets/audio_stream_widget.dart';
import 'widgets/matrix_control_panel.dart';
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
    return DynamicColorBuilder(
      builder: (lightDynamic, darkDynamic) {
        return MaterialApp(
          title: 'LED Matrix',
          debugShowCheckedModeBanner: false,
          theme: TofuTheme.light(
          seedColor: const Color(0xFF00E5FF),
          ),
          darkTheme: TofuTheme.dark(
          seedColor: const Color(0xFFFF9900),
          ),
        themeMode: ThemeMode.dark,
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

class _HomePageState extends State<HomePage> with WidgetsBindingObserver {
  late LedMatrixApi _api;

  List<MatrixApp> _apps = [];
  String? _activeAppId;
  MatrixSettings? _settings;
  List<AppSetting> _appSettings = [];
  bool _loading = true;
  String? _error;
  // Debounce timers for API calls
  Timer? _brightnessDebounce;
  final Map<String, Timer?> _settingDebounce = {};

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _initializeApi();
    _loadData();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _loadData();
    }
  }

  void _initializeApi() {
    final apiController = Provider.of<ApiSettingsController>(context, listen: false);
    _updateApiInstance(apiController);
    
    // Listen to API settings changes
    apiController.addListener(_onApiSettingsChanged);
  }

  void _updateApiInstance(ApiSettingsController apiController) {
    if (apiController.useMockMode) {
      _api = MockLedMatrixApi();
    } else {
      _api = HttpLedMatrixApi(baseUrl: apiController.apiUrl);
    }
  }

  void _onApiSettingsChanged() {
    final apiController = Provider.of<ApiSettingsController>(context, listen: false);
    _updateApiInstance(apiController);
    _loadData(); // Reload data with new API configuration
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _brightnessDebounce?.cancel();
    for (var timer in _settingDebounce.values) {
      timer?.cancel();
    }
    // Remove API settings change listener
    final apiController = Provider.of<ApiSettingsController>(context, listen: false);
    apiController.removeListener(_onApiSettingsChanged);
    super.dispose();
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
      useSafeArea: true,
      backgroundColor: Colors.transparent,
      builder: (context) => Padding(
        padding: MediaQuery.of(context).viewInsets,
        child: AppSettingsBottomSheet(
          app: app,
          appSettings: _appSettings,
          onUpdateSetting: _updateAppSetting,
          getAppIcon: AppIconHelper.getAppIcon,
        ),
      ),
    );
  }

  MatrixApp? get _activeApp {
    if (_activeAppId == null) return null;
    final matches = _apps.where((a) => a.id == _activeAppId);
    return matches.isEmpty ? null : matches.first;
  }

  Widget _buildLoadingView() {
    final colorScheme = Theme.of(context).colorScheme;
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          SizedBox(
            width: 32,
            height: 32,
            child: CircularProgressIndicator(
              strokeWidth: 2,
              color: colorScheme.primary,
            ),
          ),
          const SizedBox(height: 20),
          Text(
            'CONNECTING...',
            style: TextStyle(
              fontSize: 11,
              fontWeight: FontWeight.w800,
              color: colorScheme.primary,
              letterSpacing: 3,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildErrorView() {
    final colorScheme = Theme.of(context).colorScheme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Container(
              padding: const EdgeInsets.all(20),
              decoration: BoxDecoration(
                color: colorScheme.errorContainer.withValues(alpha: 0.3),
                shape: BoxShape.circle,
                border: Border.all(
                    color: colorScheme.error.withValues(alpha: 0.3), width: 1),
              ),
              child: Icon(
                Icons.wifi_off_rounded,
                size: 48,
                color: colorScheme.error.withValues(alpha: 0.7),
              ),
            ),
            const SizedBox(height: 24),
            Text(
              'NO SIGNAL',
              style: TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w900,
                color: colorScheme.error,
                letterSpacing: 3,
              ),
            ),
            const SizedBox(height: 10),
            Text(
              _error!,
              style: TextStyle(
                fontSize: 12,
                color: colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 32),
            FilledButton.icon(
              onPressed: _loadData,
              icon: const Icon(Icons.refresh_rounded, size: 16),
              label: const Text('RETRY'),
            ),
            TextButton(
                onPressed: () => Navigator.push(
                      context,
                      MaterialPageRoute(
                          builder: (context) => const SettingsPage()),
                    ),
                child: Text('Change Settings'))
          ],
        ),
      ),
    );
  }

  Widget _buildSectionLabel(BuildContext context, String label, int count) {
    final colorScheme = Theme.of(context).colorScheme;
    return Row(
      children: [
        Container(
          width: 3,
          height: 14,
          decoration: BoxDecoration(
            color: colorScheme.primary,
            borderRadius: BorderRadius.circular(2),
            boxShadow: [
              BoxShadow(
                color: colorScheme.primary.withValues(alpha: 0.5),
                blurRadius: 6,
              ),
            ],
          ),
        ),
        const SizedBox(width: 8),
        Text(
          label,
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w800,
            letterSpacing: 2.5,
            color: colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Container(
            height: 1,
            color: colorScheme.outline.withValues(alpha: 0.12),
          ),
        ),
        const SizedBox(width: 8),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
          decoration: BoxDecoration(
            color: colorScheme.primary.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(10),
          ),
          child: Text(
            '$count',
            style: TextStyle(
              fontSize: 10,
              fontWeight: FontWeight.w700,
              color: colorScheme.primary,
            ),
          ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: _loading
          ? _buildLoadingView()
          : _error != null
              ? _buildErrorView()
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    MatrixControlPanel(
                      settings: _settings,
                      activeAppId: _activeAppId,
                      apps: _apps,
                      onPowerToggle: _settings != null
                          ? () => _setPower(!_settings!.isEnabled)
                          : () {},
                      onSettings: () => Navigator.push(
                        context,
                        MaterialPageRoute(
                            builder: (context) => const SettingsPage()),
                      ),
                      onRefresh: _loadData,
                      onBrightnessChanged: _setBrightness,
                      getAppIcon: AppIconHelper.getAppIcon,
                      onConfigureActive: _activeApp != null
                          ? () => _showAppSettingsBottomSheet(_activeApp!)
                          : null,
                    ),
                    Expanded(
                      child: ListView(
                        padding: const EdgeInsets.fromLTRB(12, 16, 12, 32),
                        children: [
                          if (_activeAppId == 'equalizer') ...[
                            const AudioStreamWidget(),
                            const SizedBox(height: 16),
                          ],
                          _buildSectionLabel(context, 'APPS', _apps.length),
                          ResponsiveAppGrid(
                            apps: _apps,
                            activeAppId: _activeAppId,
                            onActivateApp: _activateApp,
                            onShowSettings: _showAppSettingsBottomSheet,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
    );
  }
}
