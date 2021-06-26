﻿using ArcExplorer.Models;
using Avalonia.Collections;
using SerilogTimings;
using SmashArcNet;
using SmashArcNet.Nodes;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ArcExplorer.ViewModels
{
    // TODO: Move this to the ArcExplorer.ArcUtils namespace?
    internal static class FileTree
    {
        private class ExtractResult
        {
            public bool Success { get; set; }
            public string CompletionSummary { get; set; } = "";
        }

        /// <summary>
        /// Loads the base level from <paramref name="arcFile"/>.
        /// The parameter to <paramref name="extractStartCallBack"/> is the task description.
        /// </summary>
        /// <param name="arcFile">the ARC to load</param>
        /// <param name="extractStartCallBack">called before starting an extract operation</param>
        /// <param name="extractReportProgressCallBack">contains the file path and progress percentage on extracting multiple files</param>
        /// <param name="extractEndCallBack">called after starting an extract operation with a progress message</param>
        /// <returns>The nodes for the root level</returns>
        public static List<FileNodeBase> CreateRootLevelNodes(ArcFile arcFile,
            Action<string> extractStartCallBack,
            Action<string, double> extractReportProgressCallBack,
            Action<string> extractEndCallBack)
        {
            var files = new List<FileNodeBase>();
            foreach (var node in arcFile.GetRootNodes(ApplicationSettings.Instance.ArcRegion))
            {
                // There is no parent for root nodes, so leave the parent as null.
                var treeNode = CreateNode(arcFile, null, node, extractStartCallBack, extractReportProgressCallBack, extractEndCallBack);
                files.Add(treeNode);
            }
            return files;
        }

        /// <summary>
        /// Loads the child nodes of <paramref name="parent"/> from <paramref name="arcFile"/>.
        /// The parameter to <paramref name="extractStartCallBack"/> is the task description.
        /// </summary>
        /// <param name="arcFile">the ARC to load</param>
        /// <param name="parent">the folder whose children will be loaded</param>
        /// <param name="extractStartCallBack">called before starting an extract operation</param>
        /// <param name="extractReportProgressCallBack">contains the file path and progress percentage on extracting multiple files</param>
        /// <param name="extractEndCallBack">called after starting an extract operation with a progress message</param>
        public static List<FileNodeBase> CreateChildNodes(ArcFile arcFile, FolderNode parent,
            Action<string> extractStartCallBack,
            Action<string, double> extractReportProgressCallBack,
            Action<string> extractEndCallBack)
        {
            return CreateChildNodes(arcFile, parent.AbsolutePath, extractStartCallBack, extractReportProgressCallBack, extractEndCallBack);
        }

        /// <summary>
        /// Loads the child nodes of <paramref name="parent"/> from <paramref name="arcFile"/>.
        /// The parameter to <paramref name="extractStartCallBack"/> is the task description.
        /// </summary>
        /// <param name="arcFile">the ARC to load</param>
        /// <param name="parentPath">the folder whose children will be loaded</param>
        /// <param name="files">the file list to be cleared and updated</param>
        /// <param name="extractStartCallBack">called before starting an extract operation</param>
        /// <param name="extractReportProgressCallBack">contains the file path and progress percentage on extracting multiple files</param>
        /// <param name="extractEndCallBack">called after starting an extract operation with a progress message</param>
        public static List<FileNodeBase> CreateChildNodes(ArcFile arcFile, 
            string parentPath,
            Action<string> extractStartCallBack,
            Action<string, double> extractReportProgressCallBack,
            Action<string> extractEndCallBack)
        {
            if (string.IsNullOrEmpty(parentPath))
            {
                // An empty path should load the root level since we don't use a node for the root.
                return CreateRootLevelNodes(arcFile, extractStartCallBack, extractReportProgressCallBack, extractEndCallBack);
            }

            // TODO: Handle the case where the input is a file by using the parent directory.


            var files = new List<FileNodeBase>();

            // Load with and without a trailing slash to be more forgiving about the input path.
            // If either node isn't a valid path, the ARC won't find any children.
            var cleanedPath = parentPath.Trim('/');
            AddChildNodes(arcFile, cleanedPath, extractStartCallBack, extractReportProgressCallBack, extractEndCallBack, files);
            AddChildNodes(arcFile, cleanedPath + '/', extractStartCallBack, extractReportProgressCallBack, extractEndCallBack, files);

            return files;
        }

        private static void AddChildNodes(ArcFile arcFile, string parentPath, Action<string> extractStartCallBack, Action<string, double> extractReportProgressCallBack, Action<string> extractEndCallBack, List<FileNodeBase> files)
        {
            var arcNode = arcFile.CreateNode(parentPath, ApplicationSettings.Instance.ArcRegion);

            if (arcNode is ArcDirectoryNode directoryNode)
            {
                // Keep track of visited names to avoid having duplicate nodes differing by a trailing slash.
                // Assume file names are also unique within a directory.
                var names = new HashSet<string>();

                var folder = CreateFolder(arcFile, directoryNode, extractStartCallBack, extractReportProgressCallBack, extractEndCallBack, null);

                foreach (var node in arcFile.GetChildren(directoryNode, ApplicationSettings.Instance.ArcRegion))
                {
                    var treeNode = CreateNode(arcFile, folder, node, extractStartCallBack, extractReportProgressCallBack, extractEndCallBack);
                    
                    if (!names.Contains(treeNode.Name))
                    {
                        files.Add(treeNode);
                        names.Add(treeNode.Name);
                    }
                }
            }
        }

        public static FolderNode? CreateFolderNode(ArcFile arcFile, string absolutePath)
        {
            var arcNode = arcFile.CreateNode(absolutePath, ApplicationSettings.Instance.ArcRegion);
            if (arcNode is ArcDirectoryNode directory)
                return new FolderNode(absolutePath, directory);
            else
                return null;
        }

        private static FileNodeBase CreateNode(ArcFile arcFile, FolderNode? parent, IArcNode arcNode,
            Action<string> taskStart, Action<string, double> reportProgress, Action<string> taskEnd)
        {
            switch (arcNode)
            {
                case ArcDirectoryNode directoryNode:
                    var folder = CreateFolder(arcFile, directoryNode, taskStart, reportProgress, taskEnd, parent);
                    return folder;
                case ArcFileNode fileNode:
                    var file = CreateFileNode(arcFile, fileNode, taskEnd);
                    return file;
                default:
                    throw new NotImplementedException($"Unable to create node from {arcNode}");
            }
        }

        private static FileNode CreateFileNode(ArcFile arcFile, ArcFileNode arcNode, Action<string> taskEnd)
        {
            // Lazy initialize the shared file list for performance reasons.
            List<string> getSharedFiles() => arcFile.GetSharedFilePaths(arcNode, ApplicationSettings.Instance.ArcRegion);

            var fileNode = new FileNode(arcNode.FileName, arcNode.Path, arcNode.Extension,
                arcNode.IsShared, arcNode.IsRegional, arcNode.Offset, arcNode.CompSize, arcNode.DecompSize, arcNode.IsCompressed,
                getSharedFiles);

            fileNode.FileExtracting += (s, e) => ExtractFileAsync(arcFile, arcNode, taskEnd);

            return fileNode;
        }

        private static ExtractResult TryExtractFile(ArcFile arcFile, ArcFileNode arcNode)
        {
            var exportPath = GetExportPath(arcNode);

            // Extraction will fail if the directory doesn't exist.
            var exportFileDirectory = Path.GetDirectoryName(exportPath);
            try
            {
                if (!Directory.Exists(exportFileDirectory))
                    Directory.CreateDirectory(exportFileDirectory);
            }
            catch (Exception e)
            {
                Serilog.Log.Error(e, "Error creating directory {@exportFileDirectory}", exportFileDirectory);
                return new ExtractResult { CompletionSummary = $"Error creating directory {exportFileDirectory}", Success = false };
            }

            // Extraction may fail.
            // TODO: Update the C# bindings to store more detailed error info?
            if (!arcFile.TryExtractFile(arcNode, exportPath, ApplicationSettings.Instance.ArcRegion))
            {
                Serilog.Log.Logger.Error("Failed to extract to {@path}", exportPath);
                return new ExtractResult { CompletionSummary = $"Failed to extract {arcNode.Path}", Success = false };
            }

            return new ExtractResult { CompletionSummary = $"Extracted {arcNode.Path}", Success = false };
        }

        private static string GetExportPath(ArcFileNode arcNode)
        {
            // stream: -> stream and prebuilt: -> prebuilt to avoid invalid characters in paths.
            var filePath = arcNode.Path.Replace(":", "");
            if (arcNode.FileName.StartsWith("0x"))
                filePath += "." + arcNode.Extension;

            // If the user selects an absolute path for the extract location, this overrides the current directory.
            var exportDirectory = Tools.ApplicationDirectory.CreateAbsolutePath(ApplicationSettings.Instance.ExtractLocation);
            var paths = new string[] { exportDirectory };

            // Use the OS directory separators instead of the ARC path separators. 
            var exportPath = Path.Combine(paths.Concat(filePath.Split('/')).ToArray());
            return exportPath;
        }

        private static FolderNode CreateFolder(ArcFile arcFile, ArcDirectoryNode arcNode,
            Action<string> taskStart, Action<string, double> reportProgress, Action<string> taskEnd, FolderNode? parent)
        {
            var folder = new FolderNode(arcNode.Path, arcNode);
            folder.FileExtracting += (s, e) => ExtractFolderAsync(arcFile, arcNode, taskStart, reportProgress, taskEnd);

            return folder;
        }

        public static async void ExtractAllFiles(ArcFile arcFile, Action<string> taskStart, Action<string, double> reportProgress, Action<string> taskEnd)
        {
            await RunBackgroundTask("Extracting all files", () => TryExtractAllFiles(arcFile, reportProgress), taskStart, taskEnd);
        }

        private static async Task RunBackgroundTask(string taskDescription,
            Func<ExtractResult> taskToRun, Action<string> taskStart, Action<string> taskEnd)
        {
            taskStart(taskDescription);

            ExtractResult? result = null;

            await Task.Run(() =>
            {
                using (var operation = Operation.Begin(taskDescription))
                {
                    result = taskToRun();
                    if (result.Success)
                        operation.Complete();
                    else
                        operation.Cancel();
                }
            });

            taskEnd(result?.CompletionSummary ?? "");
        }

        private static void AddFilesRecursive(ArcFile arcFile, ArcDirectoryNode root, List<ArcFileNode> files)
        {
            var cleanedPath = root.Path.Trim('/');

            // We don't display separate nodes for directories differing only by a trailing slash.
            // Try both options here in case one was removed.
            var rootNoSlash = arcFile.CreateNode(cleanedPath, ApplicationSettings.Instance.ArcRegion);
            if (rootNoSlash is ArcDirectoryNode rootDirectoryNoSlash)
            {
                AddChildrenRecursive(arcFile, files, rootDirectoryNoSlash);
            }

            var rootWithSlash = arcFile.CreateNode(cleanedPath + '/', ApplicationSettings.Instance.ArcRegion);
            if (rootWithSlash is ArcDirectoryNode rootDirectoryWithSlash)
            {
                AddChildrenRecursive(arcFile, files, rootDirectoryWithSlash);
            }
        }

        private static void AddChildrenRecursive(ArcFile arcFile, List<ArcFileNode> files, ArcDirectoryNode rootDirectoryNoSlash)
        {
            foreach (var child in arcFile.GetChildren(rootDirectoryNoSlash, ApplicationSettings.Instance.ArcRegion))
            {
                // Assume files have no children, so only recurse for directories.
                switch (child)
                {
                    case ArcFileNode file:
                        files.Add(file);
                        break;
                    case ArcDirectoryNode directory:
                        AddFilesRecursive(arcFile, directory, files);
                        break;
                    default:
                        break;
                }
            }
        }

        private static ExtractResult TryExtractAllFiles(ArcFile arcFile, Action<string, double> reportProgress)
        {
            // Loading the files first uses more memory but allows for determinate progress.
            var filesToExtract = new List<ArcFileNode>();
            foreach (var arcNode in arcFile.GetRootNodes())
            {
                if (arcNode is ArcDirectoryNode directory)
                    AddFilesRecursive(arcFile, directory, filesToExtract);
            }

            for (int i = 0; i < filesToExtract.Count; i++)
            {
                TryExtractFile(arcFile, filesToExtract[i]);
                var percentage = i / (double)filesToExtract.Count * 100;

                // Fix the string length to avoid strange flickering when the progress bar resizes.
                var desiredLength = 60;
                reportProgress(filesToExtract[i].Path.PadRight(desiredLength).Substring(0, desiredLength), percentage);
            }

            // Assume the operation succeeds for now.
            // Individual file extractions may still fail.
            var result = new ExtractResult
            {
                Success = true,
                CompletionSummary = $"Extracted {filesToExtract.Count} files"
            };
            return result;
        }

        private static ExtractResult TryExtractFilesRecursive(ArcFile arcFile, ArcDirectoryNode arcNode, Action<string, double> reportProgress)
        {
            // Loading the files first uses more memory but allows for determinate progress.
            var filesToExtract = new List<ArcFileNode>();
            AddFilesRecursive(arcFile, arcNode, filesToExtract);

            for (int i = 0; i < filesToExtract.Count; i++)
            {
                TryExtractFile(arcFile, filesToExtract[i]);
                var percentage = i / (double)filesToExtract.Count * 100;
                reportProgress(filesToExtract[i].Path, percentage);
            }

            // Assume the operation succeeds for now.
            // Individual file extractions may still fail.
            var result = new ExtractResult
            {
                Success = true,
                CompletionSummary = $"Extracted {filesToExtract.Count} files from {arcNode.Path}"
            };
            return result;
        }

        private static async void ExtractFileAsync(ArcFile arcFile, ArcFileNode arcNode, Action<string> taskEnd)
        {
            // Files extract quickly, so there's no need to update the UI by calling taskStart.
            await RunBackgroundTask($"Extracting {arcNode.Path}", () => TryExtractFile(arcFile, arcNode), (_) => { }, taskEnd);
        }

        private static async void ExtractFolderAsync(ArcFile arcFile, ArcDirectoryNode arcNode,
            Action<string> taskStart, Action<string, double> reportProgress, Action<string> taskEnd)
        {
            await RunBackgroundTask($"Extracting files from {arcNode.Path}", () => TryExtractFilesRecursive(arcFile, arcNode, reportProgress), taskStart, taskEnd);
        }
    }
}
