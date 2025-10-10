import 'package:flutter/material.dart';
import 'dart:async';
import 'api_service.dart';

void main() {
  runApp(const MyApp());
}

class MyApp extends StatelessWidget {
  const MyApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'LED Matrix',
      theme: ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(
          seedColor: Colors.orange,
          brightness: Brightness.light,
        ),
      ),
      darkTheme: ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(
          seedColor: Colors.orange,
          brightness: Brightness.dark,
        ),
      ),
      home: const HomePage(),
    );
  }
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final LedMatrixApi _api = LedMatrixApi(baseUrl: 'http://localhost:5005');

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
    _loadData();
    _startPreviewTimer();
  }

  @override
  void dispose() {
    _previewTimer?.cancel();
    _brightnessDebounce?.cancel();
    for (var timer in _settingDebounce.values) {
      timer?.cancel();
    }
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

      final appsData = futures[0] as Map<String, dynamic>;
      final settingsData = futures[1] as Map<String, dynamic>;

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

  Widget _buildAppCard(MatrixApp app, bool isActive) {
    return Card(
      clipBehavior: Clip.hardEdge,
      child: InkWell(
        onTap: () {
          if (isActive && app.hasSettings) {
            _showAppSettingsBottomSheet(app);
          } else {
            _activateApp(app.id);
          }
        },
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                _getAppIcon(app.id),
                size: 48,
                color: isActive
                    ? Theme.of(context).colorScheme.primary
                    : Theme.of(context).colorScheme.onSurfaceVariant,
              ),
              const SizedBox(height: 12),
              Text(
                app.name,
                style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      color: isActive
                          ? Theme.of(context).colorScheme.primary
                          : Theme.of(context).colorScheme.onSurface,
                    ),
                textAlign: TextAlign.center,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
              if (app.hasSettings || isActive) ...[
                const SizedBox(height: 8),
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    if (app.hasSettings)
                      Icon(
                        Icons.settings,
                        size: 16,
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    if (isActive) ...[
                      if (app.hasSettings) const SizedBox(width: 8),
                      Icon(
                        Icons.check_circle,
                        size: 16,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    ],
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  void _showAppSettingsBottomSheet(MatrixApp app) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (context) => _AppSettingsBottomSheet(
        app: app,
        appSettings: _appSettings,
        onUpdateSetting: _updateAppSetting,
        getAppIcon: _getAppIcon,
      ),
    );
  }

  IconData _getAppIcon(String appId) {
    switch (appId) {
      case 'clock':
      case 'animated-clock':
        return Icons.schedule;
      case 'solid_color':
        return Icons.palette;
      case 'rainbow-spiral':
        return Icons.gradient;
      case 'bouncing-balls':
        return Icons.sports_basketball;
      case 'matrix-rain':
        return Icons.code;
      case 'geometric-patterns':
        return Icons.category;
      case 'dvd-logo':
        return Icons.movie;
      case 'weather':
        return Icons.wb_sunny;
      case 'spotify':
        return Icons.music_note;
      default:
        return Icons.apps;
    }
  }

  Widget _buildSettingInput(AppSetting setting) {
    switch (setting.type) {
      case AppSettingType.boolean:
        return SwitchListTile(
          title: Text(
            setting.currentValue == true ||
                    setting.currentValue.toString().toLowerCase() == 'true'
                ? 'Enabled'
                : 'Disabled',
          ),
          contentPadding: EdgeInsets.zero,
          value: setting.currentValue == true ||
              setting.currentValue.toString().toLowerCase() == 'true',
          onChanged: (value) => _updateAppSetting(setting.key, value),
        );

      case AppSettingType.integer:
        final currentValue = (setting.currentValue is int)
            ? setting.currentValue as int
            : int.tryParse(setting.currentValue.toString()) ?? 0;
        final minValue =
            (setting.minValue is int) ? setting.minValue as int : 0;
        final maxValue =
            (setting.maxValue is int) ? setting.maxValue as int : 100;

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Value: $currentValue',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
            Slider(
              value: currentValue.toDouble(),
              min: minValue.toDouble(),
              max: maxValue.toDouble(),
              divisions: maxValue - minValue,
              label: currentValue.toString(),
              onChanged: (value) =>
                  _updateAppSetting(setting.key, value.round()),
            ),
          ],
        );

      case AppSettingType.select:
        return DropdownButtonFormField<String>(
          value: setting.currentValue.toString(),
          decoration: const InputDecoration(
            border: OutlineInputBorder(),
            contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          ),
          items: (setting.options ?? []).map((option) {
            return DropdownMenuItem(
              value: option,
              child: Text(option),
            );
          }).toList(),
          onChanged: (value) {
            if (value != null) _updateAppSetting(setting.key, value);
          },
        );

      default:
        return TextFormField(
          initialValue: setting.currentValue.toString(),
          decoration: const InputDecoration(
            border: OutlineInputBorder(),
            contentPadding: EdgeInsets.all(12),
          ),
          onChanged: (value) => _updateAppSetting(setting.key, value),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Led Matrix'),
        scrolledUnderElevation: 0,
        leading: IconButton(
          icon: const Icon(Icons.menu),
          onPressed: () {
            // TODO: Implement menu
          },
        ),
        actions: [
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
                    Card(
                      clipBehavior: Clip.hardEdge,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Padding(
                            padding: const EdgeInsets.all(16),
                            child: Text(
                              'Live Preview',
                              style: Theme.of(context).textTheme.titleLarge,
                            ),
                          ),
                          SizedBox(
                            height: 200,
                            child: Image.network(
                              '${_api.getPreviewUrl()}&key=$_previewImageKey',
                              gaplessPlayback: true,
                              fit: BoxFit.contain,
                              filterQuality: FilterQuality.none,
                              errorBuilder: (context, error, stackTrace) {
                                return const Center(
                                  child: Column(
                                    mainAxisAlignment: MainAxisAlignment.center,
                                    children: [
                                      Icon(Icons.image_not_supported, size: 48),
                                      SizedBox(height: 8),
                                      Text('Preview not available'),
                                      Text('(Simulator mode only)'),
                                    ],
                                  ),
                                );
                              },
                              loadingBuilder:
                                  (context, child, loadingProgress) {
                                if (loadingProgress == null) return child;
                                return const Center(
                                    child: CircularProgressIndicator());
                              },
                            ),
                          ),
                        ],
                      ),
                    ),

                    const SizedBox(height: 16),

                    // Apps Section
                    Text(
                      'Applications',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 8),
                    LayoutBuilder(
                      builder: (context, constraints) {
                        // Calculate number of columns based on screen width
                        final screenWidth = constraints.maxWidth;
                        int crossAxisCount;

                        if (screenWidth > 1200) {
                          crossAxisCount = 6; // Large desktop
                        } else if (screenWidth > 900) {
                          crossAxisCount = 5; // Desktop
                        } else if (screenWidth > 700) {
                          crossAxisCount = 4; // Tablet landscape
                        } else if (screenWidth > 500) {
                          crossAxisCount = 3; // Tablet portrait
                        } else {
                          crossAxisCount = 2; // Mobile
                        }

                        return GridView.builder(
                          shrinkWrap: true,
                          physics: const NeverScrollableScrollPhysics(),
                          gridDelegate:
                              SliverGridDelegateWithFixedCrossAxisCount(
                            crossAxisCount: crossAxisCount,
                            childAspectRatio: 1.1,
                            crossAxisSpacing: 12,
                            mainAxisSpacing: 12,
                          ),
                          itemCount: _apps.length,
                          itemBuilder: (context, index) {
                            final app = _apps[index];
                            final isActive = app.id == _activeAppId;
                            return _buildAppCard(app, isActive);
                          },
                        );
                      },
                    ),

                    const SizedBox(height: 16),

                    // Display Settings
                    if (_settings != null) ...[
                      Text(
                        'Display Settings',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 8),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                mainAxisAlignment:
                                    MainAxisAlignment.spaceBetween,
                                children: [
                                  Text(
                                    'Resolution: ${_settings!.width} x ${_settings!.height}',
                                    style:
                                        Theme.of(context).textTheme.bodyLarge,
                                  ),
                                  Chip(
                                    label: Text(_settings!.isRunning
                                        ? 'Running'
                                        : 'Stopped'),
                                    avatar: Icon(
                                      _settings!.isRunning
                                          ? Icons.play_circle
                                          : Icons.stop_circle,
                                      size: 16,
                                    ),
                                  ),
                                ],
                              ),
                              const Divider(height: 32),
                              Text('Brightness: ${_settings!.brightness}%'),
                              Slider(
                                value: _settings!.brightness.toDouble(),
                                min: 0,
                                max: 100,
                                divisions: 100,
                                label: '${_settings!.brightness}%',
                                onChanged: _setBrightness,
                              ),
                            ],
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
    );
  }
}

