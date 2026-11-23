/// Turns whatever the user typed into a full device URL.
///
/// Accepts `192.168.1.20`, `ledmatrix.local:5005`, `http://host`, `https://host/path/`. A missing scheme becomes
/// `http://` and a missing port on an http address becomes 5005. Returns either a normalised [url] or an [error].
const defaultDevicePort = 5005;

({String? url, String? error}) normalizeDeviceUrl(String input) {
  var text = input.trim();
  if (text.isEmpty) return (url: null, error: 'Enter the address of your LED matrix');
  if (text.contains(RegExp(r'\s'))) return (url: null, error: 'The address cannot contain spaces');
  if (!text.contains('://')) text = 'http://$text';
  final Uri uri;
  try {
    uri = Uri.parse(text);
  } on FormatException {
    return (url: null, error: 'That does not look like a valid address');
  }
  final scheme = uri.scheme.toLowerCase();
  if (scheme != 'http' && scheme != 'https') return (url: null, error: 'Only http:// and https:// addresses are supported');
  final host = uri.host;
  if (host.isEmpty) return (url: null, error: 'Enter a host name or IP address');
  if (!_validHost(host)) return (url: null, error: 'That host name is not valid');
  // Uri drops a scheme's default port (http :80), so read an explicit port from the text itself.
  final typed = RegExp(r'^[A-Za-z]+://[^/?#]*:(\d+)(?:[/?#]|$)').firstMatch(text)?.group(1);
  final typedPort = typed == null ? null : int.tryParse(typed);
  if (typed != null && (typedPort == null || typedPort < 1 || typedPort > 65535)) {
    return (url: null, error: 'The port must be between 1 and 65535');
  }
  final port = typedPort ?? (scheme == 'http' ? defaultDevicePort : null);
  var path = uri.path;
  while (path.endsWith('/')) {
    path = path.substring(0, path.length - 1);
  }
  final shownHost = host.contains(':') ? '[$host]' : host;
  return (url: '$scheme://$shownHost${port == null ? '' : ':$port'}$path', error: null);
}

bool _validHost(String host) {
  if (host.contains(':')) return true; // IPv6 literal, already parsed by Uri.
  return RegExp(r'^[A-Za-z0-9]([A-Za-z0-9_.-]*[A-Za-z0-9])?$').hasMatch(host);
}
