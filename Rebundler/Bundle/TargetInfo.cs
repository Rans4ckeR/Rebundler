// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.NET.HostModel.Bundle
{
    /// <summary>
    /// TargetInfo: Information about the target for which the single-file bundle is built.
    ///
    /// Currently the TargetInfo only tracks:
    ///   - the target operating system
    ///   - the target architecture
    ///   - the target framework
    ///   - the default options for this target
    ///   - the assembly alignment for this target
    /// </summary>

    public class TargetInfo
    {
        public readonly Version FrameworkVersion;
        public readonly uint BundleMajorVersion;
        public readonly BundleOptions DefaultOptions;

        public TargetInfo()
        {
            FrameworkVersion = Environment.Version;
            BundleMajorVersion = 6u;
            DefaultOptions = BundleOptions.None;
        }

        // The .net core 3 apphost doesn't care about semantics of FileType -- all files are extracted at startup.
        // However, the apphost checks that the FileType value is within expected bounds, so set it to the first enumeration.
        public FileType TargetSpecificFileType(FileType fileType) => (BundleMajorVersion == 1) ? FileType.Unknown : fileType;

        // In .net core 3.x, bundle processing happens within the AppHost.
        // Therefore HostFxr and HostPolicy can be bundled within the single-file app.
        // In .net 5, bundle processing happens in HostFxr and HostPolicy libraries.
        // Therefore, these libraries themselves cannot be bundled into the single-file app.
        // This problem is mitigated by statically linking these host components with the AppHost.
        // https://github.com/dotnet/runtime/issues/32823
        public bool ShouldExclude(string relativePath) =>
            (FrameworkVersion.Major != 3) && (relativePath.Equals(HostFxr) || relativePath.Equals(HostPolicy));

        private static string HostFxr => "hostfxr.dll";
        private static string HostPolicy => "hostpolicy.dll";
    }
}
