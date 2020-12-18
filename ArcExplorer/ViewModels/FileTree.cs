﻿using Avalonia.Collections;
using SerilogTimings;
using SmashArcNet;
using SmashArcNet.Nodes;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ArcExplorer.ViewModels
{
    internal static class FileTree
    {
        /// <summary>
        /// Clears the existing items in <paramref name="files"/> and populates the base level from <paramref name="arcFile"/>.
        /// Directories are lazy loaded and will load their children after being expanded.
        /// The parameter to <paramref name="extractStartCallBack"/> is the task description.
        /// </summary>
        /// <param name="arcFile">the ARC to load</param>
        /// <param name="files">the file list to be cleared and updated</param>
        /// <param name="extractStartCallBack">called before starting an extract operation</param>
        /// <param name="extractEndCallBack">called after starting an extract operation</param>
        public static void PopulateFileTree(ArcFile arcFile, AvaloniaList<FileNodeBase> files, Action<string> extractStartCallBack, Action extractEndCallBack)
        {
            // Replace existing files with the new ARC.
            // TODO: Is memory being correctly freed?
            files.Clear();
            foreach (var node in arcFile.GetRootNodes())
            {
                var treeNode = LoadNodeAddToParent(arcFile, null, node, extractStartCallBack, extractEndCallBack);
                files.Add(treeNode);
            }
        }

        private static FileNodeBase LoadNodeAddToParent(ArcFile arcFile, FileNodeBase? parent, IArcNode arcNode, Action<string> taskStart, Action taskEnd)
        {
            switch (arcNode)
            {
                case ArcDirectoryNode directoryNode:
                    var folder = CreateFolderLoadChildren(arcFile, directoryNode, taskStart, taskEnd);
                    parent?.Children.Add(folder);
                    return folder;
                case ArcFileNode fileNode:
                    var file = CreateFileNode(arcFile, fileNode, taskStart, taskEnd);
                    parent?.Children.Add(file);
                    return file;
                default:
                    throw new NotImplementedException($"Unable to create node from {arcNode}");
            }
        }

        private static FileNode CreateFileNode(ArcFile arcFile, ArcFileNode arcNode, Action<string> taskStart, Action taskEnd)
        {
            // Assume no children for file nodes.
            var fileNode = new FileNode(Path.GetFileName(arcNode.Path), arcNode.IsShared, arcNode.IsRegional, arcNode.Offset, arcNode.CompSize, arcNode.DecompSize);
            fileNode.FileExtracting += (s, e) => ExtractFileAsync(arcFile, arcNode, taskStart, taskEnd);

            return fileNode;
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

        private static FolderNode CreateFolderLoadChildren(ArcFile arcFile, ArcDirectoryNode arcNode, Action<string> taskStart, Action taskEnd)
        {
            // Use DirectoryInfo to account for trailing slashes.
            var folder = CreateFolderNode(arcFile, arcNode, taskStart, taskEnd);

            foreach (var child in arcFile.GetChildren(arcNode))
            {
                FileNodeBase childNode = child switch
                {
                    ArcDirectoryNode directory => CreateFolderNode(arcFile, directory, taskStart, taskEnd),
                    ArcFileNode file => CreateFileNode(arcFile, file, taskStart, taskEnd),
                    _ => throw new NotImplementedException($"Unable to create node from {child}")
                };

                // When the parent is expanded, load the grandchildren to support expanding the children.
                folder.Expanded += (s, e) => LoadChildrenAddToParent(arcFile, child, childNode, taskStart, taskEnd);

                folder.Children.Add(childNode);
            }

            return folder;
        }

        private static async Task RunBackgroundTask(string taskDescription, Action taskToRun, Action<string> taskStart, Action taskEnd)
        {

            taskStart(taskDescription);

            await Task.Run(() =>
            {
                using (Operation.Time(taskDescription))
                {
                    taskToRun();
                }
            });

            taskEnd();
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

        private static void LoadChildrenAddToParent(ArcFile arcFile, IArcNode arcNode, FileNodeBase parent, Action<string> taskStart, Action taskEnd)
        {
            if (arcNode is ArcDirectoryNode directoryNode)
            {
                foreach (var child in arcFile.GetChildren(directoryNode))
                {
                    LoadNodeAddToParent(arcFile, parent, child, taskStart, taskEnd);
                }
            }
        }

        private static FolderNode CreateFolderNode(ArcFile arcFile, ArcDirectoryNode arcNode, Action<string> taskStart, Action taskEnd)
        {
            var folder = new FolderNode(new DirectoryInfo(arcNode.Path).Name, false, false);
            folder.FileExtracting += (s, e) => ExtractFolderAsync(arcFile, arcNode, taskStart, taskEnd);
            return folder;
        }

        private static async void ExtractFileAsync(ArcFile arcFile, ArcFileNode arcNode, Action<string> taskStart, Action taskEnd)
        {
            // TODO: Files extract quickly, so there's no need to update the UI by calling taskStart.
            await RunBackgroundTask($"Extracting {arcNode.Path}", () => ExtractFile(arcFile, arcNode), taskStart, taskEnd);
        }

        private static async void ExtractFolderAsync(ArcFile arcFile, ArcDirectoryNode arcNode, Action<string> taskStart, Action taskEnd)
        {
            await RunBackgroundTask($"Extracting files from {arcNode.Path}", () => ExtractFilesRecursive(arcFile, arcNode), taskStart, taskEnd);
        }

    }
}
