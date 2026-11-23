import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/connection.dart';
import '../../core/device_url.dart';
import '../../core/providers.dart';
import '../../core/theme_mode_provider.dart';
import '../../core/widgets.dart';
import 'audio_stream_provider.dart';

/// Shown in the About section. Keep in step with `version:` in pubspec.yaml.
const appVersion = '1.1.0 (3)';

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: const [
          _SectionLabel('Connection'),
          _ConnectionCard(),
          SizedBox(height: 20),
          _SectionLabel('Device'),
          _DeviceCard(),
          SizedBox(height: 20),
          _SectionLabel('Appearance'),
          _AppearanceCard(),
          SizedBox(height: 20),
          _SectionLabel('Audio'),
          _AudioCard(),
          SizedBox(height: 20),
          _SectionLabel('About'),
          _AboutCard(),
          SizedBox(height: 16),
        ],
      ),
    );
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
        child: Text(
          text.toUpperCase(),
          style: Theme.of(context).textTheme.labelMedium?.copyWith(
                color: Theme.of(context).colorScheme.primary,
                letterSpacing: 1,
                fontWeight: FontWeight.w700,
              ),
        ),
      );
}

class _ConnectionCard extends ConsumerStatefulWidget {
  const _ConnectionCard();

  @override
  ConsumerState<_ConnectionCard> createState() => _ConnectionCardState();
}

class _ConnectionCardState extends ConsumerState<_ConnectionCard> {
  late final TextEditingController _controller = TextEditingController(
    text: ref.read(apiUrlProvider),
  );
  bool _testing = false;
  ConnectionTestResult? _result;
  String? _resultFor;

  ({String? url, String? error}) get _parsed => normalizeDeviceUrl(_controller.text);
  bool get _dirty => _parsed.url != ref.read(apiUrlProvider);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _test() async {
    final url = _parsed.url;
    if (url == null) return;
    setState(() {
      _testing = true;
      _result = null;
    });
    final result = await testDeviceConnection(
      ref.read(apiFactoryProvider)(url),
    );
    if (!mounted) return;
    setState(() {
      _testing = false;
      _result = result;
      _resultFor = url;
    });
  }

  Future<void> _save() async {
    final url = _parsed.url;
    if (url == null) return;
    final saved = await ref.read(apiUrlProvider.notifier).save(url);
    if (!mounted || !saved) return;
    _controller.text = ref.read(apiUrlProvider);
    ref.invalidate(healthProvider);
    ref.read(connectionProvider.notifier).check();
    setState(() {});
    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Device address saved')));
  }

