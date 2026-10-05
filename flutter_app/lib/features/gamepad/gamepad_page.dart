import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'gamepad_provider.dart';
import 'input_service.dart';

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
  }

  @override
  void dispose() {
    _sub?.cancel();
    SystemChrome.setPreferredOrientations(const []);
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
    final player = ref.watch(playerProvider);
    final scheme = Theme.of(context).colorScheme;

    final selector = SegmentedButton<int>(
      key: const Key('player-selector'),
      showSelectedIcon: false,
      segments: [for (var i = 0; i < 4; i++) ButtonSegment(value: i, label: Text('P${i + 1}'))],
      selected: {player},
      onSelectionChanged: (s) => ref.read(playerProvider.notifier).select(s.first),
    );

    return Scaffold(
      appBar: AppBar(
        leading: BackButton(onPressed: () => context.canPop() ? context.pop() : context.go('/apps')),
        title: const Text('Gamepad'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 16),
            child: Tooltip(
              message: _connected ? 'Connected' : 'Reconnecting (using HTTP fallback)',
              child: Icon(
                _connected ? Icons.wifi_rounded : Icons.wifi_off_rounded,
                key: Key(_connected ? 'pad-connected' : 'pad-disconnected'),
                color: _connected ? scheme.primary : scheme.error,
              ),
            ),
          ),
        ],
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            children: [
              selector,
              const SizedBox(height: 8),
              Expanded(
                child: Row(
                  children: [
                    Expanded(child: Center(child: _DPad(onEvent: _emit))),
                    Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        PadKey(button: PadButton.select, label: 'SELECT', width: 96, height: 44, onEvent: _emit),
                        const SizedBox(height: 16),
                        PadKey(button: PadButton.start, label: 'START', width: 96, height: 44, onEvent: _emit),
                      ],
                    ),
                    Expanded(child: Center(child: _ActionButtons(onEvent: _emit))),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _emit(PadButton button, PadState state) {
    ref.read(inputServiceProvider).send(ref.read(playerProvider), button, state);
  }
}

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
    HapticFeedback.selectionClick();
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
        label: widget.button.name,
        child: Container(
          key: Key('pad-${widget.button.name}'),
          width: widget.width,
          height: widget.height,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: pressed ? scheme.primary : scheme.secondaryContainer,
            shape: widget.round ? BoxShape.circle : BoxShape.rectangle,
            borderRadius: widget.round ? null : BorderRadius.circular(14),
          ),
          child: widget.icon != null
              ? Icon(widget.icon, size: 36, color: fg)
              : Text(
                  widget.label ?? '',
                  style: TextStyle(fontWeight: FontWeight.bold, fontSize: widget.round ? 24 : 13, color: fg),
                ),
        ),
      ),
    );
  }
}

class _DPad extends StatelessWidget {
  const _DPad({required this.onEvent});
  final PadEvent onEvent;

  @override
  Widget build(BuildContext context) {
    const k = 84.0;
    Widget key(PadButton b, IconData i) => PadKey(button: b, icon: i, width: k, height: k, onEvent: onEvent);
    return FittedBox(
      child: SizedBox(
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
      ),
    );
  }
}

class _ActionButtons extends StatelessWidget {
  const _ActionButtons({required this.onEvent});
  final PadEvent onEvent;

  @override
  Widget build(BuildContext context) {
    const k = 96.0;
    return FittedBox(
      child: SizedBox(
        width: k * 2.2,
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
      ),
    );
  }
}
