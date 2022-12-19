// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using Microsoft.NET.HostModel.AppHost;

namespace Microsoft.NET.HostModel.Bundle
{
    /// <summary>
    /// Bundler: Functionality to embed the managed app and its dependencies
    /// into the host native binary.
    /// </summary>
    public class Bundler
    {
        public readonly Manifest BundleManifest;

        private readonly string _hostName;
        private readonly string _outputDir;
        private readonly string _depsJson;
        private readonly string _runtimeConfigJson;
        private readonly string _runtimeConfigDevJson;

        private readonly TargetInfo _target;
        private readonly BundleOptions _options;

        public Bundler(string hostName,
                       string outputDir,
                       BundleOptions options = BundleOptions.None)
        {
            _hostName = hostName;
            _outputDir = Path.GetFullPath(string.IsNullOrEmpty(outputDir) ? Environment.CurrentDirectory : outputDir);
            _target = new TargetInfo();
            string appAssemblyName = Path.GetFileNameWithoutExtension(hostName);
            _depsJson = appAssemblyName + ".deps.json";
            _runtimeConfigJson = appAssemblyName + ".runtimeconfig.json";
            _runtimeConfigDevJson = appAssemblyName + ".runtimeconfig.dev.json";

            BundleManifest = new Manifest(_target.BundleMajorVersion);
            _options = _target.DefaultOptions | options;
        }

        /// <summary>
        /// Embed 'file' into 'bundle'
        /// </summary>
        /// <returns>
        /// startOffset: offset of the start 'file' within 'bundle'
        /// compressedSize: size of the compressed data, if entry was compressed, otherwise 0
        /// </returns>
        private static (long startOffset, long compressedSize) AddToBundle(Stream bundle, Stream file)
        {
            file.Position = 0;
            long startOffset = bundle.Position;
            file.CopyTo(bundle);

            return (startOffset, 0);
        }

        private bool IsHost(string fileRelativePath)
        {
            return fileRelativePath.Equals(_hostName);
        }

        private bool ShouldIgnore(string fileRelativePath)
        {
            return fileRelativePath.Equals(_runtimeConfigDevJson);
        }

        private bool ShouldExclude(FileType type, string relativePath)
        {
            switch (type)
            {
                case FileType.Assembly:
                case FileType.DepsJson:
                case FileType.RuntimeConfigJson:
                    return false;

                case FileType.NativeBinary:
                    return !_options.HasFlag(BundleOptions.BundleNativeBinaries) || _target.ShouldExclude(relativePath);

                case FileType.Symbols:
                    return !_options.HasFlag(BundleOptions.BundleSymbolFiles);

                case FileType.Unknown:
                    return !_options.HasFlag(BundleOptions.BundleOtherFiles);

                default:
                    Debug.Assert(false);
                    return false;
            }
        }

        private static bool IsAssembly(string path, out bool isPE)
        {
            isPE = false;

            using (FileStream file = File.OpenRead(path))
            {
                try
                {
                    PEReader peReader = new PEReader(file);
                    CorHeader corHeader = peReader.PEHeaders.CorHeader;

                    isPE = true; // If peReader.PEHeaders doesn't throw, it is a valid PEImage
                    return corHeader != null;
                }
                catch (BadImageFormatException)
                {
                }
            }

            return false;
        }

        private FileType InferType(FileSpec fileSpec)
        {
            if (fileSpec.BundleRelativePath.Equals(_depsJson))
            {
                return FileType.DepsJson;
            }

            if (fileSpec.BundleRelativePath.Equals(_runtimeConfigJson))
            {
                return FileType.RuntimeConfigJson;
            }

            if (Path.GetExtension(fileSpec.BundleRelativePath).ToLowerInvariant().Equals(".pdb"))
            {
                return FileType.Symbols;
            }

            bool isPE;
            if (IsAssembly(fileSpec.SourcePath, out isPE))
            {
                return FileType.Assembly;
            }

            bool isNativeBinary = isPE;

            if (isNativeBinary)
            {
                return FileType.NativeBinary;
            }

            return FileType.Unknown;
        }

        /// <summary>
        /// Generate a bundle, given the specification of embedded files
        /// </summary>
        /// <param name="fileSpecs">
        /// An enumeration FileSpecs for the files to be embedded.
        ///
        /// Files in fileSpecs that are not bundled within the single file bundle,
        /// and should be published as separate files are marked as "IsExcluded" by this method.
        /// This doesn't include unbundled files that should be dropped, and not published as output.
        /// </param>
        /// <returns>
        /// The full path the generated bundle file
        /// </returns>
        /// <exceptions>
        /// ArgumentException if input is invalid
        /// IOExceptions and ArgumentExceptions from callees flow to the caller.
        /// </exceptions>
        public string GenerateBundle(IReadOnlyList<FileSpec> fileSpecs)
        {
            string hostSource;
            try
            {
                hostSource = fileSpecs.Single(x => x.BundleRelativePath.Equals(_hostName)).SourcePath;
            }
            catch (InvalidOperationException)
            {
                throw new ArgumentException("Invalid input specification: Must specify the host binary");
            }

            string bundlePath = Path.Combine(_outputDir, _hostName);

            BinaryUtils.CopyFile(hostSource, bundlePath);

            // Note: We're comparing file paths both on the OS we're running on as well as on the target OS for the app
            // We can't really make assumptions about the file systems (even on Linux there can be case insensitive file systems
            // and vice versa for Windows). So it's safer to do case sensitive comparison everywhere.
            var relativePathToSpec = new Dictionary<string, FileSpec>(StringComparer.Ordinal);

            long headerOffset;
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(bundlePath)))
            {
                Stream bundle = writer.BaseStream;
                bundle.Position = bundle.Length;

                foreach (var fileSpec in fileSpecs)
                {
                    string relativePath = fileSpec.BundleRelativePath;

                    if (IsHost(relativePath))
                    {
                        continue;
                    }

                    if (ShouldIgnore(relativePath))
                    {
                        continue;
                    }

                    FileType type = InferType(fileSpec);

                    if (ShouldExclude(type, relativePath))
                    {
                        continue;
                    }

                    if (relativePathToSpec.TryGetValue(fileSpec.BundleRelativePath, out _))
                    {
                        // Exact duplicate - intentionally skip and don't include a second copy in the bundle
                        continue;
                    }

                    relativePathToSpec.Add(fileSpec.BundleRelativePath, fileSpec);

                    using (FileStream file = File.OpenRead(fileSpec.SourcePath))
                    {
                        FileType targetType = _target.TargetSpecificFileType(type);
                        (long startOffset, long compressedSize) = AddToBundle(bundle, file);
                        BundleManifest.AddEntry(targetType, file, relativePath, startOffset, compressedSize);
                    }
                }

                // Write the bundle manifest
                headerOffset = BundleManifest.Write(writer);
            }

            HostWriter.SetAsBundle(bundlePath, headerOffset);

            return bundlePath;
        }
    }
}
