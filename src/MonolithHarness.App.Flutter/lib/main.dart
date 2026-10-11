import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:dynamic_color/dynamic_color.dart';
import 'package:provider/provider.dart';

import 'app_state.dart';
import 'ui/home.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  LicenseRegistry.addLicense(() async* {
    yield LicenseEntryWithLineBreaks([
      'Tailwind CSS',
    ], await rootBundle.loadString('assets/web/tailwind-browser.LICENSE.txt'));
    yield LicenseEntryWithLineBreaks([
      'Chart.js',
    ], await rootBundle.loadString('assets/web/chart.LICENSE.txt'));
  });
  await SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  runApp(const Bootstrap());
}

class Bootstrap extends StatefulWidget {
  const Bootstrap({super.key});
  @override
  State<Bootstrap> createState() => _BootstrapState();
}

class _BootstrapState extends State<Bootstrap> {
  late Future<AppState> state;
  @override
  void initState() {
    super.initState();
    state = AppState.open();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<AppState>(
    future: state,
    builder: (c, s) {
      if (s.hasData) {
        return ChangeNotifierProvider.value(
          value: s.data!,
          child: const MonolithApp(),
        );
      }
      return MaterialApp(
        home: Scaffold(
          body: Center(
            child: s.hasError
                ? Padding(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(Icons.error_outline, size: 48),
                        const SizedBox(height: 16),
                        Text('Ouverture impossible : ${s.error}'),
                        const SizedBox(height: 16),
                        FilledButton(
                          onPressed: () =>
                              setState(() => state = AppState.open()),
                          child: const Text('Réessayer'),
                        ),
                      ],
                    ),
                  )
                : const CircularProgressIndicator(),
          ),
        ),
      );
    },
  );
}

class MonolithApp extends StatelessWidget {
  const MonolithApp({super.key});
  @override
  Widget build(BuildContext context) {
    final settings = context.watch<AppState>().settings;
    return DynamicColorBuilder(
      builder: (light, dark) {
        ThemeData theme(Brightness brightness) => ThemeData(
          useMaterial3: true,
          colorScheme: ColorScheme.fromSeed(
            seedColor:
                (brightness == Brightness.light
                    ? light?.primary
                    : dark?.primary) ??
                const Color(0xff32b879),
            brightness: brightness,
          ),
          inputDecorationTheme: const InputDecorationTheme(
            border: OutlineInputBorder(
              borderRadius: BorderRadius.all(Radius.circular(16)),
            ),
          ),
          cardTheme: const CardThemeData(
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.all(Radius.circular(24)),
            ),
          ),
        );
        return MaterialApp(
          title: 'Monolith',
          debugShowCheckedModeBanner: false,
          theme: theme(Brightness.light),
          darkTheme: theme(Brightness.dark),
          themeMode: switch (settings.theme) {
            'light' => ThemeMode.light,
            'dark' => ThemeMode.dark,
            _ => ThemeMode.system,
          },
          locale: const Locale('fr'),
          supportedLocales: const [Locale('fr'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: const HomePage(),
        );
      },
    );
  }
}
