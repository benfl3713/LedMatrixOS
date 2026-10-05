import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/media_models.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';
import 'media_provider.dart';

class MediaPage extends ConsumerWidget {
  const MediaPage({super.key});

  Future<void> _upload(BuildContext context, WidgetRef ref, MediaCapabilities caps) async {
    final messenger = ScaffoldMessenger.of(context);
    final MediaSource? source = ref.read(photoLibraryAvailableProvider) ? await _chooseSource(context) : MediaSource.files;
    if (source == null) return;
    final PickedMedia? file;
    try {
      file = await ref.read(mediaPickerProvider).pick(allowVideo: caps.video, source: source);
    } catch (e) {
      messenger.showSnackBar(SnackBar(content: Text('Could not open the file chooser: $e')));
      return;
    }
    if (file == null) return;
    final error = await ref.read(mediaListProvider.notifier).upload(file, caps);
    if (error != null) {
      messenger
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(error), behavior: SnackBarBehavior.floating));
    } else {
      messenger
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text('Uploaded ${file.name}')));
    }
  }

  Future<MediaSource?> _chooseSource(BuildContext context) {
    return showModalBottomSheet<MediaSource>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          ListTile(
            key: const Key('media-source-photos'),
            leading: const Icon(Icons.photo_library_outlined),
            title: const Text('Photo library'),
            onTap: () => Navigator.pop(context, MediaSource.photos),
          ),
          ListTile(
            key: const Key('media-source-files'),
            leading: const Icon(Icons.folder_open_outlined),
            title: const Text('Files'),
            onTap: () => Navigator.pop(context, MediaSource.files),
          ),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final media = ref.watch(mediaListProvider);
    final caps = ref.watch(mediaCapabilitiesProvider).value ?? const MediaCapabilities();
    final upload = ref.watch(uploadProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Media')),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('media-upload'),
        onPressed: upload == null ? () => _upload(context, ref, caps) : null,
        icon: const Icon(Icons.upload_rounded),
        label: const Text('Upload'),
      ),
      body: media.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(mediaListProvider)),
        data: (items) => RefreshIndicator(
          onRefresh: () => ref.read(mediaListProvider.notifier).refresh(),
          child: CustomScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            slivers: [
              SliverToBoxAdapter(child: _Header(caps: caps, upload: upload, hasItems: items.isNotEmpty)),
              if (items.isEmpty)
                const SliverFillRemaining(hasScrollBody: false, child: _EmptyState())
              else
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 96),
                  sliver: SliverGrid.builder(
                    gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                      maxCrossAxisExtent: 180,
                      mainAxisExtent: 150,
                      crossAxisSpacing: 12,
                      mainAxisSpacing: 12,
                    ),
                    itemCount: items.length,
                    itemBuilder: (context, i) => _MediaTile(item: items[i]),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Header extends ConsumerWidget {
  const _Header({required this.caps, required this.upload, required this.hasItems});

  final MediaCapabilities caps;
  final UploadState? upload;
  final bool hasItems;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          FilledButton.icon(
            key: const Key('media-play-all'),
            onPressed: () async {
              final ok = await ref.read(mediaListProvider.notifier).playAll();
              if (ok && context.mounted) {
                ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Playing media on the display')));
              }
            },
            icon: const Icon(Icons.play_arrow_rounded),
            label: const Text('Play on display'),
          ),
          if (upload != null) ...[
            const SizedBox(height: 12),
            Text('Uploading ${upload!.name}', key: const Key('media-uploading'), maxLines: 1, overflow: TextOverflow.ellipsis),
            const SizedBox(height: 4),
            LinearProgressIndicator(value: upload!.progress > 0 ? upload!.progress : null),
          ],
          if (!caps.video) ...[
            const SizedBox(height: 12),
            Row(
              key: const Key('media-video-hint'),
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.info_outline_rounded, size: 18, color: scheme.onSurfaceVariant),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Video is not available: install ffmpeg on the device to upload videos. Pictures and GIFs work.',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
                  ),
                ),
              ],
            ),
          ],
          const SizedBox(height: 4),
          Text(
            'Up to ${formatBytes(caps.maxBytes)} per file.',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
          ),
        ],
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.photo_library_outlined, size: 56, color: scheme.onSurfaceVariant),
          const SizedBox(height: 12),
          Text('No media yet', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 4),
          Text('Upload a picture or GIF to show it on the display.',
              textAlign: TextAlign.center, style: TextStyle(color: scheme.onSurfaceVariant)),
        ],
      ),
    );
  }
}

