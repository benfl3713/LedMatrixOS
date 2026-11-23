import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/screen_models.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';

/// How long the draft must sit still before it is sent to the device for rendering.
const draftPreviewDebounce = Duration(milliseconds: 400);

/// Renders the unsaved [draft] on the device (one off-screen frame) and shows it pixel-crisp in the usual bezel.
/// Edits are debounced; the last good image stays up while a new one loads, and validation errors replace the image.
class DraftPreview extends ConsumerStatefulWidget {
  const DraftPreview({super.key, required this.draft});

  final ScreenDefinition draft;

  @override
  ConsumerState<DraftPreview> createState() => _DraftPreviewState();
}

class _DraftPreviewState extends ConsumerState<DraftPreview> {
  Timer? _timer;
  String? _requested;
  int _seq = 0;
  Uint8List? _image;
  List<String> _errors = const [];
  String? _failure;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _schedule(immediate: true);
  }

  @override
  void didUpdateWidget(DraftPreview old) {
    super.didUpdateWidget(old);
    _schedule();
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  void _schedule({bool immediate = false}) {
    final encoded = widget.draft.encode();
    if (encoded == _requested) return;
    _requested = encoded;
    _timer?.cancel();
    final snapshot = widget.draft.clone();
    if (!immediate) _loading = true;
    _timer = Timer(immediate ? Duration.zero : draftPreviewDebounce, () => _fetch(snapshot));
  }

  Future<void> _fetch(ScreenDefinition snapshot) async {
    final seq = ++_seq;
    if (!_loading) setState(() => _loading = true);
    final result = await ref.read(apiProvider).previewScreen(snapshot);
    if (!mounted || seq != _seq) return; // a newer draft is already on its way
    setState(() {
      _loading = false;
      result.when(
        ok: (bytes) {
          _image = bytes;
          _errors = const [];
          _failure = null;
        },
        err: (e) {
          if (e.statusCode == 400) {
            _errors = e.fieldErrors.isNotEmpty
                ? [for (final f in e.fieldErrors) f.toString()]
                : (e.errors.isNotEmpty ? e.errors : [e.message]);
            _failure = null;
          } else {
            _errors = const [];
            _failure = e.message;
          }
        },
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    final settings = ref.watch(deviceSettingsProvider).value;
    final scheme = Theme.of(context).colorScheme;
    final aspect =
        settings != null && settings.width > 0 && settings.height > 0 ? settings.width / settings.height : 4.0;
    final hasErrors = _errors.isNotEmpty;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GlowPreview(
          glow: _image != null && !hasErrors,
          child: AspectRatio(
            aspectRatio: aspect,
            child: Stack(
              fit: StackFit.expand,
              children: [
                ColoredBox(color: Colors.black, child: hasErrors ? _errorList(scheme) : _picture()),
                if (_loading)
                  const Positioned(
                    right: 8,
                    top: 8,
                    child: SizedBox(
                      key: Key('draft-preview-spinner'),
                      width: 14,
                      height: 14,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  ),
              ],
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.only(left: 4, top: 4),
          child: Text(
            _failure != null
                ? 'Preview unavailable: $_failure'
                : hasErrors
                    ? 'Fix these problems to see your screen.'
                    : 'Preview of your unsaved screen',
            key: const Key('draft-preview-caption'),
            style: Theme.of(context)
                .textTheme
                .labelSmall
                ?.copyWith(color: _failure != null ? scheme.error : scheme.onSurfaceVariant),
          ),
        ),
      ],
    );
  }

  Widget _picture() {
    final image = _image;
    if (image == null) return const SizedBox.expand();
    return Image.memory(
      image,
      key: const Key('draft-preview-image'),
      fit: BoxFit.fill,
      filterQuality: FilterQuality.none,
      gaplessPlayback: true,
      isAntiAlias: false,
      excludeFromSemantics: true,
      errorBuilder: (_, __, ___) => const SizedBox.expand(),
    );
  }

  Widget _errorList(ColorScheme scheme) => SingleChildScrollView(
        key: const Key('draft-preview-errors'),
        padding: const EdgeInsets.all(10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (final e in _errors)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(Icons.error_outline, size: 14, color: scheme.error),
                    const SizedBox(width: 6),
                    Expanded(child: Text(e, style: TextStyle(color: scheme.error, fontSize: 12))),
                  ],
                ),
              ),
          ],
        ),
      );
}
