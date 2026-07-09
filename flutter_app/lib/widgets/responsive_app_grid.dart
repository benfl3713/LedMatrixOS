import 'package:flutter/material.dart';
import '../api_service.dart';
import 'app_card.dart';
import '../utils/app_icon_helper.dart';

class ResponsiveAppGrid extends StatelessWidget {
  final List<MatrixApp> apps;
  final String? activeAppId;
  final Function(String appId) onActivateApp;
  final Function(MatrixApp app) onShowSettings;

  const ResponsiveAppGrid({
    super.key,
    required this.apps,
    required this.activeAppId,
    required this.onActivateApp,
    required this.onShowSettings,
  });

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        // Calculate number of columns based on screen width
        final screenWidth = constraints.maxWidth;
        int crossAxisCount;

        if (screenWidth > 1200) {
          crossAxisCount = 6;
        } else if (screenWidth > 900) {
          crossAxisCount = 5;
        } else if (screenWidth > 700) {
          crossAxisCount = 4;
        } else if (screenWidth > 500) {
          crossAxisCount = 4;
        } else {
          crossAxisCount = 3;
        }

        return GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: crossAxisCount,
            childAspectRatio: 0.95,
            crossAxisSpacing: 8,
            mainAxisSpacing: 8,
          ),
          itemCount: apps.length,
          itemBuilder: (context, index) {
            final app = apps[index];
            final isActive = app.id == activeAppId;
            
            return ConstrainedBox(
              constraints: BoxConstraints(maxHeight: 100),
              child: AppCard(
                app: app,
                isActive: isActive,
                getAppIcon: AppIconHelper.getAppIcon,
                onTap: () {
                  if (isActive && app.hasSettings) {
                    onShowSettings(app);
                  } else {
                    onActivateApp(app.id);
                  }
                },
              ),
            );
          },
        );
      },
    );
  }
}
