import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:ledmatrix/matrix_view_model.dart';
import 'package:ledmatrix/widgets/app_card.dart';

void main() {
  runApp(MainApp());
}

class MainApp extends StatelessWidget {
  final MatrixViewModel _viewModel = MatrixViewModel();
  MainApp({super.key}) {
    _viewModel.initialize();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      theme: ThemeData(
        colorScheme: ColorScheme.light(primary: Colors.orange),
        //textTheme: GoogleFonts.latoTextTheme(),
      ),
      home: ListenableBuilder(
        listenable: _viewModel,
        builder: (context, child) {
          return Scaffold(
            appBar: AppBar(
              title: Text('Led Matrix'),
              centerTitle: false,
              scrolledUnderElevation: 0,
              actions: [Switch(value: _viewModel.matrixSettings?.isEnabled ?? false, onChanged: (value) {
                _viewModel.setIsEnabled(value);
              })],
            ),
            body: Column(
              children: [
                Padding(
                  padding: const EdgeInsets.all(10.0),
                  child: ClipRRect(
                    borderRadius: BorderRadiusGeometry.circular(6.0),
                    child: AspectRatio(
                      aspectRatio: 4,
                      child: Container(
                        color: Colors.black,
                        width: double.infinity,
                        child: Center(
                          child: Text(
                            "Loading...",
                            style: TextStyle(color: Colors.orange, fontSize: 26),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                Slider.adaptive(value: (_viewModel.matrixSettings?.brightness.toDouble() ?? 50) / 100, onChanged: (v) {
                  _viewModel.setBrightness((v * 100).toInt());
                }),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 12.0),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Text(
                              'Apps',
                              textAlign: TextAlign.start,
                              style: Theme.of(context).textTheme.titleLarge,
                            ),
                            Chip(
                              label: Text(_viewModel.activeApp?.name ?? 'None'),
                              color: WidgetStatePropertyAll(Colors.green.shade50),
                              avatar: Icon(Icons.play_arrow_rounded),
                              visualDensity: VisualDensity.compact,
                            ),
                          ],
                        ),
                      ),
                      Expanded(
                        child: GridView.builder(
                          itemCount: _viewModel.installedApps?.length ?? 0,
                          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                            crossAxisCount: 2,
                            mainAxisExtent: 130,
                            mainAxisSpacing: 6.0,
                            crossAxisSpacing: 6.0,
                          ),
                          itemBuilder: (_, index) => AppCard(app: _viewModel.installedApps?[index], viewModel: _viewModel),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          );
        }
      ),
    );
  }
}
