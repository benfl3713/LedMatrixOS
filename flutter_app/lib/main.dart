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
  bool _loading = true;
  String? _error;
  Timer? _previewTimer;
  String _previewImageKey = '';
  
  @override
  void initState() {
    super.initState();
    _loadData();
    _startPreviewTimer();
  }
  
  @override
  void dispose() {
    _previewTimer?.cancel();
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
      // Load apps and settings concurrently
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
    } catch (e) {
      setState(() {
        _error = e.toString();
        _loading = false;
      });
    }
  }
  
  Future<void> _activateApp(String appId) async {
    try {
      final success = await _api.activateApp(appId);
      if (success) {
        setState(() {
          _activeAppId = appId;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Activated app: $appId')),
        );
      } else {
        throw Exception('Failed to activate app');
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red),
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
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // Preview Card
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const Text(
                                'LED Matrix Preview',
                                style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
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
                      
                      const SizedBox(height: 16),
                      
                      // Settings Card
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const Text(
                                'Display Settings',
                                style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                              ),
                              const SizedBox(height: 12),
                              if (_settings != null) ...[
                                Text('Resolution: ${_settings!.width} x ${_settings!.height}'),
                                Text('Status: ${_settings!.isRunning ? 'Running' : 'Stopped'}'),
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
                      
                      const SizedBox(height: 16),
                      
                      // Apps Card
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const Text(
                                'Available Apps',
                                style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                              ),
                              const SizedBox(height: 12),
                              if (_activeAppId != null)
                                Container(
                                  padding: const EdgeInsets.all(8),
                                  decoration: BoxDecoration(
                                    color: Colors.green.withOpacity(0.1),
                                    border: Border.all(color: Colors.green),
                                    borderRadius: BorderRadius.circular(4),
                                  ),
                                  child: Text('Active: $_activeAppId'),
                                ),
                              const SizedBox(height: 12),
                              Wrap(
                                spacing: 8,
                                runSpacing: 8,
                                children: _apps.map((app) {
                                  final isActive = app.id == _activeAppId;
                                  return ElevatedButton(
                                    onPressed: () => _activateApp(app.id),
                                    style: ElevatedButton.styleFrom(
                                      backgroundColor: isActive 
                                          ? Theme.of(context).primaryColor
                                          : null,
                                      foregroundColor: isActive 
                                          ? Colors.white
                                          : null,
                                    ),
                                    child: Text(app.name),
                                  );
                                }).toList(),
                              ),
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