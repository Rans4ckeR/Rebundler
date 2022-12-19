// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.MemoryMappedFiles;

namespace Microsoft.NET.HostModel.AppHost
{
    public static class PEUtils
    {
        /// <summary>
        /// The offset of the PE header pointer in the DOS header.
        /// </summary>
        private const int PEHeaderPointerOffset = 0x3C;

        /// <summary>
        /// The offset of the Subsystem field in the PE header.
        /// </summary>
        private const int SubsystemOffset = 0x5C;

        /// <summary>
        /// The value of the subsystem field which indicates Windows GUI (Graphical UI)
        /// </summary>
        private const ushort WindowsGUISubsystem = 0x2;

        /// <summary>
        /// The value of the subsystem field which indicates Windows CUI (Console)
        /// </summary>
        private const ushort WindowsCUISubsystem = 0x3;

        /// <summary>
        /// This method will attempt to set the subsystem to GUI. The apphost file should be a windows PE file.
        /// </summary>
        /// <param name="accessor">The memory accessor which has the apphost file opened.</param>
        internal static unsafe void SetWindowsGraphicalUserInterfaceBit(MemoryMappedViewAccessor accessor)
        {
            byte* pointer = null;

            try
            {
                accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                byte* bytes = pointer + accessor.PointerOffset;

                // https://en.wikipedia.org/wiki/Portable_Executable
                uint peHeaderOffset = ((uint*)(bytes + PEHeaderPointerOffset))[0];

                if (accessor.Capacity < peHeaderOffset + SubsystemOffset + sizeof(ushort))
                {
                    throw new AppHostNotPEFileException("Subsystem offset out of file range.");
                }

                ushort* subsystem = ((ushort*)(bytes + peHeaderOffset + SubsystemOffset));

                // https://docs.microsoft.com/en-us/windows/desktop/Debug/pe-format#windows-subsystem
                // The subsystem of the prebuilt apphost should be set to CUI
                if (subsystem[0] != WindowsCUISubsystem)
                {
                    throw new AppHostNotCUIException(subsystem[0]);
                }

                // Set the subsystem to GUI
                subsystem[0] = WindowsGUISubsystem;
            }
            finally
            {
                if (pointer != null)
                {
                    accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }
    }
}