class _MediaTile extends ConsumerWidget {
  const _MediaTile({required this.item});

  final MediaItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    final api = ref.watch(apiProvider);
    return Card(
      clipBehavior: Clip.antiAlias,
      margin: EdgeInsets.zero,
      child: InkWell(
        key: Key('media-tile-${item.id}'),
        onTap: () => _showActions(context, ref, item),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(
              child: Stack(
                fit: StackFit.expand,
                children: [
                  ColoredBox(
                    color: Colors.black,
                    child: Image.network(
                      api.mediaThumbUrl(item.id),
                      fit: BoxFit.contain,
                      filterQuality: FilterQuality.none,
                      errorBuilder: (_, __, ___) => Icon(Icons.broken_image_outlined, color: scheme.onSurfaceVariant),
                    ),
                  ),
                  if (item.isGif || item.isVideo)
                    Positioned(
                      top: 6,
                      left: 6,
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(color: scheme.primaryContainer, borderRadius: BorderRadius.circular(6)),
                        child: Text(item.isGif ? 'GIF' : 'VIDEO',
                            style: Theme.of(context)
                                .textTheme
                                .labelSmall
                                ?.copyWith(color: scheme.onPrimaryContainer, fontWeight: FontWeight.bold)),
                      ),
                    ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(8, 6, 8, 6),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.titleSmall),
                  Text('${item.width}x${item.height}',
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(color: scheme.onSurfaceVariant)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

void _showActions(BuildContext context, WidgetRef ref, MediaItem item) {
  showModalBottomSheet<void>(
    context: context,
    showDragHandle: true,
    builder: (sheetContext) => SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            title: Text(item.name),
            subtitle: Text([
              item.kind.toUpperCase(),
              '${item.width}x${item.height}',
              if (item.frames > 1) '${item.frames} frames',
              if (item.durationMs > 0) '${(item.durationMs / 1000).toStringAsFixed(1)}s',
            ].join(' - ')),
          ),
          ListTile(
            key: const Key('media-play-one'),
            leading: const Icon(Icons.play_arrow_rounded),
            title: const Text('Play only this'),
            onTap: () async {
              Navigator.pop(sheetContext);
              final ok = await ref.read(mediaListProvider.notifier).playOne(item.id);
              if (ok && context.mounted) {
                ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Playing ${item.name}')));
              }
            },
          ),
          ListTile(
            key: const Key('media-delete'),
            leading: Icon(Icons.delete_outline_rounded, color: Theme.of(context).colorScheme.error),
            title: const Text('Delete'),
            onTap: () async {
              Navigator.pop(sheetContext);
              final confirmed = await showDialog<bool>(
                context: context,
                builder: (dialogContext) => AlertDialog(
                  title: const Text('Delete this item?'),
                  content: Text('"${item.name}" will be removed from the device.'),
                  actions: [
                    TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Cancel')),
                    FilledButton(
                      key: const Key('media-delete-confirm'),
                      onPressed: () => Navigator.pop(dialogContext, true),
                      child: const Text('Delete'),
                    ),
                  ],
                ),
              );
              if (confirmed == true) await ref.read(mediaListProvider.notifier).delete(item.id);
            },
          ),
        ],
      ),
    ),
  );
}
