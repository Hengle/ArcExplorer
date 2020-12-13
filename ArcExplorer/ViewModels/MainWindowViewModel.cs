﻿using Avalonia.Collections;
using ReactiveUI;
using SmashArcNet;
using SmashArcNet.Nodes;
using System;
using System.IO;
using System.Linq;
using SerilogTimings;
using System.Threading.Tasks;
using ArcExplorer.Logging;

namespace ArcExplorer.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public AvaloniaList<FileNodeBase> Files { get; } = new AvaloniaList<FileNodeBase>();

        public FileNodeBase? SelectedFile 
        { 
            get => selectedFile;
            set => this.RaiseAndSetIfChanged(ref selectedFile, value);
        }
        private FileNodeBase? selectedFile;

        public string ArcVersion
        {
            get => arcVersion;
            set => this.RaiseAndSetIfChanged(ref arcVersion, value);
        }
        private string arcVersion = "";

        public string FileCount
        {
            get => fileCount;
            set => this.RaiseAndSetIfChanged(ref fileCount, value);
        }
        private string fileCount = "";

        public string ArcPath
        {
            get => arcPath;
            set => this.RaiseAndSetIfChanged(ref arcPath, value);
        }
        private string arcPath = "";

        public bool IsLoading
        {
            get => isLoading;
            set => this.RaiseAndSetIfChanged(ref isLoading, value);
        }
        private bool isLoading = false;

        public string LoadingDescription
        {
            get => loadingDescription;
            set => this.RaiseAndSetIfChanged(ref loadingDescription, value);
        }
        private string loadingDescription = "";

        public bool HasErrors
        {
            get => hasErrors;
            set => this.RaiseAndSetIfChanged(ref hasErrors, value);
        }
        private bool hasErrors = false;

        public string ErrorDescription
        {
            get => errorDescription;
            set => this.RaiseAndSetIfChanged(ref errorDescription, value);
        }
        private string errorDescription = "";

        private ArcFile? arcFile;

        public MainWindowViewModel()
        {
            ApplicationSink.Instance.Value.LogEventHandled += LogEventHandled;
        }

        private void LogEventHandled(object? sender, EventArgs e)
        {
            HasErrors = ApplicationSink.Instance.Value.ErrorCount > 0;
            ErrorDescription = $"{ApplicationSink.Instance.Value.ErrorCount} Errors";
        }

        public void OpenArc(string path)
        {
            // TODO: This is expensive and should be handled separately.
            var hashesFile = "Hashes.txt";
            if (!HashLabels.TryLoadHashes(hashesFile))
            {
                Serilog.Log.Logger.Error("Failed to open Hashes file {@path}", hashesFile);
                return;
            }

            if (!ArcFile.TryOpenArc(path, out arcFile))
            {
                Serilog.Log.Logger.Error("Failed to open ARC file {@path}", path);
                return;
            }

            FileCount = arcFile.FileCount.ToString();
            ArcPath = path;

            PopulateFileTree(arcFile);
        }

        private void PopulateFileTree(ArcFile arcFile)
        {
            // Replace existing files with the new ARC.
            // TODO: Is memory being correctly freed?
            Files.Clear();
            foreach (var node in arcFile.GetRootNodes())
            {
                var treeNode = LoadNodeAddToParent(arcFile, null, node);
                Files.Add(treeNode);
            }
        }

        private FileNodeBase LoadNodeAddToParent(ArcFile arcFile, FileNodeBase? parent, IArcNode arcNode)
        {
            switch (arcNode)
            {
                case ArcDirectoryNode directoryNode:
                    var folder = CreateFolderLoadChildren(arcFile, directoryNode);
                    parent?.Children.Add(folder);
                    return folder;
                case ArcFileNode fileNode:
                    var file = CreateFileNode(arcFile, fileNode);
                    parent?.Children.Add(file);
                    return file;
                default:
                    throw new NotImplementedException($"Unable to create node from {arcNode}");
            }
        }

        public void ErrorClick()
        {
            var window = new Views.LogWindow() { Items = ApplicationSink.Instance.Value.LogMessages };
            window.Show();
        }

        private FileNode CreateFileNode(ArcFile arcFile, ArcFileNode arcNode)
        {
            // Assume no children for file nodes.
            var fileNode = new FileNode(Path.GetFileName(arcNode.Path), arcNode.IsShared, arcNode.IsRegional, arcNode.Offset, arcNode.CompSize, arcNode.DecompSize);
            fileNode.FileExtracting += (s, e) => ExtractFileAsync(arcFile, arcNode);

            return fileNode;
        }

        private async void ExtractFileAsync(ArcFile arcFile, ArcFileNode arcNode)
        {
            await RunBackgroundTask($"Extracting {arcNode.Path}", () => ExtractFile(arcFile, arcNode));
        }

        private async Task RunBackgroundTask(string taskDescription, Action taskToRun)
        {
            // TODO: Correctly update the description for multiple background tasks.
            // The code currently only shows a description for the most recent task.
            IsLoading = true;
            LoadingDescription = taskDescription;

            await Task.Run(() =>
            {
                using (Operation.Time(taskDescription))
                {
                    taskToRun();
                }
            });

            LoadingDescription = "";
            IsLoading = false;
        }

        private static void ExtractFile(ArcFile arcFile, ArcFileNode arcNode)
        {
            // TODO: Combine the paths with the export directory specified in preferences.
            // TODO: Will this always produce a correct path?
            var currentDirectory = Directory.GetCurrentDirectory();
            var paths = new string[] { currentDirectory, "export" };
            var exportPath = Path.Combine(paths.Concat(arcNode.Path.Split('/')).ToArray());

            // Extraction will fail if the directory doesn't exist.
            var exportFileDirectory = Path.GetDirectoryName(exportPath);
            if (!Directory.Exists(exportFileDirectory))
                Directory.CreateDirectory(exportFileDirectory);

            // Extraction may fail.
            // TODO: Update the C# bindings to store more detailed error info?
            if (!arcFile.TryExtractFile(arcNode, exportPath))
                Serilog.Log.Logger.Error("Failed to extract to {@path}", exportPath);
        }

        private FolderNode CreateFolderLoadChildren(ArcFile arcFile, ArcDirectoryNode arcNode)
        {
            // Use DirectoryInfo to account for trailing slashes.
            var folder = CreateFolderNode(arcFile, arcNode);

            foreach (var child in arcFile.GetChildren(arcNode))
            {
                FileNodeBase childNode = child switch
                {
                    ArcDirectoryNode directory => CreateFolderNode(arcFile, directory),
                    ArcFileNode file => CreateFileNode(arcFile, file),
                    _ => throw new NotImplementedException($"Unable to create node from {child}")
                };

                // When the parent is expanded, load the grandchildren to support expanding the children.
                folder.Expanded += (s, e) => LoadChildrenAddToParent(arcFile, child, childNode);

                folder.Children.Add(childNode);
            }

            return folder;
        }

        private FolderNode CreateFolderNode(ArcFile arcFile, ArcDirectoryNode arcNode)
        {
            var folder = new FolderNode(new DirectoryInfo(arcNode.Path).Name, false, false);
            folder.FileExtracting += (s, e) => ExtractFolderAsync(arcFile, arcNode);
            return folder;
        }

        private async void ExtractFolderAsync(ArcFile arcFile, ArcDirectoryNode arcNode)
        {
            await RunBackgroundTask($"Extracting files from {arcNode.Path}", () => ExtractFilesRecursive(arcFile, arcNode));
        }

        private static void ExtractFilesRecursive(ArcFile arcFile, ArcDirectoryNode arcNode)
        {
            foreach (var child in arcFile.GetChildren(arcNode))
            {
                // Assume files have no children, so only recurse for directories.
                switch (child)
                {
                    case ArcFileNode file:
                        ExtractFile(arcFile, file);
                        break;
                    case ArcDirectoryNode directory:
                        ExtractFilesRecursive(arcFile, directory);
                       break;
                    default:
                        break;
                }
            }
        }

        private void LoadChildrenAddToParent(ArcFile arcFile, IArcNode arcNode, FileNodeBase parent)
        {
            if (arcNode is ArcDirectoryNode directoryNode)
            {
                foreach (var child in arcFile.GetChildren(directoryNode))
                {
                    LoadNodeAddToParent(arcFile, parent, child);
                }
            }
        }

        public void RebuildFileTree()
        {
            // TODO: Preserve the existing directory structure.
            if (arcFile != null)
            {
                // Clear everything to ensure the proper icons get loaded when changing themes.
                SelectedFile = null;
                Files.Clear();
                PopulateFileTree(arcFile);
            }
        }
    }
}
