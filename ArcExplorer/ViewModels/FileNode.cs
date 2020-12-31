﻿using System.Collections.Generic;
using System.IO;
using System;

namespace ArcExplorer.ViewModels
{
    public sealed class FileNode : FileNodeBase
    {
        public ulong Offset { get; }
        public ulong CompressedSize { get; }
        public ulong DecompressedSize { get; }
        public List<string> SharedFilePaths
        { 
            get
            {
                // Lazy load for performance reasons.
                if (sharedFilePaths == null)
                {
                    sharedFilePaths = getSharedFiles();
                    // Use ascending alphabetical order.
                    sharedFilePaths.Sort();
                }
                return sharedFilePaths;
            }
        }
        private List<string>? sharedFilePaths;

        public string SharedFileDescription => $"Shared with the following {SharedFilePaths.Count} files:";

        public override Dictionary<string, string> ObjectProperties => GetPropertyInfo();

        private readonly Func<List<string>> getSharedFiles;

        public FileNode(string name, string absolutePath, bool isShared, bool isRegional, ulong offset, ulong compressedSize, ulong decompressedSize, Func<List<string>> getSharedFiles) : base(name, absolutePath, isShared, isRegional)
        {
            var extension = Path.GetExtension(name);
            Offset = offset;
            CompressedSize = compressedSize;
            DecompressedSize = decompressedSize;
            DetailsIconKey = FileFormatInfo.GetFileIconKey(extension);
            Description = FileFormatInfo.GetDescription(extension);
            this.getSharedFiles = getSharedFiles;
        }

        private Dictionary<string, string> GetPropertyInfo()
        {
            return new Dictionary<string, string>()
            {
                { "Description", Description },
                { "Offset", $"{Tools.ValueConversion.GetValueFromPreferencesFormat(Offset)} bytes" },
                { "Compressed Size", $"{Tools.ValueConversion.GetValueFromPreferencesFormat(CompressedSize)} bytes" },
                { "Decompressed Size", $"{Tools.ValueConversion.GetValueFromPreferencesFormat(DecompressedSize)} bytes" },
            };
        }
    }
}
