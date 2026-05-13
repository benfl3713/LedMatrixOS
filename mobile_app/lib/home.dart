import 'package:flutter/material.dart';
import 'package:ledmatrix/home_viewmodel.dart';
import 'package:ledmatrix/services/api.dart';
import 'package:provider/provider.dart';

class Home extends StatelessWidget {
  const Home({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text('Led Matrix'),
        centerTitle: true,
        actions: [
          IconButton(onPressed: () {}, icon: Icon(Icons.settings_outlined)),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: Provider.of<HomeViewModel>(context, listen: false).reload,
        child: Consumer<HomeViewModel>(
          builder: (context, model, _) {
            if (model.error != null) {
              return Expanded(
                child: Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    spacing: 20,
                    children: [
                      Text(
                        model.error!,
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                      FilledButton(
                        onPressed: Provider.of<HomeViewModel>(
                          context,
                          listen: false,
                        ).reload,
                        child: Text('Retry'),
                      ),
                    ],
                  ),
                ),
              );
            }

            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                MatrixLivePreview(),
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16.0,
                    vertical: 12.0,
                  ),
                  child: FilledButton(
                    onPressed: () {},
                    style: ButtonStyle(
                      backgroundColor: WidgetStatePropertyAll(
                        Theme.of(context).colorScheme.secondary,
                      ),
                    ),
                    child: Padding(
                      padding: const EdgeInsets.all(10.0),
                      child: Icon(Icons.power_settings_new, size: 30),
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.all(8.0),
                  child: Text('Apps', style: Theme.of(context).textTheme.titleMedium),
                ),
                Expanded(
                  child: Builder(
                    builder: (context) {
                      if (model.loadingApps) {
                        return Center(child: CircularProgressIndicator());
                      }
                      return GridView.builder(
                        itemCount: model.apps.length,
                        gridDelegate: SliverGridDelegateWithMaxCrossAxisExtent(
                          maxCrossAxisExtent: 160,
                        ),
                        itemBuilder: (_, index) =>
                            AppGridCard(app: model.apps[index]),
                      );
                    },
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class MatrixLivePreview extends StatelessWidget {
  const MatrixLivePreview({
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(20.0),
      child: AspectRatio(
        aspectRatio: 4,
        child: Container(
          decoration: BoxDecoration(
            color: Colors.black,
            boxShadow: [
              BoxShadow(
                color: Colors.black26,
                blurRadius: 2,
                offset: Offset(2, 2)
              )
            ]
          ),
          child: Center(
            child: Text(
              'Hello World',
              style: TextStyle(color: Colors.white),
            ),
          ),
        ),
      ),
    );
  }
}

class AppGridCard extends StatelessWidget {
  const AppGridCard({super.key, required this.app});

  final MatrixApp app;

  @override
  Widget build(BuildContext context) {
    void openAppBottomSheet() {
      showModalBottomSheet(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (context) {
          return AppSettingsSheet(app: app);
        },
      );
    }

    return Card.outlined(
      child: InkWell(
        onTap: openAppBottomSheet,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.train,
              size: 50,
              color: Theme.of(context).colorScheme.tertiary,
            ),
            Text(app.name),
          ],
        ),
      ),
    );
  }
}

class AppSettingsSheet extends StatelessWidget {
  const AppSettingsSheet({super.key, required this.app});

  final MatrixApp app;

  @override
  Widget build(BuildContext context) {
    return DraggableScrollableSheet(
      maxChildSize: 0.67,
      expand: false,
      builder: (context, scrollController) {
        return SizedBox(
          width: double.infinity,
          child: Padding(
            padding: const EdgeInsets.all(8.0),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  app.name,
                  style: Theme.of(context).textTheme.headlineMedium,
                ),
              ],
            ),
          ),
        );
      },
    );
  }
}
