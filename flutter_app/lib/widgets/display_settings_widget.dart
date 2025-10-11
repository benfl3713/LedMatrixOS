import 'package:flutter/material.dart';
import '../api_service.dart';

class DisplaySettingsWidget extends StatelessWidget {
  final MatrixSettings settings;
  final Function(double brightness) onBrightnessChanged;

  const DisplaySettingsWidget({
    super.key,
    required this.settings,
    required this.onBrightnessChanged,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'Resolution: ${settings.width} x ${settings.height}',
                  style: Theme.of(context).textTheme.bodyLarge,
                ),
                Chip(
                  label: Text(settings.isRunning ? 'Running' : 'Stopped'),
                  avatar: Icon(
                    settings.isRunning ? Icons.play_circle : Icons.stop_circle,
                    size: 16,
                  ),
                ),
              ],
            ),
            const Divider(height: 32),
            Text('Brightness: ${settings.brightness}%'),
            Slider(
              value: settings.brightness.toDouble(),
              min: 0,
              max: 100,
              divisions: 100,
              label: '${settings.brightness}%',
              onChanged: onBrightnessChanged,
            ),
          ],
        ),
      ),
    );
  }
}
