import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';
import 'audio_stream_provider.dart';

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: const [
          _DeviceUrlCard(),
          SizedBox(height: 12),
          _HealthCard(),
          SizedBox(height: 12),
          _AudioCard(),
        ],
      ),
    );
  }
}

class _DeviceUrlCard extends ConsumerStatefulWidget {
  const _DeviceUrlCard();

  @override
  ConsumerState<_DeviceUrlCard> createState() => _DeviceUrlCardState();
}

class _DeviceUrlCardState extends ConsumerState<_DeviceUrlCard> {
  late final TextEditingController _controller = TextEditingController(text: ref.read(apiUrlProvider));

  bool get _valid {
    final uri = Uri.tryParse(_controller.text.trim());
    return uri != null && (uri.scheme == 'http' || uri.scheme == 'https') && uri.host.isNotEmpty;
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    await ref.read(apiUrlProvider.notifier).save(_controller.text);
    if (!mounted) return;
    ref.invalidate(healthProvider);
    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Device URL saved')));
  }

  @override
  Widget build(BuildContext context) {
    // Pick up the persisted URL once it has loaded from storage.
    ref.listen(apiUrlProvider, (previous, next) {
      if (_controller.text != next) _controller.text = next;
    });
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Device', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 12),
          TextField(
            key: const Key('device-url'),
            controller: _controller,
            keyboardType: TextInputType.url,
            autocorrect: false,
            decoration: InputDecoration(
              labelText: 'Device URL',
              hintText: 'http://192.168.1.50:5005',
              border: const OutlineInputBorder(),
              errorText: _valid ? null : 'Enter a full http:// or https:// address',
            ),
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton(onPressed: _valid ? _save : null, child: const Text('Save')),
              const SizedBox(width: 8),
              TextButton(
                onPressed: () async {
                  await ref.read(apiUrlProvider.notifier).reset();
                  _controller.text = ref.read(apiUrlProvider);
                  ref.invalidate(healthProvider);
                },
                child: const Text('Reset'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

String formatUptime(int seconds) {
  final d = Duration(seconds: seconds);
  if (d.inDays > 0) return '${d.inDays}d ${d.inHours % 24}h';
  if (d.inHours > 0) return '${d.inHours}h ${d.inMinutes % 60}m';
  if (d.inMinutes > 0) return '${d.inMinutes}m ${d.inSeconds % 60}s';
  return '${d.inSeconds}s';
}

class _HealthCard extends ConsumerWidget {
  const _HealthCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final health = ref.watch(healthProvider);
    final scheme = Theme.of(context).colorScheme;
    return SectionCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text('Status', style: Theme.of(context).textTheme.titleMedium)),
              IconButton(
                tooltip: 'Refresh',
                onPressed: () => ref.invalidate(healthProvider),
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
          health.when(
            loading: () => const Padding(
              padding: EdgeInsets.all(8),
              child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
            ),
            error: (e, _) => Row(
              children: [
                Icon(Icons.error_outline, color: scheme.error),
                const SizedBox(width: 8),
                Expanded(child: Text(e.toString(), style: TextStyle(color: scheme.error))),
              ],
            ),
            data: (Health h) => Column(
              children: [
                _row(context, 'Health', h.isOk ? 'OK' : h.status, color: h.isOk ? Colors.green : scheme.error),
                _row(context, 'Active app', h.activeApp ?? 'none'),
                _row(context, 'Display', '${h.width}x${h.height}, ${h.isEnabled ? 'on' : 'off'}'),
                _row(context, 'Uptime', formatUptime(h.uptimeSeconds)),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _row(BuildContext context, String label, String value, {Color? color}) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Row(
          children: [
            Expanded(child: Text(label)),
            Text(value, style: TextStyle(fontWeight: FontWeight.w600, color: color)),
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
              Icon(audio.isStreaming ? Icons.mic : Icons.mic_off, color: audio.isStreaming ? theme.colorScheme.error : null),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Audio streaming', style: theme.textTheme.titleMedium),
                    Text(audio.isStreaming ? 'Microphone is active' : 'Stream audio to the equalizer',
                        style: theme.textTheme.bodySmall),
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
              decoration: BoxDecoration(color: theme.colorScheme.errorContainer, borderRadius: BorderRadius.circular(8)),
              child: Text(audio.error!, style: TextStyle(color: theme.colorScheme.onErrorContainer, fontSize: 12)),
            ),
          ],
          const SizedBox(height: 8),
          Text(
            'Set the equalizer to "Microphone" mode in its app settings.',
            style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant, fontStyle: FontStyle.italic),
          ),
        ],
      ),
    );
  }
}
