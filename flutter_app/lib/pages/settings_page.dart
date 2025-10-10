import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../controllers/api_settings_controller.dart';

class SettingsPage extends StatefulWidget {
  const SettingsPage({super.key});

  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  late TextEditingController _urlController;
  bool _isValidUrl = true;

  @override
  void initState() {
    super.initState();
    final controller = Provider.of<ApiSettingsController>(context, listen: false);
    _urlController = TextEditingController(text: controller.apiUrl);
    _urlController.addListener(_validateUrl);
  }

  @override
  void dispose() {
    _urlController.dispose();
    super.dispose();
  }

  void _validateUrl() {
    final url = _urlController.text.trim();
    final isValid = url.isNotEmpty && 
        (url.startsWith('http://') || url.startsWith('https://'));
    if (_isValidUrl != isValid) {
      setState(() {
        _isValidUrl = isValid;
      });
    }
  }

  Future<void> _saveSettings() async {
    if (!_isValidUrl) return;

    final controller = Provider.of<ApiSettingsController>(context, listen: false);
    await controller.updateApiUrl(_urlController.text.trim());
    
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('API URL updated successfully'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  Future<void> _resetToDefault() async {
    final controller = Provider.of<ApiSettingsController>(context, listen: false);
    await controller.resetToDefault();
    _urlController.text = controller.apiUrl;
    
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('API URL reset to default'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Settings'),
        backgroundColor: colorScheme.surface,
        foregroundColor: colorScheme.onSurface,
        elevation: 0,
      ),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'API Configuration',
              style: theme.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'Configure the URL for your LED Matrix API server',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 24),
            
            // API URL Input
            Text(
              'API Server URL',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w500,
              ),
            ),
            const SizedBox(height: 8),
            TextFormField(
              controller: _urlController,
              decoration: InputDecoration(
                hintText: 'http://localhost:5000',
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(12),
                ),
                prefixIcon: const Icon(Icons.link),
                suffixIcon: _isValidUrl
                    ? Icon(
                        Icons.check_circle,
                        color: colorScheme.primary,
                      )
                    : Icon(
                        Icons.error,
                        color: colorScheme.error,
                      ),
                errorText: _isValidUrl ? null : 'Please enter a valid URL',
              ),
              keyboardType: TextInputType.url,
              textInputAction: TextInputAction.done,
              onFieldSubmitted: (_) => _saveSettings(),
            ),
            const SizedBox(height: 16),
            
            // Action Buttons
            Row(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed: _isValidUrl ? _saveSettings : null,
                    icon: const Icon(Icons.save),
                    label: const Text('Save Settings'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: _resetToDefault,
                    icon: const Icon(Icons.refresh),
                    label: const Text('Reset to Default'),
                  ),
                ),
              ],
            ),
            
            const SizedBox(height: 32),
            
            // Current Settings Info
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(
                          Icons.info_outline,
                          color: colorScheme.primary,
                          size: 20,
                        ),
                        const SizedBox(width: 8),
                        Text(
                          'Current Settings',
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    Consumer<ApiSettingsController>(
                      builder: (context, controller, child) {
                        return Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            _buildInfoRow(
                              'API URL',
                              controller.apiUrl,
                              Icons.link,
                            ),
                            const SizedBox(height: 8),
                            _buildInfoRow(
                              'Status',
                              'Connected',
                              Icons.check_circle,
                              colorScheme.primary,
                            ),
                          ],
                        );
                      },
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

  Widget _buildInfoRow(String label, String value, IconData icon, [Color? iconColor]) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    
    return Row(
      children: [
        Icon(
          icon,
          size: 16,
          color: iconColor ?? colorScheme.onSurfaceVariant,
        ),
        const SizedBox(width: 8),
        Text(
          '$label: ',
          style: theme.textTheme.bodyMedium?.copyWith(
            fontWeight: FontWeight.w500,
          ),
        ),
        Expanded(
          child: Text(
            value,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: colorScheme.onSurfaceVariant,
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}
