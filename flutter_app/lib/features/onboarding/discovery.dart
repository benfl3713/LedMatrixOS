import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http/http.dart' as http;

import '../../api/models.dart';
import '../../core/device_url.dart';
import '../../core/providers.dart';

/// A display found on the network.
class FoundDevice {
  const FoundDevice({required this.url, required this.health});

  final String url;
  final Health health;

  String get host => Uri.parse(url).host;
  String get resolution => health.width > 0 && health.height > 0 ? '${health.width}x${health.height}' : 'LED matrix';
}

/// Asks `GET {baseUrl}/api/health` and returns the reading, or null when nothing (or not a LedMatrixOS) answers.
typedef HealthProbe = Future<Health?> Function(String baseUrl, Duration timeout);

/// Starts a network scan; the stream emits devices as they are found and closes when the scan is done.
/// Cancelling the subscription stops the scan.
typedef DeviceScanner = Stream<FoundDevice> Function();

/// The scanner the onboarding screen uses. Tests override this with a canned stream.
final deviceScannerProvider = Provider<DeviceScanner>((ref) => () => scanForDevices());

Future<Health?> httpHealthProbe(String baseUrl, Duration timeout) async {
  final client = http.Client();
  try {
    final response = await client.get(Uri.parse('$baseUrl/api/health')).timeout(timeout);
    if (response.statusCode != 200) return null;
    final json = jsonDecode(response.body);
    if (json is! Map<String, dynamic> || json['status'] is! String) return null;
    return Health.fromJson(json);
  } catch (_) {
    return null;
  } finally {
    client.close();
  }
}

/// The phone's own IPv4 addresses on real networks (no loopback or link-local).
Future<List<String>> localIPv4Addresses() async {
  if (kIsWeb) return const [];
  try {
    final interfaces = await NetworkInterface.list(type: InternetAddressType.IPv4, includeLoopback: false);
    return [
      for (final i in interfaces)
        for (final a in i.addresses)
          if (!a.isLoopback && !a.isLinkLocal) a.address,
    ];
  } catch (_) {
    return const [];
  }
}

/// The /24 neighbours of [address] (`192.168.1.20` -> `192.168.1.1` ... `.254`, without itself).
List<String> subnetHosts(String address) {
  final parts = address.split('.');
  if (parts.length != 4) return const [];
  final prefix = parts.take(3).join('.');
  return [for (var i = 1; i < 255; i++) if ('$prefix.$i' != address) '$prefix.$i'];
}

/// Looks for displays: first the well-known names (`ledmatrix.local` and the configured default), then every host on
/// the phone's /24 subnet, port 5005, with at most [concurrency] requests in flight. On web only the well-known
/// names are tried (browsers cannot scan).
Stream<FoundDevice> scanForDevices({
  HealthProbe probe = httpHealthProbe,
  Future<List<String>> Function() localAddresses = localIPv4Addresses,
  List<String> priority = const ['http://ledmatrix.local:$defaultDevicePort', defaultApiUrl],
  int concurrency = 32,
  Duration priorityTimeout = const Duration(seconds: 2),
  Duration scanTimeout = const Duration(milliseconds: 700),
}) {
  late StreamController<FoundDevice> controller;
  var cancelled = false;
  final seen = <String>{};

  Future<void> check(String url, Duration timeout) async {
    if (cancelled) return;
    final health = await probe(url, timeout);
    if (health == null || cancelled || controller.isClosed) return;
    final host = Uri.parse(url).host;
    if (!seen.add(host)) return;
    controller.add(FoundDevice(url: url, health: health));
  }

  Future<void> run() async {
    await Future.wait([for (final url in priority) check(url, priorityTimeout)]);
    if (cancelled || kIsWeb) return;
    final hosts = <String>{};
    for (final address in await localAddresses()) {
      hosts.addAll(subnetHosts(address));
    }
    final queue = hosts.map((h) => 'http://$h:$defaultDevicePort').toList();
    var next = 0;
    Future<void> worker() async {
      while (!cancelled && next < queue.length) {
        await check(queue[next++], scanTimeout);
      }
    }

    await Future.wait([for (var i = 0; i < concurrency; i++) worker()]);
  }

  controller = StreamController<FoundDevice>(
    onListen: () async {
      try {
        await run();
      } catch (_) {
        // A scan that breaks simply ends; the manual path stays available.
      }
      if (!controller.isClosed) await controller.close();
    },
    onCancel: () => cancelled = true,
  );
  return controller.stream;
}
