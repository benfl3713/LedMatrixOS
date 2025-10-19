import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../controllers/audio_stream_controller.dart';
import '../controllers/api_settings_controller.dart';

/// Widget that shows audio streaming controls for the equalizer app
class AudioStreamWidget extends StatelessWidget {
  const AudioStreamWidget({super.key});

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (_) {
        final apiController = Provider.of<ApiSettingsController>(context, listen: false);
        final controller = AudioStreamController();
        controller.initialize(apiController.apiUrl);
        return controller;
      },
      child: const _AudioStreamContent(),
    );
  }
}

class _AudioStreamContent extends StatelessWidget {
  const _AudioStreamContent();

  @override
  Widget build(BuildContext context) {
    final controller = Provider.of<AudioStreamController>(context);
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.all(16),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(
                  controller.isStreaming ? Icons.mic : Icons.mic_off,
                  color: controller.isStreaming ? Colors.red : null,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Audio Streaming',
                        style: theme.textTheme.titleMedium,
                      ),
                      if (controller.isStreaming)
                        Text(
                          'Microphone is active',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: Colors.red,
                          ),
                        )
                      else
                        Text(
                          'Stream audio to visualizer',
                          style: theme.textTheme.bodySmall,
                        ),
                    ],
                  ),
                ),
                FilledButton.icon(
                  onPressed: controller.toggleStreaming,
                  icon: Icon(
                    controller.isStreaming ? Icons.stop : Icons.play_arrow,
                  ),
                  label: Text(controller.isStreaming ? 'Stop' : 'Start'),
                  style: FilledButton.styleFrom(
                    backgroundColor: controller.isStreaming
                        ? theme.colorScheme.error
                        : theme.colorScheme.primary,
                  ),
                ),
              ],
            ),
            if (controller.error != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: theme.colorScheme.errorContainer,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Row(
                  children: [
                    Icon(
                      Icons.error_outline,
                      color: theme.colorScheme.error,
                      size: 20,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        controller.error!,
                        style: TextStyle(
                          color: theme.colorScheme.onErrorContainer,
                          fontSize: 12,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 8),
            Text(
              'Tip: Make sure the equalizer is set to "Microphone" mode in app settings',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
                fontStyle: FontStyle.italic,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

