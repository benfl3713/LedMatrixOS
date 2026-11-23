import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/router.dart';
import 'core/theme.dart';
import 'core/theme_mode_provider.dart';

void main() {
  runApp(const ProviderScope(retry: _noRetry, child: LedMatrixApp()));
}

/// Failed loads show an error with a Retry button instead of silently retrying.
Duration? _noRetry(int retryCount, Object error) => null;

class LedMatrixApp extends ConsumerWidget {
  const LedMatrixApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      title: 'LED Matrix',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(Brightness.light),
      darkTheme: buildTheme(Brightness.dark),
      themeMode: ref.watch(themeModeProvider),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
