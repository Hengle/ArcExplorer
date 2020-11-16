﻿using ArcExplorer.Views;
using Avalonia.Collections;
using ReactiveUI;
using SmashArcNet;
using System;
using System.IO;
using System.Linq;

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

        public void OpenArc(string path)
        {
            // TODO: This is expensive and should be handled separately.
            HashLabels.Initialize("Hashes.txt");

            if (!ArcFile.TryOpenArc(path, out ArcFile? arcFile))
            {
                Serilog.Log.Logger.Information("Failed to open ARC file {@path}", path);
                return;
            }

            FileCount = arcFile.FileCount.ToString();
            ArcPath = path;

            PopulateFileTree(arcFile);
        }

        private void PopulateFileTree(ArcFile arcFile)
        {
            foreach (var node in arcFile.GetRootNodes())
            {
                var treeNode = LoadNodeAddToParent(arcFile, null, node);
                Files.Add(treeNode);
            }
        }

        private static FileNodeBase LoadNodeAddToParent(ArcFile arcFile, FileNodeBase? parent, ArcFileTreeNode arcNode)
        {
            switch (arcNode.Type)
            {
                case ArcFileTreeNode.FileType.Directory:
                    var folder = CreateFolderLoadChildren(arcFile, arcNode);
                    parent?.Children.Add(folder);
                    return folder;
                case ArcFileTreeNode.FileType.File:
                    var file = CreateFileNode(arcFile, arcNode);
                    parent?.Children.Add(file);
                    return file;
                default:
                    throw new NotImplementedException($"Unable to create node from {arcNode.Type}");
            }
        }

        private static FileNode CreateFileNode(ArcFile arcFile, ArcFileTreeNode arcNode)
        {
            // Assume no children for file nodes.
            var fileNode = new FileNode(Path.GetFileName(arcNode.Path), arcNode.IsShared, arcNode.IsRegional, arcNode.Offset, arcNode.CompSize, arcNode.DecompSize);
            fileNode.FileExtracting += (s, e) => ExtractFile(arcFile, arcNode);

            return fileNode;
        }

        private static void ExtractFile(ArcFile arcFile, ArcFileTreeNode arcNode)
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
                Serilog.Log.Logger.Information("Failed to extract to {@path}", exportPath);
        }

        private static FolderNode CreateFolderLoadChildren(ArcFile arcFile, ArcFileTreeNode arcNode)
        {
            // Use DirectoryInfo to account for trailing slashes.
            var folder = CreateFolderNode(arcNode);

            foreach (var child in arcFile.GetChildren(arcNode))
            {
                FileNodeBase childNode = child.Type switch
                {
                    ArcFileTreeNode.FileType.Directory => CreateFolderNode(child),
                    ArcFileTreeNode.FileType.File => CreateFileNode(arcFile, child),
                    _ => throw new NotImplementedException($"Unsupported type {child.Type}")
                };

                // When the parent is expanded, load the grandchildren to support expanding the children.
                folder.Expanded += (s, e) => LoadChildrenAddToParent(arcFile, child, childNode);

                folder.Children.Add(childNode);
            }

            return folder;
        }

        private static FolderNode CreateFolderNode(ArcFileTreeNode arcNode)
        {
            return new FolderNode(new DirectoryInfo(arcNode.Path).Name, false, false);
        }

        private static void LoadChildrenAddToParent(ArcFile arcFile, ArcFileTreeNode arcNode, FileNodeBase parent)
        {
            foreach (var child in arcFile.GetChildren(arcNode))
            {
                LoadNodeAddToParent(arcFile, parent, child);
            }
        }

        public void RebuildFileTree()
        {
            // TODO: Update icons without rebuilding the tree?
            //Files.Clear();
            //PopulateFileTree();
        }
    }
}
