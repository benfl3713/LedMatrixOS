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
    final colorScheme = Theme.of(context).colorScheme;

    return DraggableScrollableSheet(
      initialChildSize: 0.6,
      minChildSize: 0.3,
      maxChildSize: 0.9,
      expand: false,
      builder: (context, scrollController) => Container(
        decoration: BoxDecoration(
          color: colorScheme.surfaceContainerLow,
          borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Handle + accent top border
            Container(
              decoration: BoxDecoration(
                border: Border(
                  top: BorderSide(
                    color: colorScheme.primary.withOpacity(0.45),
                    width: 2,
                  ),
                ),
                borderRadius:
                    const BorderRadius.vertical(top: Radius.circular(24)),
              ),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Drag handle
                    Center(
                      child: Container(
                        width: 36,
                        height: 4,
                        decoration: BoxDecoration(
                          color: colorScheme.primary.withOpacity(0.4),
                          borderRadius: BorderRadius.circular(2),
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),

                    // Header
                    Row(
                      children: [
                        Container(
                          padding: const EdgeInsets.all(10),
                          decoration: BoxDecoration(
                            color: colorScheme.primary.withOpacity(0.12),
                            borderRadius: BorderRadius.circular(12),
                            border: Border.all(
                              color: colorScheme.primary.withOpacity(0.3),
                              width: 1,
                            ),
                            boxShadow: [
                              BoxShadow(
                                color: colorScheme.primary.withOpacity(0.18),
                                blurRadius: 12,
                              ),
                            ],
                          ),
                          child: Icon(
                            widget.getAppIcon(widget.app.id),
                            size: 22,
                            color: colorScheme.primary,
                          ),
                        ),
                        const SizedBox(width: 14),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                widget.app.name.toUpperCase(),
                                style: TextStyle(
                                  fontSize: 16,
                                  fontWeight: FontWeight.w900,
                                  color: colorScheme.onSurface,
                                  letterSpacing: 1.2,
                                ),
                              ),
                              const SizedBox(height: 2),
                              Text(
                                'CONFIGURE APP',
                                style: TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.w700,
                                  color: colorScheme.primary,
                                  letterSpacing: 1.5,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),
                    Divider(
                        height: 1,
                        color: colorScheme.outline.withOpacity(0.15)),
                  ],
                ),
              ),
            ),

            // Settings content
            Expanded(
              child: _localSettings.isEmpty
                  ? Center(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(
                            Icons.tune_rounded,
                            size: 52,
                            color:
                                colorScheme.onSurfaceVariant.withOpacity(0.25),
                          ),
                          const SizedBox(height: 14),
                          Text(
                            'NO SETTINGS',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w800,
                              color:
                                  colorScheme.onSurfaceVariant.withOpacity(0.4),
                              letterSpacing: 2,
                            ),
                          ),
                        ],
                      ),
                    )
                  : ListView(
                      controller: scrollController,
                      padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
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
