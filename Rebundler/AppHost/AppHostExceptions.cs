// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.NET.HostModel.AppHost
{
    /// <summary>
    /// An instance of this exception is thrown when an AppHost binary update
    /// fails due to known user errors.
    /// </summary>
    public class AppHostUpdateException : Exception
    {
        internal AppHostUpdateException(string message = null)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Unable to use the input file as application host executable because it's not a
    /// Windows executable for the CUI (Console) subsystem.
    /// </summary>
    public sealed class AppHostNotCUIException : AppHostUpdateException
    {
        internal AppHostNotCUIException(ushort subsystem)
            : base($"Selected apphost is not a CUI Windows application. Subsystem: {subsystem}")
        {
        }
    }

    /// <summary>
    ///  Unable to use the input file as an application host executable
    ///  because it's not a Windows PE file
    /// </summary>
    public sealed class AppHostNotPEFileException : AppHostUpdateException
    {
        public readonly string Reason;

        internal AppHostNotPEFileException(string reason)
            : base($"Selected apphost is not a valid PE file. {reason}")
        {
            Reason = reason;
        }
    }
}