class _AppSettingsBottomSheet extends StatefulWidget {
  final MatrixApp app;
  final List<AppSetting> appSettings;
  final Function(String key, dynamic value) onUpdateSetting;
  final IconData Function(String appId) getAppIcon;

  const _AppSettingsBottomSheet({
    required this.app,
    required this.appSettings,
    required this.onUpdateSetting,
    required this.getAppIcon,
  });

  @override
  State<_AppSettingsBottomSheet> createState() =>
      _AppSettingsBottomSheetState();
}

class _AppSettingsBottomSheetState extends State<_AppSettingsBottomSheet> {
  late List<AppSetting> _localSettings;

  @override
  void initState() {
    super.initState();
    _localSettings = List.from(widget.appSettings);
  }

  @override
  void didUpdateWidget(_AppSettingsBottomSheet oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.appSettings != oldWidget.appSettings) {
      setState(() {
        _localSettings = List.from(widget.appSettings);
      });
    }
  }

  void _updateLocalSetting(String key, dynamic value) {
    setState(() {
      final settingIndex = _localSettings.indexWhere((s) => s.key == key);
      if (settingIndex >= 0) {
        final setting = _localSettings[settingIndex];
        _localSettings[settingIndex] = AppSetting(
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

    // Also update the parent
    widget.onUpdateSetting(key, value);
  }

  Widget _buildSettingWidget(AppSetting setting) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (setting.type == AppSettingType.boolean) ...[
            // Inline layout for boolean settings
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        setting.name,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      if (setting.description.isNotEmpty) ...[
                        const SizedBox(height: 4),
                        Text(
                          setting.description,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ],
                  ),
                ),
                const SizedBox(width: 16),
                Switch(
                  value: setting.currentValue == true ||
                      setting.currentValue.toString().toLowerCase() == 'true',
                  onChanged: (value) => _updateLocalSetting(setting.key, value),
                ),
              ],
            ),
          ] else ...[
            // Regular layout for other setting types
            Text(
              setting.name,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            if (setting.description.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(
                setting.description,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
            const SizedBox(height: 8),
            _buildSettingInput(setting),
          ],
        ],
      ),
    );
  }

  Widget _buildSettingInput(AppSetting setting) {
    switch (setting.type) {
      case AppSettingType.integer:
        final currentValue = (setting.currentValue is int)
            ? setting.currentValue as int
            : int.tryParse(setting.currentValue.toString()) ?? 0;
        final minValue =
            (setting.minValue is int) ? setting.minValue as int : 0;
        final maxValue =
            (setting.maxValue is int) ? setting.maxValue as int : 100;

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Value: $currentValue',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
            Slider(
              value: currentValue.toDouble(),
              min: minValue.toDouble(),
              max: maxValue.toDouble(),
              divisions: maxValue - minValue,
              label: currentValue.toString(),
              onChanged: (value) =>
                  _updateLocalSetting(setting.key, value.round()),
            ),
          ],
        );

      case AppSettingType.select:
        return DropdownButtonFormField<String>(
          value: setting.currentValue.toString(),
          decoration: const InputDecoration(
            border: OutlineInputBorder(),
            contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          ),
          items: (setting.options ?? []).map((option) {
            return DropdownMenuItem(
              value: option,
              child: Text(option),
            );
          }).toList(),
          onChanged: (value) {
            if (value != null) _updateLocalSetting(setting.key, value);
          },
        );

      default:
        return TextFormField(
          initialValue: setting.currentValue.toString(),
          decoration: const InputDecoration(
            border: OutlineInputBorder(),
            contentPadding: EdgeInsets.all(12),
          ),
          onChanged: (value) => _updateLocalSetting(setting.key, value),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    return DraggableScrollableSheet(
      initialChildSize: 0.6,
      minChildSize: 0.3,
      maxChildSize: 0.9,
      expand: false,
      builder: (context, scrollController) => Container(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Handle bar
            Center(
              child: Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: Theme.of(context)
                      .colorScheme
                      .onSurfaceVariant
                      .withOpacity(0.4),
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 16),

            // Header
            Row(
              children: [
                Icon(
                  widget.getAppIcon(widget.app.id),
                  size: 32,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        widget.app.name,
                        style: Theme.of(context).textTheme.headlineSmall,
                      ),
                      Text(
                        'App Settings',
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                              color: Theme.of(context)
                                  .colorScheme
                                  .onSurfaceVariant,
                            ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 24),

            // Settings content
            Expanded(
              child: _localSettings.isEmpty
                  ? Center(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(
                            Icons.settings_outlined,
                            size: 64,
                            color: Theme.of(context)
                                .colorScheme
                                .onSurfaceVariant
                                .withOpacity(0.5),
                          ),
                          const SizedBox(height: 16),
                          Text(
                            'No settings available',
                            style: Theme.of(context)
                                .textTheme
                                .titleMedium
                                ?.copyWith(
                                  color: Theme.of(context)
                                      .colorScheme
                                      .onSurfaceVariant,
                                ),
                          ),
                        ],
                      ),
                    )
                  : ListView(
                      controller: scrollController,
                      children: _localSettings
                          .map((setting) => _buildSettingWidget(setting))
                          .toList(),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}
