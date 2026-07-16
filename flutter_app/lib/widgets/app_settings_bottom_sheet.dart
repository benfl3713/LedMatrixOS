import 'package:flutter/material.dart';
import '../api_service.dart';
import 'glass_container.dart';

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
    final colorScheme = Theme.of(context).colorScheme;
    
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: GlassContainer(
        borderRadius: 20,
        opacity: 0.05,
        blur: 5,
        padding: const EdgeInsets.all(16),
        border: Border.all(
          color: colorScheme.onSurface.withValues(alpha: 0.08),
          width: 1,
        ),
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
                          setting.name.toUpperCase(),
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w900,
                            letterSpacing: 1.0,
                            color: colorScheme.onSurface,
                          ),
                        ),
                        if (setting.description.isNotEmpty) ...[
                          const SizedBox(height: 4),
                          Text(
                            setting.description,
                            style: TextStyle(
                              fontSize: 11,
                              color: colorScheme.onSurfaceVariant,
                            ),
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
                setting.name.toUpperCase(),
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 1.0,
                  color: colorScheme.onSurface,
                ),
              ),
              if (setting.description.isNotEmpty) ...[
                const SizedBox(height: 4),
                Text(
                  setting.description,
                  style: TextStyle(
                    fontSize: 11,
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
              const SizedBox(height: 16),
              _buildSettingInput(setting),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildSettingInput(AppSetting setting) {
    final colorScheme = Theme.of(context).colorScheme;
    
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
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '$currentValue',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w900,
                    color: colorScheme.primary,
                  ),
                ),
                Text(
                  '$maxValue',
                  style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.w600,
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
            SliderTheme(
              data: SliderTheme.of(context).copyWith(
                trackHeight: 4,
                thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 8),
              ),
              child: Slider(
                value: currentValue.toDouble(),
                min: minValue.toDouble(),
                max: maxValue.toDouble(),
                divisions: (maxValue - minValue) > 0 ? maxValue - minValue : 1,
                label: currentValue.toString(),
                onChanged: (value) =>
                    _updateLocalSetting(setting.key, value.round()),
              ),
            ),
          ],
        );

      case AppSettingType.select:
        return DropdownButtonFormField<String>(
          initialValue: setting.currentValue.toString(),
          decoration: InputDecoration(
            filled: true,
            fillColor: colorScheme.onSurface.withValues(alpha: 0.05),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(12),
              borderSide: BorderSide.none,
            ),
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
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
          decoration: InputDecoration(
            filled: true,
            fillColor: colorScheme.onSurface.withValues(alpha: 0.05),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(12),
              borderSide: BorderSide.none,
            ),
            contentPadding: const EdgeInsets.all(16),
          ),
          onChanged: (value) => _updateLocalSetting(setting.key, value),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return DraggableScrollableSheet(
      initialChildSize: 0.7,
      minChildSize: 0.4,
      maxChildSize: 0.95,
      expand: false,
      builder: (context, scrollController) => GlassContainer(
        borderRadius: 36,
        opacity: 0.8,
        blur: 20,
        border: Border.all(
          color: colorScheme.onSurface.withValues(alpha: 0.1),
          width: 1.5,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Handle + header
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 16, 20, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Drag handle
                  Center(
                    child: Container(
                      width: 40,
                      height: 5,
                      decoration: BoxDecoration(
                        color: colorScheme.onSurface.withValues(alpha: 0.1),
                        borderRadius: BorderRadius.circular(2.5),
                      ),
                    ),
                  ),
                  const SizedBox(height: 24),

                  // Header
                  Row(
                    children: [
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: colorScheme.primary.withValues(alpha: 0.15),
                          borderRadius: BorderRadius.circular(16),
                        ),
                        child: Icon(
                          widget.getAppIcon(widget.app.id),
                          size: 28,
                          color: colorScheme.primary,
                        ),
                      ),
                      const SizedBox(width: 16),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              widget.app.name,
                              style: TextStyle(
                                fontSize: 20,
                                fontWeight: FontWeight.w900,
                                color: colorScheme.onSurface,
                                letterSpacing: -0.5,
                              ),
                            ),
                            Text(
                              'APP SETTINGS',
                              style: TextStyle(
                                fontSize: 11,
                                fontWeight: FontWeight.w800,
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
                ],
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
                            size: 64,
                            color: colorScheme.onSurface.withValues(alpha: 0.1),
                          ),
                          const SizedBox(height: 16),
                          Text(
                            'NO CONFIGURATION',
                            style: TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w900,
                              color: colorScheme.onSurface.withValues(alpha: 0.2),
                              letterSpacing: 2,
                            ),
                          ),
                        ],
                      ),
                    )
                  : ListView(
                      controller: scrollController,
                      padding: const EdgeInsets.fromLTRB(20, 8, 20, 40),
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
