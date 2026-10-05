import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'input_service.dart';

/// Owns the input connection while something watches it. Tests override this provider.
final inputServiceProvider = Provider.autoDispose<InputService>((ref) {
  final service = InputService(baseUrl: ref.watch(apiUrlProvider));
  ref.onDispose(service.dispose);
  service.start();
  return service;
});

/// Selected player, 0-based.
class PlayerNotifier extends Notifier<int> {
  @override
  int build() => 0;

  void select(int player) => state = player.clamp(0, 3);
}

final playerProvider = NotifierProvider<PlayerNotifier, int>(PlayerNotifier.new);
