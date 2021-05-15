﻿using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ArcExplorer.ViewModels;
using ArcExplorer.Views;
using Serilog;
using ArcExplorer.Logging;
using ArcExplorer.Tools;
using SmashArcNet;
using System.Threading.Tasks;
using SerilogTimings;
using Avalonia.Controls;

namespace ArcExplorer
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            ApplicationStyles.SetThemeFromSettings();
            ConfigureAndStartLogging();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm,
                    WindowState = Models.ApplicationSettings.Instance.StartMaximized ? WindowState.Maximized : WindowState.Normal
                };

                await UpdateHashesFromGithub(desktop.MainWindow, vm);
            }
            base.OnFrameworkInitializationCompleted();
        }

        private static async Task UpdateHashesFromGithub(Window window, MainWindowViewModel vm)
        {
            // Update the time for the last update check to ensure updates are only checked once per day.
            // This avoids rate limits with the Github API used to check for updates.
            var currentTime = System.DateTime.UtcNow;
            var lastCheckTime = Models.ApplicationSettings.Instance.LastHashesUpdateCheckTime.Date;

            Models.ApplicationSettings.Instance.LastHashesUpdateCheckTime = currentTime;

            if (lastCheckTime.Date >= currentTime.Date)
                return;

            var latestCommit = await HashLabelUpdater.Instance.TryFindNewerHashesCommit();

            if (latestCommit != null)
            {
                var author = latestCommit.Commit.Author.Name;
                var date = latestCommit.Commit.Author.Date.ToString();
                var message = latestCommit.Commit.Message;
                Log.Logger.Information("Found a hashes update. Author: {@author}, Date: {@date}, Message: {@message}", author, date, message);

                var dialog = new HashUpdateDialog(message, author, date);
                dialog.Closed += async (s, e) =>
                {
                    if (!dialog.WasCancelled)
                    {
                        vm.BackgroundTaskStart("Updating hashes");
                        using (var downloadHashes = Operation.Begin("Downloading latest hashes"))
                        {
                            await HashLabelUpdater.Instance.DownloadHashes(HashLabelUpdater.HashesPath);
                            downloadHashes.Complete();
                        }

                        using (var updateHashes = Operation.Begin("Updating hashes"))
                        {
                            if (!HashLabels.TryLoadHashes(HashLabelUpdater.HashesPath))
                            {
                                Log.Logger.Error("Failed to open hashes file {@path}", HashLabelUpdater.HashesPath);
                                updateHashes.Cancel();
                            }

                            Models.ApplicationSettings.Instance.CurrentHashesCommitSha = latestCommit.Sha;
                            updateHashes.Complete();
                        }
                        vm.BackgroundTaskEnd("");
                    }
                };

                await dialog.ShowDialog(window);
            }
        }

        private static void ConfigureAndStartLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(ApplicationDirectory.CreateAbsolutePath("log.txt"))
                .WriteTo.ApplicationLog()
                .CreateLogger();
        }
    }
}