  /// Forgets this display and reopens setup, where the user can scan for displays or enter another address.
  Future<void> _findDisplay() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Find another display?'),
        content: const Text("You'll go back to setup."),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            key: const Key('confirm-find-display'),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Continue'),
          ),
        ],
      ),
    );
    if (ok == true && mounted) ref.read(apiUrlProvider.notifier).forget();
  }

  @override
  Widget build(BuildContext context) {
    // Pick up the persisted URL once it has loaded from storage (unless the user is already typing).
    ref.listen(apiUrlProvider, (previous, next) {
      if (_controller.text == previous || _controller.text.isEmpty) _controller.text = next;
    });
    final parsed = _parsed;
    final theme = Theme.of(context);
    final stale = _result != null && _resultFor != parsed.url;
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          TextField(
            key: const Key('device-url'),
            controller: _controller,
            keyboardType: TextInputType.url,
            autocorrect: false,
            enableSuggestions: false,
            textInputAction: TextInputAction.done,
            decoration: InputDecoration(
              labelText: 'Device address',
              hintText: '192.168.1.50 or ledmatrix.local',
              border: const OutlineInputBorder(),
              errorText: parsed.error,
              helperText: parsed.url == null ? null : 'Will connect to ${parsed.url}',
              helperMaxLines: 2,
            ),
            onChanged: (_) => setState(() {}),
            onSubmitted: (_) => _test(),
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              OutlinedButton.icon(
                key: const Key('test-connection'),
                onPressed: parsed.url == null || _testing ? null : _test,
                icon: _testing
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.wifi_find_rounded),
                label: const Text('Test connection'),
              ),
              FilledButton(
                key: const Key('save-url'),
                onPressed: parsed.url != null && _dirty ? _save : null,
                child: const Text('Save'),
              ),
              TextButton(
                key: const Key('find-display'),
                onPressed: _findDisplay,
                child: const Text('Find display'),
              ),
            ],
          ),
          if (_result != null && !stale) ...[
            const SizedBox(height: 12),
            _TestOutcome(result: _result!),
          ],
          if (!_dirty && parsed.url != null) ...[
            const SizedBox(height: 12),
            Consumer(
              builder: (context, ref, _) {
                final c = ref.watch(connectionProvider);
                final (icon, color, text) = switch (c.status) {
                  ConnectionStatus.online => (
                      Icons.check_circle_rounded,
                      Colors.green,
                      'Connected',
                    ),
                  ConnectionStatus.connecting => (
                      Icons.sync_rounded,
                      theme.colorScheme.onSurfaceVariant,
                      'Connecting...',
                    ),
                  ConnectionStatus.offline => (
                      Icons.error_outline_rounded,
                      theme.colorScheme.error,
                      'Not reachable',
                    ),
                };
                return Row(
                  children: [
                    Icon(icon, size: 18, color: color),
                    const SizedBox(width: 8),
                    Text(
                      text,
                      style: theme.textTheme.bodyMedium?.copyWith(color: color),
                    ),
                  ],
                );
              },
            ),
          ],
        ],
      ),
    );
  }
}

