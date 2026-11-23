import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import '../now/live_matrix.dart';
import 'gamepad_provider.dart';
import 'input_service.dart';

/// Games on the display that the pad can launch.
const gameApps = [('snake', 'Snake'), ('breakout', 'Breakout'), ('tetris', 'Tetris'), ('pong', 'Pong')];

class GamepadPage extends ConsumerStatefulWidget {
  const GamepadPage({super.key});

  @override
  ConsumerState<GamepadPage> createState() => _GamepadPageState();
}

class _GamepadPageState extends ConsumerState<GamepadPage> {
  bool _connected = false;
  StreamSubscription<bool>? _sub;
  InputService? _service;

  @override
  void initState() {
    super.initState();
    SystemChrome.setPreferredOrientations(DeviceOrientation.values);
    // Plugins are missing in widget tests; the pad still works without them.
    WakelockPlus.enable().catchError((Object _) {});
  }

  @override
  void dispose() {
    _sub?.cancel();
    SystemChrome.setPreferredOrientations(const []);
    WakelockPlus.disable().catchError((Object _) {});
    super.dispose();
  }

  void _bind(InputService service) {
    if (identical(_service, service)) return;
    _service = service;
    _sub?.cancel();
    _connected = service.connected;
    _sub = service.connectionChanges.listen((v) {
      if (mounted) setState(() => _connected = v);
    });
  }

  @override
  Widget build(BuildContext context) {
    final service = ref.watch(inputServiceProvider);
    _bind(service);
    final scheme = Theme.of(context).colorScheme;
    final color = _connected ? scheme.primary : scheme.error;

    final status = Semantics(
      label: _connected ? 'Connected' : 'Reconnecting, using HTTP fallback',
      child: Tooltip(
        message: _connected ? 'Connected' : 'Reconnecting (using HTTP fallback)',
        child: Padding(
          padding: const EdgeInsets.only(right: Space.lg),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                _connected ? Icons.wifi_rounded : Icons.wifi_off_rounded,
                key: Key(_connected ? 'pad-connected' : 'pad-disconnected'),
                size: 18,
                color: color,
              ),
              const SizedBox(width: 4),
              Text(
                _connected ? 'Live' : 'Reconnecting',
                style: Theme.of(context).textTheme.labelMedium?.copyWith(color: color),
              ),
            ],
          ),
        ),
      ),
    );

    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 48,
        leading: BackButton(onPressed: () => context.canPop() ? context.pop() : context.go('/apps')),
        titleSpacing: 0,
        title: const Text('Gamepad'),
        actions: [status],
      ),
      body: SafeArea(
        child: LayoutBuilder(builder: (context, c) => c.maxWidth > c.maxHeight ? _landscape() : _portrait()),
      ),
    );
  }

  Widget _preview() {
    final settings = ref.watch(deviceSettingsProvider).value;
    final aspect = settings != null && settings.width > 0 && settings.height > 0
        ? settings.width / settings.height
        : 4.0;
    return Semantics(
      label: 'Live preview of the LED matrix',
      child: GlowPreview(
        glow: false,
        child: AspectRatio(aspectRatio: aspect, child: const LiveMatrix()),
      ),
    );
  }

  Widget _selectStart() => Row(
    mainAxisAlignment: MainAxisAlignment.center,
    mainAxisSize: MainAxisSize.min,
    children: [
      PadKey(button: PadButton.select, label: 'SELECT', width: 84, height: 40, onEvent: _emit),
      const SizedBox(width: Space.lg),
      PadKey(button: PadButton.start, label: 'START', width: 84, height: 40, onEvent: _emit),
    ],
  );

  Widget _portrait() {
    return Padding(
      padding: const EdgeInsets.fromLTRB(Space.lg, Space.xs, Space.lg, Space.sm),
      child: Column(
        children: [
          _preview(),
          const SizedBox(height: Space.sm),
          _PlayerChips(player: ref.watch(playerProvider)),
          const SizedBox(height: Space.xs),
          const _GameChips(),
          const Spacer(),
          FittedBox(
            fit: BoxFit.scaleDown,
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                _DPad(onEvent: _emit),
                const SizedBox(width: Space.md),
                _ActionButtons(onEvent: _emit),
              ],
            ),
          ),
          const SizedBox(height: Space.md),
          _selectStart(),
          const Spacer(),
        ],
      ),
    );
  }

  Widget _landscape() {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: Space.lg, vertical: Space.sm),
      child: Row(
        children: [
          FittedBox(
            fit: BoxFit.scaleDown,
            child: _DPad(onEvent: _emit),
          ),
          const SizedBox(width: Space.lg),
          Expanded(
            child: Center(
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _preview(),
                    const SizedBox(height: Space.sm),
                    _PlayerChips(player: ref.watch(playerProvider)),
                    const SizedBox(height: Space.xs),
                    const _GameChips(),
                    const SizedBox(height: Space.sm),
                    _selectStart(),
                  ],
                ),
              ),
            ),
          ),
          const SizedBox(width: Space.lg),
          FittedBox(
            fit: BoxFit.scaleDown,
            child: _ActionButtons(onEvent: _emit),
          ),
        ],
      ),
    );
  }

  void _emit(PadButton button, PadState state) {
    ref.read(inputServiceProvider).send(ref.read(playerProvider), button, state);
  }
}

String _spoken(PadButton b) => switch (b) {
  PadButton.up => 'Up',
  PadButton.down => 'Down',
  PadButton.left => 'Left',
  PadButton.right => 'Right',
  PadButton.a => 'A button',
  PadButton.b => 'B button',
  PadButton.start => 'Start',
  PadButton.select => 'Select',
};

