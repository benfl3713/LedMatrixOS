import 'package:flutter/material.dart';
import 'package:flex_color_scheme/flex_color_scheme.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:ledmatrix/home.dart';
import 'package:ledmatrix/home_viewmodel.dart';
import 'package:provider/provider.dart';

void main() {
  runApp(const MainApp());
}

class MainApp extends StatelessWidget {
  const MainApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) => HomeViewModel(),
      child: MaterialApp(
        title: 'Led Matrix',
        theme: FlexThemeData.light(
          scheme: FlexScheme.bigStone,
          textTheme: GoogleFonts.pressStart2pTextTheme(),
        ), //bigStone
        darkTheme: FlexThemeData.dark(
          scheme: FlexScheme.shark,
          textTheme: GoogleFonts.radioCanadaTextTheme(),
        ),
        themeMode: ThemeMode.system,
        debugShowCheckedModeBanner: false,
        home: Home(),
      ),
    );
  }
}
