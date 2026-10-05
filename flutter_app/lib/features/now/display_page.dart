import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

import '../../core/providers.dart';
import 'live_matrix.dart';

const _ledDotsPrefsKey = 'display_led_dots';

/// Turns the phone into a display: the live matrix full screen in landscape, screen kept awake.
/// Tap to show the controls, which hide again after a few seconds.
class DisplayPage extends ConsumerStatefulWidget {
  const DisplayPage({super.key});

  @override
  ConsumerState<DisplayPage> createState() => _DisplayPageState();
}

class _DisplayPageState extends ConsumerState<DisplayPage> {
  bool _ledDots = true;
  bool _controls = true;
  Timer? _hideTimer;

  @override
  void initState() {
    super.initState();
    SystemChrome.setPreferredOrientations([DeviceOrientation.landscapeLeft, DeviceOrientation.landscapeRight]);
    SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky);
    _platform(WakelockPlus.enable());
    _loadPrefs();
    _scheduleHide();
  }

  @override
  void dispose() {
    _hideTimer?.cancel();
    SystemChrome.setPreferredOrientations([]);
    SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
    _platform(WakelockPlus.disable());
    super.dispose();
  }

  /// Plugins are missing in widget tests and on some platforms; the display still works without them.
  void _platform(Future<void> call) => call.catchError((Object _) {});

  Future<void> _loadPrefs() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final saved = prefs.getBool(_ledDotsPrefsKey);
      if (saved != null && mounted) setState(() => _ledDots = saved);
    } catch (_) {}
  }

  Future<void> _setLedDots(bool value) async {
    setState(() => _ledDots = value);
    _scheduleHide();
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(_ledDotsPrefsKey, value);
    } catch (_) {}
  }

  void _scheduleHide() {
    _hideTimer?.cancel();
    _hideTimer = Timer(const Duration(seconds: 3), () {
      if (mounted) setState(() => _controls = false);
    });
  }

  void _toggleControls() {
    setState(() => _controls = !_controls);
    if (_controls) _scheduleHide();
  }

  @override
  Widget build(BuildContext context) {
    final settings = ref.watch(deviceSettingsProvider).value;
    final cols = settings != null && settings.width > 0 ? settings.width : 256;
    final rows = settings != null && settings.height > 0 ? settings.height : 64;
    final dpr = MediaQuery.devicePixelRatioOf(context);

    return Scaffold(
      backgroundColor: Colors.black,
      body: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: _toggleControls,
        onLongPress: () => ref.invalidate(previewFeedProvider),
        child: Stack(
          children: [
            Positioned.fill(
              child: LayoutBuilder(builder: (context, box) {
                // Whole physical pixels per LED keep every dot the same size; fractional only on tiny screens.
                final fit = (box.maxWidth * dpr / cols).clamp(0.0, box.maxHeight * dpr / rows);
                final cell = fit >= 2 ? fit.floorToDouble() : fit;
                return Center(
                  child: SizedBox(
                    key: const Key('display-matrix'),
                    width: cell * cols / dpr,
                    height: cell * rows / dpr,
                    child: LiveMatrix(ledDots: _ledDots),
                  ),
                );
              }),
            ),
            Positioned(
              top: 0,
              right: 0,
              child: SafeArea(
                child: AnimatedOpacity(
                  opacity: _controls ? 1 : 0,
                  duration: const Duration(milliseconds: 200),
                  child: IgnorePointer(
                    ignoring: !_controls,
                    child: Padding(
                      padding: const EdgeInsets.all(8),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          _ControlButton(
                            key: const Key('display-led-dots'),
                            tooltip: _ledDots ? 'Show square pixels' : 'Show LED dots',
                            icon: _ledDots ? Icons.grid_on_rounded : Icons.blur_on_rounded,
                            onPressed: () => _setLedDots(!_ledDots),
                          ),
                          const SizedBox(width: 8),
                          _ControlButton(
                            key: const Key('display-close'),
                            tooltip: 'Exit full screen',
                            icon: Icons.close_rounded,
                            // Opened directly (deep link or web URL) there is nothing to pop back to.
                            onPressed: () => context.canPop() ? context.pop() : context.go('/now'),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ControlButton extends StatelessWidget {
  const _ControlButton({super.key, required this.tooltip, required this.icon, required this.onPressed});

  final String tooltip;
  final IconData icon;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) => IconButton.filledTonal(
        tooltip: tooltip,
        icon: Icon(icon),
        onPressed: onPressed,
        style: IconButton.styleFrom(backgroundColor: Colors.white12, foregroundColor: Colors.white),
      );
}
