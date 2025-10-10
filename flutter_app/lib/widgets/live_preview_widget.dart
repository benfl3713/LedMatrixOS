import 'package:flutter/material.dart';
import '../api_service.dart';

class LivePreviewWidget extends StatelessWidget {
  final LedMatrixApi api;
  final String previewImageKey;

  const LivePreviewWidget({
    super.key,
    required this.api,
    required this.previewImageKey,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      clipBehavior: Clip.hardEdge,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Text(
              'Live Preview',
              style: Theme.of(context).textTheme.titleLarge,
            ),
          ),
          SizedBox(
            width: MediaQuery.of(context).size.width,
            child: Container(
              constraints: const BoxConstraints(
                maxHeight: 200,
              ),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: Image.network(
                  '${api.getPreviewUrl()}&key=$previewImageKey',
                  gaplessPlayback: true,
                  fit: BoxFit.contain,
                  filterQuality: FilterQuality.none,
                  errorBuilder: (context, error, stackTrace) {
                    return const Center(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(Icons.image_not_supported, size: 48),
                          SizedBox(height: 8),
                          Text('Preview not available'),
                          Text('(Simulator mode only)'),
                        ],
                      ),
                    );
                  },
                  loadingBuilder: (context, child, loadingProgress) {
                    if (loadingProgress == null) return child;
                    return const Center(child: CircularProgressIndicator());
                  },
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
