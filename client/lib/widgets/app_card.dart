import 'dart:math';

import 'package:flutter/material.dart';
import 'package:ledmatrix/matrix_view_model.dart';
import 'package:ledmatrix/models/matrix_app.dart';

class AppCard extends StatelessWidget {
  final MatrixApp? app;
  final MatrixViewModel? viewModel;
  Color color = Color.fromRGBO(
    Random().nextInt(255),
    Random().nextInt(255),
    Random().nextInt(255),
    0.4,
  );
  AppCard({super.key, this.app, required this.viewModel});

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: () {
        if (viewModel != null && app != null) {
          viewModel!.setActiveApp(app!.id);
        }
      },
      child: Card(
        color: color,
        child: Padding(
          padding: const EdgeInsets.all(8.0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Card(child: Icon(Icons.home, size: 45, color: color.withValues(alpha: 1))),
              Text(app?.name ?? 'Unknown', style: TextStyle(fontSize: 18)),
            ],
          ),
        ),
      ),
    );
  }
}