typedef PadEvent = void Function(PadButton button, PadState state);

/// A touch target that reports down on pointer-down and up on release/cancel. Uses [Listener] so several
/// keys can be held at once (each pointer is tracked independently).
class PadKey extends StatefulWidget {
  const PadKey({
    super.key,
    required this.button,
    required this.onEvent,
    this.label,
    this.icon,
    this.width = 72,
    this.height = 72,
    this.round = false,
  });

  final PadButton button;
  final PadEvent onEvent;
  final String? label;
  final IconData? icon;
  final double width;
  final double height;
  final bool round;

  @override
  State<PadKey> createState() => _PadKeyState();
}

class _PadKeyState extends State<PadKey> {
  int? _pointer;

  @override
  void dispose() {
    // Never leave a button stuck down on the device.
    if (_pointer != null) widget.onEvent(widget.button, PadState.up);
    super.dispose();
  }

  void _down(PointerDownEvent e) {
    if (_pointer != null) return;
    setState(() => _pointer = e.pointer);
    HapticFeedback.lightImpact();
    widget.onEvent(widget.button, PadState.down);
  }

  void _release(PointerEvent e) {
    if (_pointer != e.pointer) return;
    setState(() => _pointer = null);
    widget.onEvent(widget.button, PadState.up);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final pressed = _pointer != null;
    final fg = pressed ? scheme.onPrimary : scheme.onSecondaryContainer;
    return Listener(
      onPointerDown: _down,
      onPointerUp: _release,
      onPointerCancel: _release,
      child: Semantics(
        button: true,
        label: _spoken(widget.button),
        child: AnimatedContainer(
          key: Key('pad-${widget.button.name}'),
          duration: const Duration(milliseconds: 40),
          width: widget.width,
          height: widget.height,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: pressed ? scheme.primary : scheme.secondaryContainer,
            shape: widget.round ? BoxShape.circle : BoxShape.rectangle,
            borderRadius: widget.round ? null : BorderRadius.circular(Radii.md),
            border: Border.all(color: pressed ? scheme.primary : scheme.outlineVariant),
            boxShadow: [if (pressed) BoxShadow(color: scheme.primary.withValues(alpha: 0.45), blurRadius: 18)],
          ),
          child: widget.icon != null
              ? Icon(widget.icon, size: 40, color: fg)
              : Text(
                  widget.label ?? '',
                  style: TextStyle(fontWeight: FontWeight.bold, fontSize: widget.round ? 28 : 13, color: fg),
                ),
        ),
      ),
    );
  }
}

class _PlayerChips extends ConsumerWidget {
  const _PlayerChips({required this.player});
  final int player;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Row(
      key: const Key('player-selector'),
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        for (var i = 0; i < 4; i++)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 3),
            child: Semantics(
              label: 'Player ${i + 1}',
              selected: player == i,
              child: ChoiceChip(
                label: Text('P${i + 1}'),
                selected: player == i,
                showCheckmark: false,
                visualDensity: VisualDensity.compact,
                onSelected: (_) => ref.read(playerProvider.notifier).select(i),
              ),
            ),
          ),
      ],
    );
  }
}

class _GameChips extends ConsumerWidget {
  const _GameChips();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final active = ref.watch(appListProvider.select((a) => a.value?.activeApp));
    return Wrap(
      alignment: WrapAlignment.center,
      spacing: 6,
      children: [
        for (final (id, name) in gameApps)
          Semantics(
            label: 'Play $name',
            selected: active == id,
            child: ChoiceChip(
              key: Key('game-$id'),
              label: Text(name),
              selected: active == id,
              showCheckmark: false,
              visualDensity: VisualDensity.compact,
              onSelected: (_) => ref.read(appListProvider.notifier).activate(id),
            ),
          ),
      ],
    );
  }
}

class _DPad extends StatelessWidget {
  const _DPad({required this.onEvent});
  final PadEvent onEvent;

  @override
  Widget build(BuildContext context) {
    const k = 60.0;
    Widget key(PadButton b, IconData i) => PadKey(button: b, icon: i, width: k, height: k, onEvent: onEvent);
    return SizedBox(
      width: k * 3,
      height: k * 3,
      child: Stack(
        children: [
          Positioned(left: k, top: 0, child: key(PadButton.up, Icons.keyboard_arrow_up_rounded)),
          Positioned(left: 0, top: k, child: key(PadButton.left, Icons.keyboard_arrow_left_rounded)),
          Positioned(left: k * 2, top: k, child: key(PadButton.right, Icons.keyboard_arrow_right_rounded)),
          Positioned(left: k, top: k * 2, child: key(PadButton.down, Icons.keyboard_arrow_down_rounded)),
        ],
      ),
    );
  }
}

class _ActionButtons extends StatelessWidget {
  const _ActionButtons({required this.onEvent});
  final PadEvent onEvent;

  @override
  Widget build(BuildContext context) {
    const k = 76.0;
    return SizedBox(
      width: k * 1.7,
      height: k * 2,
      child: Stack(
        children: [
          Positioned(
            left: 0,
            bottom: 0,
            child: PadKey(button: PadButton.b, label: 'B', width: k, height: k, round: true, onEvent: onEvent),
          ),
          Positioned(
            right: 0,
            top: 0,
            child: PadKey(button: PadButton.a, label: 'A', width: k, height: k, round: true, onEvent: onEvent),
          ),
        ],
      ),
    );
  }
}
