import 'package:flutter/material.dart';
import 'dart:async';
import 'dart:math' as math;
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
        primarySwatch: Colors.purple,
        useMaterial3: true,
        cardTheme: CardTheme(
          elevation: 6,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        ),
        colorScheme: ColorScheme.fromSeed(
          seedColor: Colors.purple,
          brightness: Brightness.light,
        ).copyWith(
          primary: Colors.purple.shade600,
          secondary: Colors.cyan.shade400,
          tertiary: Colors.pink.shade400,
        ),
      ),
      darkTheme: ThemeData(
        primarySwatch: Colors.purple,
        useMaterial3: true,
        brightness: Brightness.dark,
        cardTheme: CardTheme(
          elevation: 8,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        ),
        colorScheme: ColorScheme.fromSeed(
          seedColor: Colors.purple,
          brightness: Brightness.dark,
        ).copyWith(
          primary: Colors.purple.shade400,
          secondary: Colors.cyan.shade300,
          tertiary: Colors.pink.shade300,
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
  final LedMatrixApi _api = LedMatrixApi(baseUrl: 'http://localhost:5005');
  
  List<MatrixApp> _apps = [];
  String? _activeAppId;
  MatrixSettings? _settings;
  List<AppSetting> _appSettings = [];
  bool _loading = true;
  String? _error;
  Timer? _previewTimer;
  String _previewImageKey = '';
  
  late AnimationController _pulseController;
  late AnimationController _rotationController;
  late Animation<double> _pulseAnimation;
  late Animation<double> _rotationAnimation;
  
  @override
  void initState() {
    super.initState();
    _initAnimations();
    _loadData();
    _startPreviewTimer();
  }
  
  void _initAnimations() {
    _pulseController = AnimationController(
      duration: const Duration(seconds: 2),
      vsync: this,
    )..repeat(reverse: true);
    
    _rotationController = AnimationController(
      duration: const Duration(seconds: 20),
      vsync: this,
    )..repeat();
    
    _pulseAnimation = Tween<double>(
      begin: 0.8,
      end: 1.2,
    ).animate(CurvedAnimation(
      parent: _pulseController,
      curve: Curves.easeInOut,
    ));
    
    _rotationAnimation = Tween<double>(
      begin: 0.0,
      end: 2 * math.pi,
    ).animate(_rotationController);
  }
  
  @override
  void dispose() {
    _previewTimer?.cancel();
    _pulseController.dispose();
    _rotationController.dispose();
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
          SnackBar(
            content: Row(
              children: [
                const Icon(Icons.check_circle, color: Colors.white),
                const SizedBox(width: 8),
                Text('🎉 Activated: ${_apps.firstWhere((a) => a.id == appId).name}'),
              ],
            ),
            backgroundColor: Colors.green.shade600,
            behavior: SnackBarBehavior.floating,
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
          ),
        );
        
        _loadAppSettings(appId);
      } else {
        throw Exception('Failed to activate app');
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Row(
            children: [
              const Icon(Icons.error, color: Colors.white),
              const SizedBox(width: 8),
              Text('❌ Error: $e'),
            ],
          ),
          backgroundColor: Colors.red.shade600,
          behavior: SnackBarBehavior.floating,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        ),
      );
    }
  }
  
  Future<void> _updateAppSetting(String key, dynamic value) async {
    if (_activeAppId == null) return;
    
    try {
      final success = await _api.updateAppSettings(_activeAppId!, {key: value});
      if (success) {
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
          SnackBar(
            content: Text('✨ Updated $key'),
            duration: const Duration(milliseconds: 800),
            behavior: SnackBarBehavior.floating,
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
          ),
        );
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('💥 Error updating setting: $e'),
          backgroundColor: Colors.red.shade600,
          behavior: SnackBarBehavior.floating,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        ),
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
        SnackBar(
          content: Text('Error setting brightness: $e'),
          backgroundColor: Colors.red.shade600,
          behavior: SnackBarBehavior.floating,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        ),
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
        SnackBar(
          content: Text('Error setting FPS: $e'),
          backgroundColor: Colors.red.shade600,
          behavior: SnackBarBehavior.floating,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        ),
      );
    }
  }

  Widget _buildAppCard(MatrixApp app, bool isActive) {
    final colors = _getAppColors(app.id);
    
    return AnimatedContainer(
      duration: const Duration(milliseconds: 300),
      curve: Curves.easeInOut,
      child: Card(
        elevation: isActive ? 12 : 6,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        child: InkWell(
          borderRadius: BorderRadius.circular(20),
          onTap: () => _activateApp(app.id),
          child: Container(
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(20),
              gradient: LinearGradient(
                colors: isActive
                    ? [
                        colors['primary']!.withOpacity(0.8),
                        colors['secondary']!.withOpacity(0.9),
                        colors['primary']!,
                      ]
                    : [
                        Colors.white,
                        Colors.grey.shade50,
                      ],
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                stops: const [0.0, 0.5, 1.0],
              ),
              boxShadow: isActive
                  ? [
                      BoxShadow(
                        color: colors['primary']!.withOpacity(0.4),
                        blurRadius: 20,
                        offset: const Offset(0, 8),
                      ),
                    ]
                  : null,
            ),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Stack(
                    alignment: Alignment.center,
                    children: [
                      if (isActive)
                        AnimatedBuilder(
                          animation: _pulseAnimation,
                          builder: (context, child) {
                            return Transform.scale(
                              scale: _pulseAnimation.value,
                              child: Container(
                                width: 80,
                                height: 80,
                                decoration: BoxDecoration(
                                  shape: BoxShape.circle,
                                  color: Colors.white.withOpacity(0.2),
                                ),
                              ),
                            );
                          },
                        ),
                      Container(
                        width: 64,
                        height: 64,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          color: isActive ? Colors.white.withOpacity(0.9) : colors['primary']!.withOpacity(0.1),
                          border: Border.all(
                            color: isActive ? Colors.white : colors['primary']!,
                            width: 2,
                          ),
                        ),
                        child: Icon(
                          _getAppIcon(app.id),
                          size: 32,
                          color: isActive ? colors['primary'] : colors['primary']!.withOpacity(0.8),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  Text(
                    app.name,
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.bold,
                      color: isActive ? Colors.white : Colors.grey.shade800,
                    ),
                    textAlign: TextAlign.center,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 8),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      if (app.hasSettings)
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                          decoration: BoxDecoration(
                            color: isActive ? Colors.white.withOpacity(0.2) : colors['secondary']!.withOpacity(0.1),
                            borderRadius: BorderRadius.circular(12),
                            border: Border.all(
                              color: isActive ? Colors.white.withOpacity(0.5) : colors['secondary']!.withOpacity(0.5),
                              width: 1,
                            ),
                          ),
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(
                                Icons.tune,
                                size: 12,
                                color: isActive ? Colors.white : colors['secondary'],
                              ),
                              const SizedBox(width: 4),
                              Text(
                                'Config',
                                style: TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.w600,
                                  color: isActive ? Colors.white : colors['secondary'],
                                ),
                              ),
                            ],
                          ),
                        ),
                      if (isActive) ...[
                        const SizedBox(width: 8),
                        AnimatedBuilder(
                          animation: _rotationController,
                          builder: (context, child) {
                            return Transform.rotate(
                              angle: _rotationAnimation.value,
                              child: Container(
                                padding: const EdgeInsets.all(6),
                                decoration: BoxDecoration(
                                  color: Colors.white.withOpacity(0.2),
                                  shape: BoxShape.circle,
                                ),
                                child: const Icon(
                                  Icons.stars,
                                  size: 16,
                                  color: Colors.white,
                                ),
                              ),
                            );
                          },
                        ),
                      ],
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
  
  Map<String, Color> _getAppColors(String appId) {
    switch (appId) {
      case 'clock':
      case 'animated-clock':
        return {'primary': Colors.blue.shade600, 'secondary': Colors.lightBlue.shade400};
      case 'solid_color':
        return {'primary': Colors.pink.shade600, 'secondary': Colors.purple.shade400};
      case 'rainbow-spiral':
        return {'primary': Colors.deepPurple.shade600, 'secondary': Colors.indigo.shade400};
      case 'bouncing-balls':
        return {'primary': Colors.orange.shade600, 'secondary': Colors.amber.shade400};
      case 'matrix-rain':
        return {'primary': Colors.green.shade600, 'secondary': Colors.lightGreen.shade400};
      case 'geometric-patterns':
        return {'primary': Colors.indigo.shade600, 'secondary': Colors.deepPurple.shade400};
      case 'dvd-logo':
        return {'primary': Colors.red.shade600, 'secondary': Colors.pink.shade400};
      case 'weather':
        return {'primary': Colors.cyan.shade600, 'secondary': Colors.teal.shade400};
      case 'spotify':
        return {'primary': Colors.green.shade600, 'secondary': Colors.lime.shade400};
      default:
        return {'primary': Colors.grey.shade600, 'secondary': Colors.blueGrey.shade400};
    }
  }
  
  IconData _getAppIcon(String appId) {
    switch (appId) {
      case 'clock':
      case 'animated-clock':
        return Icons.access_time_rounded;
      case 'solid_color':
        return Icons.palette_rounded;
      case 'rainbow-spiral':
        return Icons.gradient_rounded;
      case 'bouncing-balls':
        return Icons.sports_basketball_rounded;
      case 'matrix-rain':
        return Icons.code_rounded;
      case 'geometric-patterns':
        return Icons.category_rounded;
      case 'dvd-logo':
        return Icons.movie_rounded;
      case 'weather':
        return Icons.wb_sunny_rounded;
      case 'spotify':
        return Icons.music_note_rounded;
      default:
        return Icons.apps_rounded;
    }
  }
  
  Widget _buildAppSettingsCard() {
    if (_appSettings.isEmpty) return const SizedBox.shrink();
    
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Card(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        child: Container(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(20),
            gradient: LinearGradient(
              colors: [
                Theme.of(context).colorScheme.tertiary.withOpacity(0.1),
                Theme.of(context).colorScheme.secondary.withOpacity(0.1),
              ],
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
            ),
          ),
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: Theme.of(context).colorScheme.tertiary.withOpacity(0.2),
                        shape: BoxShape.circle,
                      ),
                      child: Icon(
                        Icons.tune_rounded,
                        color: Theme.of(context).colorScheme.tertiary,
                        size: 24,
                      ),
                    ),
                    const SizedBox(width: 12),
                    Text(
                      '🎛️ App Configuration',
                      style: TextStyle(
                        fontSize: 20,
                        fontWeight: FontWeight.bold,
                        color: Theme.of(context).colorScheme.tertiary,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 20),
                ..._appSettings.map((setting) => _buildSettingWidget(setting)).toList(),
              ],
            ),
          ),
        ),
      ),
    );
  }
  
  Widget _buildSettingWidget(AppSetting setting) {
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 8),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Colors.white.withOpacity(0.7),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: Theme.of(context).colorScheme.primary.withOpacity(0.2),
          width: 1,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                _getSettingIcon(setting.type),
                size: 16,
                color: Theme.of(context).colorScheme.primary,
              ),
              const SizedBox(width: 8),
              Text(
                setting.name,
                style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            setting.description,
            style: TextStyle(
              color: Colors.grey.shade600,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 12),
          _buildSettingInput(setting),
        ],
      ),
    );
  }
  
  IconData _getSettingIcon(AppSettingType type) {
    switch (type) {
      case AppSettingType.boolean:
        return Icons.toggle_on_rounded;
      case AppSettingType.integer:
        return Icons.linear_scale_rounded;
      case AppSettingType.select:
        return Icons.arrow_drop_down_circle_rounded;
      default:
        return Icons.edit_rounded;
    }
  }
  
  Widget _buildSettingInput(AppSetting setting) {
    switch (setting.type) {
      case AppSettingType.boolean:
        return Container(
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.primary.withOpacity(0.1),
            borderRadius: BorderRadius.circular(12),
          ),
          child: SwitchListTile(
            title: Text(
              setting.currentValue == true || setting.currentValue.toString().toLowerCase() == 'true' 
                ? '✅ Enabled' : '❌ Disabled',
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
            contentPadding: const EdgeInsets.symmetric(horizontal: 12),
            value: setting.currentValue == true || setting.currentValue.toString().toLowerCase() == 'true',
            onChanged: (value) => _updateAppSetting(setting.key, value),
            activeColor: Theme.of(context).colorScheme.primary,
          ),
        );
      
      case AppSettingType.integer:
        final currentValue = (setting.currentValue is int) 
            ? setting.currentValue as int 
            : int.tryParse(setting.currentValue.toString()) ?? 0;
        final minValue = (setting.minValue is int) ? setting.minValue as int : 0;
        final maxValue = (setting.maxValue is int) ? setting.maxValue as int : 100;
        
        return Column(
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              decoration: BoxDecoration(
                color: Theme.of(context).colorScheme.primary.withOpacity(0.1),
                borderRadius: BorderRadius.circular(20),
              ),
              child: Text(
                '🎚️ $currentValue',
                style: TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.bold,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
            ),
            const SizedBox(height: 8),
            SliderTheme(
              data: SliderTheme.of(context).copyWith(
                trackHeight: 6,
                thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 12),
                overlayShape: const RoundSliderOverlayShape(overlayRadius: 24),
              ),
              child: Slider(
                value: currentValue.toDouble(),
                min: minValue.toDouble(),
                max: maxValue.toDouble(),
                divisions: maxValue - minValue,
                label: currentValue.toString(),
                onChanged: (value) => _updateAppSetting(setting.key, value.round()),
                activeColor: Theme.of(context).colorScheme.primary,
                inactiveColor: Theme.of(context).colorScheme.primary.withOpacity(0.3),
              ),
            ),
          ],
        );
      
      case AppSettingType.select:
        return Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.primary.withOpacity(0.1),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: Theme.of(context).colorScheme.primary.withOpacity(0.3),
              width: 1,
            ),
          ),
          child: DropdownButton<String>(
            value: setting.currentValue.toString(),
            isExpanded: true,
            underline: const SizedBox(),
            icon: Icon(
              Icons.keyboard_arrow_down_rounded,
              color: Theme.of(context).colorScheme.primary,
            ),
            items: (setting.options ?? []).map((option) {
              return DropdownMenuItem(
                value: option,
                child: Row(
                  children: [
                    Container(
                      width: 12,
                      height: 12,
                      margin: const EdgeInsets.only(right: 8),
                      decoration: BoxDecoration(
                        color: _getColorForOption(option),
                        shape: BoxShape.circle,
                        border: Border.all(color: Colors.grey.shade400),
                      ),
                    ),
                    Text(option),
                  ],
                ),
              );
            }).toList(),
            onChanged: (value) {
              if (value != null) _updateAppSetting(setting.key, value);
            },
          ),
        );
      
      default:
        return Container(
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.primary.withOpacity(0.1),
            borderRadius: BorderRadius.circular(12),
          ),
          child: TextFormField(
            initialValue: setting.currentValue.toString(),
            decoration: const InputDecoration(
              border: InputBorder.none,
              contentPadding: EdgeInsets.all(12),
            ),
            onChanged: (value) => _updateAppSetting(setting.key, value),
          ),
        );
    }
  }
  
  Color _getColorForOption(String option) {
    switch (option.toLowerCase()) {
      case 'red': return Colors.red;
      case 'green': return Colors.green;
      case 'blue': return Colors.blue;
      case 'yellow': return Colors.yellow;
      case 'cyan': return Colors.cyan;
      case 'magenta': return Colors.pink;
      case 'white': return Colors.grey;
      default: return Colors.grey.shade400;
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            AnimatedBuilder(
              animation: _rotationController,
              builder: (context, child) {
                return Transform.rotate(
                  angle: _rotationAnimation.value,
                  child: const Icon(Icons.lightbulb_rounded),
                );
              },
            ),
            const SizedBox(width: 8),
            const Text('🎮 LED Matrix Control'),
          ],
        ),
        backgroundColor: Theme.of(context).colorScheme.inversePrimary,
        elevation: 0,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            onPressed: _loadData,
          ),
        ],
      ),
      body: _loading
          ? Center(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  AnimatedBuilder(
                    animation: _pulseController,
                    builder: (context, child) {
                      return Transform.scale(
                        scale: _pulseAnimation.value,
                        child: Container(
                          width: 80,
                          height: 80,
                          decoration: BoxDecoration(
                            gradient: RadialGradient(
                              colors: [
                                Theme.of(context).primaryColor.withOpacity(0.8),
                                Theme.of(context).primaryColor.withOpacity(0.3),
                              ],
                            ),
                            shape: BoxShape.circle,
                          ),
                          child: const Icon(
                            Icons.cable_rounded,
                            size: 40,
                            color: Colors.white,
                          ),
                        ),
                      );
                    },
                  ),
                  const SizedBox(height: 24),
                  Text(
                    '🔌 Connecting to LED Matrix...',
                    style: TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.bold,
                      color: Theme.of(context).primaryColor,
                    ),
                  ),
                ],
              ),
            )
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.error_outline_rounded, size: 80, color: Colors.red),
                      const SizedBox(height: 16),
                      Text('💥 Oops! Something went wrong', 
                          style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: Colors.red.shade700)),
                      const SizedBox(height: 8),
                      Text(_error!, 
                          style: TextStyle(color: Colors.red.shade600),
                          textAlign: TextAlign.center),
                      const SizedBox(height: 24),
                      ElevatedButton.icon(
                        onPressed: _loadData,
                        icon: const Icon(Icons.refresh_rounded),
                        label: const Text('🔄 Try Again'),
                        style: ElevatedButton.styleFrom(
                          backgroundColor: Theme.of(context).primaryColor,
                          foregroundColor: Colors.white,
                          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
                        ),
                      ),
                    ],
                  ),
                )
              : Container(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      colors: [
                        Theme.of(context).colorScheme.primary.withOpacity(0.05),
                        Theme.of(context).colorScheme.secondary.withOpacity(0.05),
                        Theme.of(context).colorScheme.tertiary.withOpacity(0.05),
                      ],
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      stops: const [0.0, 0.5, 1.0],
                    ),
                  ),
                  child: SingleChildScrollView(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        // Preview Card
                        Container(
                          margin: const EdgeInsets.all(16),
                          child: Card(
                            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
                            child: Container(
                              decoration: BoxDecoration(
                                borderRadius: BorderRadius.circular(20),
                                gradient: LinearGradient(
                                  colors: [
                                    Theme.of(context).colorScheme.primary.withOpacity(0.1),
                                    Theme.of(context).colorScheme.secondary.withOpacity(0.1),
                                  ],
                                  begin: Alignment.topLeft,
                                  end: Alignment.bottomRight,
                                ),
                              ),
                              child: Padding(
                                padding: const EdgeInsets.all(20),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Row(
                                      children: [
                                        Container(
                                          padding: const EdgeInsets.all(10),
                                          decoration: BoxDecoration(
                                            color: Theme.of(context).colorScheme.primary.withOpacity(0.2),
                                            shape: BoxShape.circle,
                                          ),
                                          child: Icon(
                                            Icons.monitor_rounded,
                                            color: Theme.of(context).colorScheme.primary,
                                            size: 24,
                                          ),
                                        ),
                                        const SizedBox(width: 12),
                                        Text(
                                          '📺 Live Preview',
                                          style: TextStyle(
                                            fontSize: 20,
                                            fontWeight: FontWeight.bold,
                                            color: Theme.of(context).colorScheme.primary,
                                          ),
                                        ),
                                      ],
                                    ),
                                    const SizedBox(height: 16),
                                    Container(
                                      width: double.infinity,
                                      height: 200,
                                      decoration: BoxDecoration(
                                        border: Border.all(
                                          color: Theme.of(context).colorScheme.primary.withOpacity(0.3),
                                          width: 2,
                                        ),
                                        borderRadius: BorderRadius.circular(16),
                                        gradient: LinearGradient(
                                          colors: [
                                            Colors.black.withOpacity(0.8),
                                            Colors.grey.shade900.withOpacity(0.8),
                                          ],
                                          begin: Alignment.topCenter,
                                          end: Alignment.bottomCenter,
                                        ),
                                      ),
                                      child: ClipRRect(
                                        borderRadius: BorderRadius.circular(14),
                                        child: Image.network(
                                          '${_api.getPreviewUrl()}&key=$_previewImageKey',
                                          fit: BoxFit.contain,
                                          filterQuality: FilterQuality.none,
                                          errorBuilder: (context, error, stackTrace) {
                                            return const Center(
                                              child: Column(
                                                mainAxisAlignment: MainAxisAlignment.center,
                                                children: [
                                                  Icon(Icons.image_not_supported_rounded, 
                                                       size: 48, color: Colors.grey),
                                                  SizedBox(height: 8),
                                                  Text('🚫 Preview not available\n(Simulator mode only)', 
                                                       textAlign: TextAlign.center,
                                                       style: TextStyle(color: Colors.grey)),
                                                ],
                                              ),
                                            );
                                          },
                                          loadingBuilder: (context, child, loadingProgress) {
                                            if (loadingProgress == null) return child;
                                            return const Center(
                                              child: CircularProgressIndicator(),
                                            );
                                          },
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                        ),
                        
                        // Apps Grid Section
                        Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                children: [
                                  Container(
                                    padding: const EdgeInsets.all(10),
                                    decoration: BoxDecoration(
                                      color: Theme.of(context).colorScheme.secondary.withOpacity(0.2),
                                      shape: BoxShape.circle,
                                    ),
                                    child: Icon(
                                      Icons.apps_rounded,
                                      color: Theme.of(context).colorScheme.secondary,
                                      size: 24,
                                    ),
                                  ),
                                  const SizedBox(width: 12),
                                  Text(
                                    '🎨 Choose Your Vibe',
                                    style: TextStyle(
                                      fontSize: 20,
                                      fontWeight: FontWeight.bold,
                                      color: Theme.of(context).colorScheme.secondary,
                                    ),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 16),
                              // Apps Grid
                              GridView.builder(
                                shrinkWrap: true,
                                physics: const NeverScrollableScrollPhysics(),
                                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                                  crossAxisCount: 2,
                                  childAspectRatio: 0.85,
                                  crossAxisSpacing: 12,
                                  mainAxisSpacing: 12,
                                ),
                                itemCount: _apps.length,
                                itemBuilder: (context, index) {
                                  final app = _apps[index];
                                  final isActive = app.id == _activeAppId;
                                  return _buildAppCard(app, isActive);
                                },
                              ),
                            ],
                          ),
                        ),
                        
                        // App Settings Card
                        _buildAppSettingsCard(),
                        
                        // Display Settings Card
                        Container(
                          margin: const EdgeInsets.all(16),
                          child: Card(
                            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
                            child: Container(
                              decoration: BoxDecoration(
                                borderRadius: BorderRadius.circular(20),
                                gradient: LinearGradient(
                                  colors: [
                                    Theme.of(context).colorScheme.primary.withOpacity(0.1),
                                    Theme.of(context).colorScheme.tertiary.withOpacity(0.1),
                                  ],
                                  begin: Alignment.topLeft,
                                  end: Alignment.bottomRight,
                                ),
                              ),
                              child: Padding(
                                padding: const EdgeInsets.all(20),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Row(
                                      children: [
                                        Container(
                                          padding: const EdgeInsets.all(10),
                                          decoration: BoxDecoration(
                                            color: Theme.of(context).colorScheme.primary.withOpacity(0.2),
                                            shape: BoxShape.circle,
                                          ),
                                          child: Icon(
                                            Icons.display_settings_rounded,
                                            color: Theme.of(context).colorScheme.primary,
                                            size: 24,
                                          ),
                                        ),
                                        const SizedBox(width: 12),
                                        Text(
                                          '⚙️ Display Controls',
                                          style: TextStyle(
                                            fontSize: 20,
                                            fontWeight: FontWeight.bold,
                                            color: Theme.of(context).colorScheme.primary,
                                          ),
                                        ),
                                      ],
                                    ),
                                    const SizedBox(height: 16),
                                    if (_settings != null) ...[
                                      Container(
                                        padding: const EdgeInsets.all(16),
                                        decoration: BoxDecoration(
                                          color: Colors.white.withOpacity(0.7),
                                          borderRadius: BorderRadius.circular(16),
                                        ),
                                        child: Row(
                                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                          children: [
                                            Column(
                                              crossAxisAlignment: CrossAxisAlignment.start,
                                              children: [
                                                Text(
                                                  '📐 Resolution',
                                                  style: TextStyle(
                                                    fontWeight: FontWeight.bold,
                                                    color: Colors.grey.shade700,
                                                  ),
                                                ),
                                                Text(
                                                  '${_settings!.width} x ${_settings!.height}',
                                                  style: const TextStyle(
                                                    fontSize: 18,
                                                    fontWeight: FontWeight.bold,
                                                  ),
                                                ),
                                              ],
                                            ),
                                            Container(
                                              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                                              decoration: BoxDecoration(
                                                color: _settings!.isRunning ? Colors.green.shade100 : Colors.red.shade100,
                                                borderRadius: BorderRadius.circular(20),
                                                border: Border.all(
                                                  color: _settings!.isRunning ? Colors.green : Colors.red,
                                                  width: 2,
                                                ),
                                              ),
                                              child: Row(
                                                mainAxisSize: MainAxisSize.min,
                                                children: [
                                                  Icon(
                                                    _settings!.isRunning ? Icons.play_circle_rounded : Icons.stop_circle_rounded,
                                                    size: 16,
                                                    color: _settings!.isRunning ? Colors.green.shade700 : Colors.red.shade700,
                                                  ),
                                                  const SizedBox(width: 4),
                                                  Text(
                                                    _settings!.isRunning ? 'RUNNING' : 'STOPPED',
                                                    style: TextStyle(
                                                      color: _settings!.isRunning ? Colors.green.shade700 : Colors.red.shade700,
                                                      fontWeight: FontWeight.bold,
                                                      fontSize: 12,
                                                    ),
                                                  ),
                                                ],
                                              ),
                                            ),
                                          ],
                                        ),
                                      ),
                                      const SizedBox(height: 20),
                                      
                                      // Brightness Control
                                      Container(
                                        padding: const EdgeInsets.all(16),
                                        decoration: BoxDecoration(
                                          color: Colors.white.withOpacity(0.7),
                                          borderRadius: BorderRadius.circular(16),
                                        ),
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Row(
                                              children: [
                                                const Icon(Icons.brightness_7_rounded, size: 20),
                                                const SizedBox(width: 8),
                                                Text(
                                                  '💡 Brightness: ${_settings!.brightness}%',
                                                  style: const TextStyle(
                                                    fontWeight: FontWeight.bold,
                                                    fontSize: 16,
                                                  ),
                                                ),
                                              ],
                                            ),
                                            const SizedBox(height: 12),
                                            SliderTheme(
                                              data: SliderTheme.of(context).copyWith(
                                                trackHeight: 6,
                                                thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 12),
                                                overlayShape: const RoundSliderOverlayShape(overlayRadius: 24),
                                              ),
                                              child: Slider(
                                                value: _settings!.brightness.toDouble(),
                                                min: 0,
                                                max: 100,
                                                divisions: 100,
                                                label: '${_settings!.brightness}%',
                                                onChanged: _setBrightness,
                                                activeColor: Colors.amber,
                                                inactiveColor: Colors.amber.withOpacity(0.3),
                                              ),
                                            ),
                                          ],
                                        ),
                                      ),
                                      
                                      const SizedBox(height: 16),
                                      
                                      // FPS Control
                                      Container(
                                        padding: const EdgeInsets.all(16),
                                        decoration: BoxDecoration(
                                          color: Colors.white.withOpacity(0.7),
                                          borderRadius: BorderRadius.circular(16),
                                        ),
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Row(
                                              children: [
                                                const Icon(Icons.speed_rounded, size: 20),
                                                const SizedBox(width: 8),
                                                Text(
                                                  '⚡ FPS: ${_settings!.fps}',
                                                  style: const TextStyle(
                                                    fontWeight: FontWeight.bold,
                                                    fontSize: 16,
                                                  ),
                                                ),
                                              ],
                                            ),
                                            const SizedBox(height: 12),
                                            SliderTheme(
                                              data: SliderTheme.of(context).copyWith(
                                                trackHeight: 6,
                                                thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 12),
                                                overlayShape: const RoundSliderOverlayShape(overlayRadius: 24),
                                              ),
                                              child: Slider(
                                                value: _settings!.fps.toDouble(),
                                                min: 1,
                                                max: 120,
                                                divisions: 119,
                                                label: '${_settings!.fps}',
                                                onChanged: _setFps,
                                                activeColor: Colors.green,
                                                inactiveColor: Colors.green.withOpacity(0.3),
                                              ),
                                            ),
                                          ],
                                        ),
                                      ),
                                    ],
                                  ],
                                ),
                              ),
                            ),
                          ),
                        ),
                        
                        const SizedBox(height: 20), // Bottom padding
                      ],
                    ),
                  ),
                ),
    );
  }
}