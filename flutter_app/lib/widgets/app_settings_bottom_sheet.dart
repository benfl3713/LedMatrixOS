import 'package:flutter/material.dart';
import '../api_service.dart';

class AppSettingsBottomSheet extends StatefulWidget {
  final MatrixApp app;
  final List<AppSetting> appSettings;
  final Function(String key, dynamic value) onUpdateSetting;
  final IconData Function(String appId) getAppIcon;

  const AppSettingsBottomSheet({
    super.key,
    required this.app,
    required this.appSettings,
    required this.onUpdateSetting,
    required this.getAppIcon,
  });

  @override
  State<AppSettingsBottomSheet> createState() => _AppSettingsBottomSheetState();
}

class _AppSettingsBottomSheetState extends State<AppSettingsBottomSheet> {
  late List<AppSetting> _localSettings;

  @override
  void initState() {
    super.initState();
    _localSettings = List.from(widget.appSettings);
  }

  @override
  void didUpdateWidget(AppSettingsBottomSheet oldWidget) {
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