class _TestOutcome extends StatelessWidget {
  const _TestOutcome({required this.result});
  final ConnectionTestResult result;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final ok = result.success;
    final h = result.health;
    final text = ok
        ? 'Connected: ${h!.width}x${h.height} display, ${h.activeApp == null ? 'no app running' : 'running ${h.activeApp}'}.'
        : result.error!;
    return Container(
      key: Key(ok ? 'test-ok' : 'test-failed'),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: ok ? scheme.secondaryContainer : scheme.errorContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            ok ? Icons.check_circle_rounded : Icons.error_outline_rounded,
            size: 20,
            color: ok ? scheme.onSecondaryContainer : scheme.onErrorContainer,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              text,
              style: TextStyle(
                color: ok ? scheme.onSecondaryContainer : scheme.onErrorContainer,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// "2 days, 3 hours", "1 hour, 5 minutes", "4 minutes", "20 seconds".
String formatUptime(int seconds) {
  final d = Duration(seconds: seconds < 0 ? 0 : seconds);
  String unit(int n, String name) => '$n $name${n == 1 ? '' : 's'}';
  if (d.inDays > 0)
    return d.inHours % 24 == 0 ? unit(d.inDays, 'day') : '${unit(d.inDays, 'day')}, ${unit(d.inHours % 24, 'hour')}';
  if (d.inHours > 0)
    return d.inMinutes % 60 == 0
        ? unit(d.inHours, 'hour')
        : '${unit(d.inHours, 'hour')}, ${unit(d.inMinutes % 60, 'minute')}';
  if (d.inMinutes > 0) return unit(d.inMinutes, 'minute');
  return unit(d.inSeconds, 'second');
}

class _DeviceCard extends ConsumerWidget {
  const _DeviceCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final connection = ref.watch(connectionProvider);
    final settings = ref.watch(deviceSettingsProvider).value;
    final health = connection.health ?? ref.watch(healthProvider).value;
    final scheme = Theme.of(context).colorScheme;

    Widget body;
    if (health == null && settings == null) {
      body = connection.isOffline
          ? Row(
              children: [
                Icon(Icons.cloud_off_rounded, color: scheme.error),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'No device information yet. Check the address above.',
                    style: TextStyle(color: scheme.onSurfaceVariant),
                  ),
                ),
              ],
            )
          : const Padding(
              padding: EdgeInsets.all(8),
              child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
            );
    } else {
      final width = settings?.width ?? health!.width;
      final height = settings?.height ?? health!.height;
      body = Column(
        children: [
          _InfoRow(
            'Status',
            connection.isOffline
                ? 'Offline (last known)'
                : (health?.isOk ?? true)
                    ? 'Running'
                    : (health?.status ?? 'Unknown'),
            color: connection.isOffline || !(health?.isOk ?? true) ? scheme.error : Colors.green,
          ),
          _InfoRow(
            'Display',
            '$width x $height px, ${(settings?.isEnabled ?? health?.isEnabled ?? true) ? 'on' : 'off'}',
          ),
          if (settings != null) _InfoRow('Frame rate', '${settings.fps} fps'),
          _InfoRow('Active app', health?.activeApp ?? 'none'),
          if (health != null) _InfoRow('Uptime', formatUptime(health.uptimeSeconds)),
        ],
      );
    }

    return SectionCard(
      padding: const EdgeInsets.fromLTRB(16, 8, 8, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Spacer(),
              IconButton(
                tooltip: 'Refresh',
                onPressed: () {
                  ref.read(connectionProvider.notifier).check();
                  ref.read(deviceSettingsProvider.notifier).syncSilently();
                },
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
          Padding(padding: const EdgeInsets.only(right: 8), child: body),
        ],
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow(this.label, this.value, {this.color});
  final String label;
  final String value;
  final Color? color;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 5),
        child: Row(
          children: [
            Expanded(
              child: Text(
                label,
                style: TextStyle(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ),
            Text(
              value,
              style: TextStyle(fontWeight: FontWeight.w600, color: color),
            ),
          ],
        ),
      );
}

class _AppearanceCard extends ConsumerWidget {
  const _AppearanceCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final mode = ref.watch(themeModeProvider);
    return SectionCard(
      child: SizedBox(
        width: double.infinity,
        child: SegmentedButton<ThemeMode>(
          showSelectedIcon: false,
          segments: const [
            ButtonSegment(
              value: ThemeMode.system,
              label: Text('System'),
              icon: Icon(Icons.brightness_auto_outlined),
            ),
            ButtonSegment(
              value: ThemeMode.dark,
              label: Text('Dark'),
              icon: Icon(Icons.dark_mode_outlined),
            ),
            ButtonSegment(
              value: ThemeMode.light,
              label: Text('Light'),
              icon: Icon(Icons.light_mode_outlined),
            ),
          ],
          selected: {mode},
          onSelectionChanged: (s) => ref.read(themeModeProvider.notifier).set(s.first),
        ),
      ),
    );
  }
}

class _AboutCard extends StatelessWidget {
  const _AboutCard();

  @override
  Widget build(BuildContext context) => const SectionCard(
        child: Column(
          children: [
            _InfoRow('App', 'LED Matrix Controller'),
            _InfoRow('Version', appVersion),
          ],
        ),
      );
}

class _AudioCard extends ConsumerWidget {
  const _AudioCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final audio = ref.watch(audioStreamProvider);
    final theme = Theme.of(context);
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                audio.isStreaming ? Icons.mic : Icons.mic_off,
                color: audio.isStreaming ? theme.colorScheme.error : null,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Audio streaming', style: theme.textTheme.titleMedium),
                    Text(
                      audio.isStreaming ? 'Microphone is active' : 'Stream audio to the equalizer',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              FilledButton.icon(
                onPressed: () => ref.read(audioStreamProvider.notifier).toggle(),
                icon: Icon(audio.isStreaming ? Icons.stop : Icons.play_arrow),
                label: Text(audio.isStreaming ? 'Stop' : 'Start'),
              ),
            ],
          ),
          if (audio.error != null) ...[
            const SizedBox(height: 12),
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: theme.colorScheme.errorContainer,
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                audio.error!,
                style: TextStyle(
                  color: theme.colorScheme.onErrorContainer,
                  fontSize: 12,
                ),
              ),
            ),
          ],
          const SizedBox(height: 8),
          Text(
            'Set the equalizer to "Microphone" mode in its app settings.',
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
              fontStyle: FontStyle.italic,
            ),
          ),
        ],
      ),
    );
  }
}
