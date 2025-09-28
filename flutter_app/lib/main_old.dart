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
      title: 'LED Matrix Controller',
      theme: ThemeData(
        primarySwatch: Colors.blue,
        useMaterial3: true,
        cardTheme: CardTheme(
          elevation: 4,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
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

class _HomePageState extends State<HomePage> with TickerProviderStateMixin {
  final LedMatrixApi _api = LedMatrixApi(baseUrl: 'http://radarlights:5005');
  
  List<MatrixApp> _apps = [];
  String? _activeAppId;
  MatrixSettings? _settings;
  List<AppSetting> _appSettings = [];
  bool _loading = true;
  String? _error;
  Timer? _previewTimer;
  String _previewImageKey = '';
  late PageController _pageController;
  int _currentAppPage = 0;
  
  @override
  void initState() {
    super.initState();
    _pageController = PageController(viewportFraction: 0.85);
    _loadData();
    _startPreviewTimer();
  }
  
  @override
  void dispose() {
    _previewTimer?.cancel();
    _pageController.dispose();
    super.dispose();
  }
  
  void _startPreviewTimer() {
    _previewTimer = Timer.periodic(const Duration(milliseconds: 100), (timer) {
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
      
      // Load app settings if current app has settings
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
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Activated: ${_apps.firstWhere((a) => a.id == appId).name}')),
        );
        
        // Load settings for the new app
        _loadAppSettings(appId);
      } else {
        throw Exception('Failed to activate app');
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red),
      );
    }
  }
  
  Future<void> _updateAppSetting(String key, dynamic value) async {
    if (_activeAppId == null) return;
    
    try {
      final success = await _api.updateAppSettings(_activeAppId!, {key: value});
      if (success) {
        // Update local setting value
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
        
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Updated $key'), duration: Duration(milliseconds: 800)),
        );
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error updating setting: $e'), backgroundColor: Colors.red),
      );
    }
  }
  
  Future<void> _setBrightness(double brightness) async {
    try {
      final success = await _api.setBrightness(brightness.round());
      if (success) {
        setState(() {
          _settings = MatrixSettings(
            width: _settings!.width,
            height: _settings!.height,
            brightness: brightness.round(),
            fps: _settings!.fps,
            isRunning: _settings!.isRunning,
          );
        });
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error setting brightness: $e'), backgroundColor: Colors.red),
      );
    }
  }
  
  Future<void> _setFps(double fps) async {
    try {
      final success = await _api.setFps(fps.round());
      if (success) {
        setState(() {
          _settings = MatrixSettings(
            width: _settings!.width,
            height: _settings!.height,
            brightness: _settings!.brightness,
            fps: fps.round(),
            isRunning: _settings!.isRunning,
          );
        });
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error setting FPS: $e'), backgroundColor: Colors.red),
      );
    }
  }

  Widget _buildAppCard(MatrixApp app, bool isActive) {
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => _activateApp(app.id),
        child: Container(
          padding: const EdgeInsets.all(20),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(12),
            gradient: isActive
                ? LinearGradient(
                    colors: [
                      Theme.of(context).primaryColor.withOpacity(0.8),
                      Theme.of(context).primaryColor,
                    ],
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                  )
                : null,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                _getAppIcon(app.id),
                size: 48,
                color: isActive ? Colors.white : Theme.of(context).primaryColor,
              ),
              const SizedBox(height: 12),
              Text(
                app.name,
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                  color: isActive ? Colors.white : null,
                ),
                textAlign: TextAlign.center,
              ),
              if (app.hasSettings) ...[
                const SizedBox(height: 8),
                Icon(
                  Icons.settings,
                  size: 16,
                  color: isActive ? Colors.white70 : Colors.grey,
                ),
              ],
              if (isActive) ...[
                const SizedBox(height: 8),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.2),
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: const Text(
                    'ACTIVE',
                    style: TextStyle(
                      color: Colors.white,
                      fontSize: 12,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
  
  IconData _getAppIcon(String appId) {
    switch (appId) {
      case 'clock':
      case 'animated-clock':
        return Icons.access_time;
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
  
  Widget _buildAppSettingsCard() {
    if (_appSettings.isEmpty) return const SizedBox.shrink();
    
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.tune, color: Theme.of(context).primaryColor),
                const SizedBox(width: 8),
                Text(
                  'App Settings',
                  style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                ),
              ],
            ),
            const SizedBox(height: 16),
            ..._appSettings.map((setting) => _buildSettingWidget(setting)).toList(),
          ],
        ),
      ),
    );
  }
  
  Widget _buildSettingWidget(AppSetting setting) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            setting.name,
            style: const TextStyle(fontWeight: FontWeight.bold),
          ),
          Text(
            setting.description,
            style: TextStyle(color: Colors.grey[600], fontSize: 12),
          ),
          const SizedBox(height: 8),
          _buildSettingInput(setting),
        ],
      ),
    );
  }
  
  Widget _buildSettingInput(AppSetting setting) {
    switch (setting.type) {
      case AppSettingType.boolean:
        return SwitchListTile(
          title: const Text(''),
          contentPadding: EdgeInsets.zero,
          value: setting.currentValue == true || setting.currentValue.toString().toLowerCase() == 'true',
          onChanged: (value) => _updateAppSetting(setting.key, value),
        );
      
      case AppSettingType.integer:
        final currentValue = (setting.currentValue is int) 
            ? setting.currentValue as int 
            : int.tryParse(setting.currentValue.toString()) ?? 0;
        final minValue = (setting.minValue is int) ? setting.minValue as int : 0;
        final maxValue = (setting.maxValue is int) ? setting.maxValue as int : 100;
        
        return Column(
          children: [
            Text('${setting.name}: $currentValue'),
            Slider(
              value: currentValue.toDouble(),
              min: minValue.toDouble(),
              max: maxValue.toDouble(),
              divisions: maxValue - minValue,
              label: currentValue.toString(),
              onChanged: (value) => _updateAppSetting(setting.key, value.round()),
            ),
          ],
        );
      
      case AppSettingType.select:
        return DropdownButton<String>(
          value: setting.currentValue.toString(),
          isExpanded: true,
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
          onChanged: (value) => _updateAppSetting(setting.key, value),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('LED Matrix Controller'),
        backgroundColor: Theme.of(context).colorScheme.inversePrimary,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _loadData,
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.error, size: 64, color: Colors.red),
                      const SizedBox(height: 16),
                      Text('Error: $_error', style: TextStyle(color: Colors.red)),
                      const SizedBox(height: 16),
                      ElevatedButton(
                        onPressed: _loadData,
                        child: const Text('Retry'),
                      ),
                    ],
                  ),
                )
              : SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // Preview Card
                      Card(
                        margin: const EdgeInsets.all(16),
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                children: [
                                  Icon(Icons.monitor, color: Theme.of(context).primaryColor),
                                  const SizedBox(width: 8),
                                  const Text(
                                    'LED Matrix Preview',
                                    style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 12),
                              Container(
                                width: double.infinity,
                                height: 200,
                                decoration: BoxDecoration(
                                  border: Border.all(color: Colors.grey),
                                  borderRadius: BorderRadius.circular(8),
                                ),
                                child: ClipRRect(
                                  borderRadius: BorderRadius.circular(8),
                                  child: Image.network(
                                    '${_api.getPreviewUrl()}&key=$_previewImageKey',
                                    fit: BoxFit.contain,
                                    filterQuality: FilterQuality.none,
                                    errorBuilder: (context, error, stackTrace) {
                                      return const Center(
                                        child: Column(
                                          mainAxisAlignment: MainAxisAlignment.center,
                                          children: [
                                            Icon(Icons.image_not_supported, size: 48, color: Colors.grey),
                                            SizedBox(height: 8),
                                            Text('Preview not available\n(Simulator mode only)', textAlign: TextAlign.center),
                                          ],
                                        ),
                                      );
                                    },
                                    loadingBuilder: (context, child, loadingProgress) {
                                      if (loadingProgress == null) return child;
                                      return const Center(child: CircularProgressIndicator());
                                    },
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      
                      // Apps Carousel Section
                      Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Icon(Icons.apps, color: Theme.of(context).primaryColor),
                                const SizedBox(width: 8),
                                const Text(
                                  'Display Applications',
                                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                                ),
                              ],
                            ),
                            const SizedBox(height: 16),
                            SizedBox(
                              height: 200,
                              child: PageView.builder(
                                controller: _pageController,
                                onPageChanged: (page) {
                                  setState(() {
                                    _currentAppPage = page;
                                  });
                                },
                                itemCount: _apps.length,
                                itemBuilder: (context, index) {
                                  final app = _apps[index];
                                  final isActive = app.id == _activeAppId;
                                  return _buildAppCard(app, isActive);
                                },
                              ),
                            ),
                            const SizedBox(height: 16),
                            // Page indicators
                            Row(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: List.generate(
                                _apps.length,
                                (index) => Container(
                                  width: 8,
                                  height: 8,
                                  margin: const EdgeInsets.symmetric(horizontal: 4),
                                  decoration: BoxDecoration(
                                    color: index == _currentAppPage
                                        ? Theme.of(context).primaryColor
                                        : Colors.grey.withOpacity(0.5),
                                    shape: BoxShape.circle,
                                  ),
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                      
                      // App Settings Card
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 16),
                        child: _buildAppSettingsCard(),
                      ),
                      
                      // Display Settings Card
                      Card(
                        margin: const EdgeInsets.all(16),
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                children: [
                                  Icon(Icons.display_settings, color: Theme.of(context).primaryColor),
                                  const SizedBox(width: 8),
                                  const Text(
                                    'Display Settings',
                                    style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 12),
                              if (_settings != null) ...[
                                Row(
                                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                  children: [
                                    Text('Resolution: ${_settings!.width} x ${_settings!.height}'),
                                    Container(
                                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                                      decoration: BoxDecoration(
                                        color: _settings!.isRunning ? Colors.green : Colors.red,
                                        borderRadius: BorderRadius.circular(12),
                                      ),
                                      child: Text(
                                        _settings!.isRunning ? 'RUNNING' : 'STOPPED',
                                        style: const TextStyle(color: Colors.white, fontSize: 12),
                                      ),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 16),
                                
                                // Brightness Slider
                                Text('Brightness: ${_settings!.brightness}%'),
                                Slider(
                                  value: _settings!.brightness.toDouble(),
                                  min: 0,
                                  max: 100,
                                  divisions: 100,
                                  label: '${_settings!.brightness}%',
                                  onChanged: _setBrightness,
                                ),
                                
                                const SizedBox(height: 16),
                                
                                // FPS Slider
                                Text('FPS: ${_settings!.fps}'),
                                Slider(
                                  value: _settings!.fps.toDouble(),
                                  min: 1,
                                  max: 120,
                                  divisions: 119,
                                  label: '${_settings!.fps}',
                                  onChanged: _setFps,
                                ),
                              ],
                            ],
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
    );
  }
}
