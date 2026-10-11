import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:webview_flutter/webview_flutter.dart';
import 'package:webview_flutter_android/webview_flutter_android.dart';

import '../domain/models.dart';
import '../services/reply_parts.dart';

class WebPreview extends StatefulWidget {
  final String html;
  final AppSettings settings;
  final bool dark;
  const WebPreview({
    super.key,
    required this.html,
    required this.settings,
    required this.dark,
  });
  @override
  State<WebPreview> createState() => _WebPreviewState();
}

class _WebPreviewState extends State<WebPreview> {
  WebViewController? controller;
  String? error;
  bool loading = true;
  @override
  void initState() {
    super.initState();
    start();
  }

  Future<void> start() async {
    try {
      final document = sandboxDocument(
        widget.html,
        dark: widget.dark,
        tailwind: widget.settings.tailwind,
        charts: widget.settings.chartJs,
        tailwindScript: widget.settings.tailwind
            ? await rootBundle.loadString('assets/web/tailwind-browser.js')
            : '',
        chartScript: widget.settings.chartJs
            ? await rootBundle.loadString('assets/web/chart.umd.js')
            : '',
      );
      final view = WebViewController(onPermissionRequest: (r) => r.deny());
      await view.setJavaScriptMode(JavaScriptMode.unrestricted);
      await view.setBackgroundColor(
        widget.dark ? const Color(0xff111713) : const Color(0xfff7fbf7),
      );
      if (view.platform is AndroidWebViewController) {
        if (kDebugMode) await AndroidWebViewController.enableDebugging(true);
        final android = view.platform as AndroidWebViewController;
        await android.setAllowFileAccess(false);
        await android.setAllowContentAccess(false);
        await android.setGeolocationEnabled(false);
      }
      var injected = false;
      await view.setNavigationDelegate(
        NavigationDelegate(
          onNavigationRequest: (r) => r.url == 'about:blank'
              ? NavigationDecision.navigate
              : NavigationDecision.prevent,
          onPageFinished: (_) async {
            if (injected) return;
            injected = true;
            try {
              await view.runJavaScript(
                'document.getElementById("preview").srcdoc=${jsonEncode(document)};',
              );
              if (mounted) setState(() => loading = false);
            } catch (ex) {
              if (mounted) setState(() => error = 'Aperçu indisponible : $ex');
            }
          },
          onWebResourceError: (e) {
            if (e.isForMainFrame == true && mounted) {
              setState(() => error = e.description);
            }
          },
        ),
      );
      final color = widget.dark ? '#111713' : '#f7fbf7';
      await view.loadHtmlString(
        '''<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; frame-src about:; style-src 'unsafe-inline'; script-src 'unsafe-inline' 'unsafe-eval'; img-src data:; font-src data:; connect-src 'none'; form-action 'none'; base-uri 'none'"><style>html,body{margin:0;width:100%;height:100%;background:$color}iframe{border:0;width:100%;height:100%;display:block;background:$color}</style></head><body><iframe id="preview" sandbox="allow-scripts" referrerpolicy="no-referrer"></iframe></body></html>''',
      );
      if (mounted) setState(() => controller = view);
    } catch (ex) {
      if (mounted) setState(() => error = 'Aperçu indisponible : $ex');
    }
  }

  Widget nativeView(WebViewController view) =>
      WebViewWidget.fromPlatformCreationParams(
        params: view.platform is AndroidWebViewController
            ? AndroidWebViewWidgetCreationParams(
                controller: view.platform,
                displayWithHybridComposition: true,
              )
            : PlatformWebViewWidgetCreationParams(controller: view.platform),
      );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Application web'),
      actions: [
        IconButton(
          tooltip: 'Réinitialiser',
          onPressed: () {
            setState(() {
              loading = true;
              error = null;
              controller = null;
            });
            start();
          },
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: SafeArea(
      child: error != null
          ? Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: SelectableText(error!),
              ),
            )
          : Stack(
              children: [
                if (controller != null) nativeView(controller!),
                if (loading) const Center(child: CircularProgressIndicator()),
              ],
            ),
    ),
  );
}
