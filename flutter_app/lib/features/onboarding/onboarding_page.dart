import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/connection.dart';
import '../../core/device_url.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'discovery.dart';

/// First-run flow, shown while no device address has been saved: find the display on the network or type its
/// address, test it, and save. Saving changes the device config, which makes the router land on Now.
class OnboardingPage extends ConsumerStatefulWidget {
  const OnboardingPage({super.key});

  @override
  ConsumerState<OnboardingPage> createState() => _OnboardingPageState();
}

enum _ScanState { idle, scanning, done }

class _OnboardingPageState extends ConsumerState<OnboardingPage> {
  final _controller = TextEditingController();
  StreamSubscription<FoundDevice>? _scan;
  _ScanState _scanState = _ScanState.idle;
  final List<FoundDevice> _found = [];

  String? _fieldError;
  bool _testing = false;
  ConnectionTestResult? _result;
  String? _testedUrl;
  bool _saving = false;

  @override
  void dispose() {
    _scan?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _startScan() {
    _scan?.cancel();
    setState(() {
      _scanState = _ScanState.scanning;
      _found.clear();
    });
    _scan = ref.read(deviceScannerProvider)().listen(
      (device) {
        if (mounted) setState(() => _found.add(device));
      },
      onDone: () {
        if (mounted) setState(() => _scanState = _ScanState.done);
      },
      onError: (_) {
        if (mounted) setState(() => _scanState = _ScanState.done);
      },
    );
  }

  Future<void> _save(String url) async {
    if (_saving) return;
    setState(() => _saving = true);
    await ref.read(deviceConfigProvider.notifier).save(url);
    if (mounted) setState(() => _saving = false);
  }

  Future<void> _test() async {
    final parsed = normalizeDeviceUrl(_controller.text);
    if (parsed.url == null) {
      setState(() {
        _fieldError = parsed.error;
        _result = null;
      });
      return;
    }
    setState(() {
      _fieldError = null;
      _testing = true;
      _result = null;
    });
    final url = parsed.url!;
    final result = await testDeviceConnection(
      ref.read(apiFactoryProvider)(url),
    );
    if (!mounted) return;
    setState(() {
      _testing = false;
      _result = result;
      _testedUrl = url;
    });
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final result = _result;
    final previousUrl = ref.watch(
      deviceConfigProvider.select((c) => c.previousUrl),
    );
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 520),
            child: ListView(
              key: const Key('onboarding'),
              padding: const EdgeInsets.fromLTRB(
                Space.lg,
                Space.xl,
                Space.lg,
                Space.xl,
              ),
              children: [
                const _Hero(),
                const SizedBox(height: Space.xl),
                Semantics(
                  header: true,
                  child: Text(
                    'Meet your display',
                    style: text.headlineMedium,
                    textAlign: TextAlign.center,
                  ),
                ),
                const SizedBox(height: Space.sm),
                Text(
                  'Control your LED matrix from your phone. Connect to the display on your Wi-Fi to get started.',
                  style: text.bodyLarge?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: Space.xl),
                if (previousUrl != null) ...[
                  OutlinedButton(
                    key: const Key('keep-previous'),
                    style: OutlinedButton.styleFrom(
                      minimumSize: const Size.fromHeight(48),
                    ),
                    onPressed: () => ref.read(deviceConfigProvider.notifier).restorePrevious(),
                    child: Text(
                      'Keep using $previousUrl',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      textAlign: TextAlign.center,
                    ),
                  ),
                  const SizedBox(height: Space.md),
                ],
                FilledButton.icon(
                  key: const Key('find-display'),
                  style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(52),
                  ),
                  onPressed: _scanState == _ScanState.scanning ? null : _startScan,
                  icon: const Icon(Icons.radar_rounded),
                  label: Text(
                    _scanState == _ScanState.idle ? 'Find my display' : 'Scan again',
                  ),
                ),
                const SizedBox(height: Space.md),
                _ScanResults(
                  state: _scanState,
                  found: _found,
                  saving: _saving,
                  onSelect: (d) => _save(d.url),
                ),
                const SizedBox(height: Space.xl),
                const SectionHeader('Or enter the address'),
                SectionCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextField(
                        key: const Key('manual-address'),
                        controller: _controller,
                        keyboardType: TextInputType.url,
                        autocorrect: false,
                        enableSuggestions: false,
                        textInputAction: TextInputAction.go,
                        onSubmitted: (_) => _test(),
                        onChanged: (_) {
                          if (_result != null || _fieldError != null) {
                            setState(() {
                              _result = null;
                              _fieldError = null;
                            });
                          }
                        },
                        decoration: InputDecoration(
                          labelText: 'Display address',
                          hintText: 'ledmatrix.local or 192.168.1.20',
                          prefixIcon: const Icon(Icons.lan_outlined),
                          errorText: _fieldError,
                        ),
                      ),
                      const SizedBox(height: Space.md),
                      OutlinedButton.icon(
                        key: const Key('test-connection'),
                        style: OutlinedButton.styleFrom(
                          minimumSize: const Size.fromHeight(48),
                        ),
                        onPressed: _testing ? null : _test,
                        icon: _testing
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.wifi_tethering_rounded),
                        label: Text(
                          _testing ? 'Testing...' : 'Test connection',
                        ),
                      ),
                      if (result != null) ...[
                        const SizedBox(height: Space.md),
                        _TestOutcome(result: result, url: _testedUrl ?? ''),
                        if (result.success) ...[
                          const SizedBox(height: Space.md),
                          FilledButton(
                            key: const Key('use-display'),
                            style: FilledButton.styleFrom(
                              minimumSize: const Size.fromHeight(52),
                            ),
                            onPressed: _saving ? null : () => _save(_testedUrl!),
                            child: const Text('Use this display'),
                          ),
                        ],
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A little LED panel as the product mark.
class _Hero extends StatelessWidget {
  const _Hero();

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return ExcludeSemantics(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 320),
          child: GlowPreview(
            child: AspectRatio(
              aspectRatio: 4,
              child: ColoredBox(
                color: Brand.panel,
                child: Center(
                  child: FittedBox(
                    child: Padding(
                      padding: const EdgeInsets.all(Space.md),
                      child: Text(
                        'LED MATRIX',
                        style: TextStyle(
                          color: scheme.primary,
                          fontWeight: FontWeight.w800,
                          letterSpacing: 6,
                          fontSize: 28,
                          shadows: [
                            Shadow(
                              color: scheme.primary.withValues(alpha: 0.8),
                              blurRadius: 14,
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _ScanResults extends StatelessWidget {
  const _ScanResults({
    required this.state,
    required this.found,
    required this.saving,
    required this.onSelect,
  });

  final _ScanState state;
  final List<FoundDevice> found;
  final bool saving;
  final ValueChanged<FoundDevice> onSelect;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    if (state == _ScanState.idle) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final d in found)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.sm),
            child: Semantics(
              button: true,
              label:
                  'Display at ${d.host}, ${d.resolution}${d.health.activeApp == null ? '' : ', showing ${d.health.activeApp}'}. Connect.',
              child: ExcludeSemantics(
                child: Material(
                  color: scheme.surfaceContainerLow,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(Radii.md),
                    side: BorderSide(
                      color: scheme.primary.withValues(alpha: 0.5),
                    ),
                  ),
                  child: InkWell(
                    key: Key('found-${d.host}'),
                    borderRadius: BorderRadius.circular(Radii.md),
                    onTap: saving ? null : () => onSelect(d),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(minHeight: 64),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          horizontal: Space.lg,
                          vertical: Space.md,
                        ),
                        child: Row(
                          children: [
                            Icon(Icons.grid_on_rounded, color: scheme.primary),
                            const SizedBox(width: Space.md),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    d.host,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: text.titleMedium,
                                  ),
                                  Text(
                                    [
                                      d.resolution,
                                      if (d.health.activeApp != null) 'showing ${d.health.activeApp}',
                                    ].join(' - '),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: text.bodySmall?.copyWith(
                                      color: scheme.onSurfaceVariant,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                            Icon(
                              Icons.chevron_right_rounded,
                              color: scheme.onSurfaceVariant,
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        if (state == _ScanState.scanning)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: Space.sm),
            child: Row(
              key: const Key('scanning'),
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
                const SizedBox(width: Space.md),
                Flexible(
                  child: Text(
                    'Scanning your network...',
                    style: text.bodyMedium?.copyWith(
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                ),
              ],
            ),
          ),
        if (state == _ScanState.done && found.isEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: Space.sm),
            child: Text(
              "No display found. Check that it is powered on and on the same Wi-Fi, or enter its address below.",
              key: const Key('scan-empty'),
              textAlign: TextAlign.center,
              style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant),
            ),
          ),
      ],
    );
  }
}

class _TestOutcome extends StatelessWidget {
  const _TestOutcome({required this.result, required this.url});

  final ConnectionTestResult result;
  final String url;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final ok = result.success;
    final health = result.health;
    final detail = ok
        ? [
            if (health!.width > 0) '${health.width}x${health.height}',
            if (health.activeApp != null) 'showing ${health.activeApp}',
          ].join(' - ')
        : (result.error ?? 'Could not reach the display');
    return Semantics(
      liveRegion: true,
      child: Row(
        key: Key(ok ? 'test-ok' : 'test-failed'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            ok ? Icons.check_circle_rounded : Icons.error_outline_rounded,
            color: ok ? scheme.primary : scheme.error,
          ),
          const SizedBox(width: Space.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  ok ? 'Connected to $url' : "Couldn't connect",
                  style: text.titleSmall,
                ),
                if (detail.isNotEmpty)
                  Text(
                    detail,
                    style: text.bodySmall?.copyWith(
                      color: scheme.onSurfaceVariant,
                    ),
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Shown for the instant before the saved device address has been read.
class LoadingSplash extends StatelessWidget {
  const LoadingSplash({super.key});

  @override
  Widget build(BuildContext context) => const Scaffold(
        body: Center(
          child: SizedBox(
            width: 28,
            height: 28,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
      );
}
