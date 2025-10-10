import 'package:flutter/material.dart';
import '../api_service.dart';

class AppCard extends StatelessWidget {
  final MatrixApp app;
  final bool isActive;
  final VoidCallback onTap;
  final IconData Function(String appId) getAppIcon;

  const AppCard({
    super.key,
    required this.app,
    required this.isActive,
    required this.onTap,
    required this.getAppIcon,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      clipBehavior: Clip.hardEdge,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                getAppIcon(app.id),
                size: 48,
                color: isActive
                    ? Theme.of(context).colorScheme.primary
                    : Theme.of(context).colorScheme.onSurfaceVariant,
              ),
              const SizedBox(height: 12),
              Text(
                app.name,
                style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      color: isActive
                          ? Theme.of(context).colorScheme.primary
                          : Theme.of(context).colorScheme.onSurface,
                    ),
                textAlign: TextAlign.center,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
              if (app.hasSettings || isActive) ...[
                const SizedBox(height: 8),
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    if (app.hasSettings)
                      Icon(
                        Icons.settings,
                        size: 16,
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    if (isActive) ...[
                      if (app.hasSettings) const SizedBox(width: 8),
                      Icon(
                        Icons.check_circle,
                        size: 16,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    ],
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
