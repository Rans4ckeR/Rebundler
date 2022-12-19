// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using ICSharpCode.ILSpyX;
using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;
using Microsoft.Win32.SafeHandles;

//https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/GenerateBundle.cs
//https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/CreateAppHost.cs
//C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x86\7.0.1\runtimes\win-x86\native\apphost.exe

internal static partial class Program
{
    private const uint RT_ICON = 3;
    private const uint RT_GROUP_ICON = 14;
    private const string APP_HOST_TEMPLATE = "apphost.exe";

    [SupportedOSPlatform("windows5.0")]
    private static async Task Main(string[] args)
    {
        string exeFilePath = args[0];
        string iconFilePath = args[1];

        Directory.CreateDirectory($"{Path.GetDirectoryName(exeFilePath)!}\\out");

        var assembly = Assembly.GetAssembly(typeof(Program));
        Stream appHostTemplateStream = assembly!.GetManifestResourceStream($"Rebundler.{APP_HOST_TEMPLATE}")!;
        string appHostTemplateFilePath = $"{Path.GetDirectoryName(exeFilePath)}\\out\\{APP_HOST_TEMPLATE}";
        FileStream appHostFileStream = File.Create(appHostTemplateFilePath);

        await using (appHostTemplateStream.ConfigureAwait(false))
        {
            await using (appHostFileStream.ConfigureAwait(false))
            {
                await appHostTemplateStream.CopyToAsync(appHostFileStream).ConfigureAwait(false);
            }
        }

        var loadedPackage = LoadedPackage.FromBundle(exeFilePath);

        foreach (PackageEntry packageEntry in loadedPackage!.Entries)
        {
            using (Stream? stream = packageEntry.TryOpenStream())
            {
                using (FileStream fileStream = File.Create($"{Path.GetDirectoryName(exeFilePath)}\\out\\{packageEntry.Name}"))
                {
                    await stream!.CopyToAsync(fileStream);
                }
            }
        }

        var fs = File.OpenRead(iconFilePath);
        await using (fs.ConfigureAwait(false))
        {
            var reader = new IconReader(fs);

            await ChangeIcon($"{Path.GetDirectoryName(exeFilePath)}\\out\\{loadedPackage.Entries.Single(q => q.Name.EndsWith(".dll")).Name}", reader.Icons).ConfigureAwait(false);
        }

        HostWriter.CreateAppHost(
            appHostTemplateFilePath,
            $"{Path.GetDirectoryName(exeFilePath)}\\out\\{loadedPackage.Entries.Single(q => q.Name.EndsWith(".dll")).Name.Replace(".dll", ".exe")}",
            loadedPackage.Entries.Single(q => q.Name.EndsWith(".dll")).Name,
            true,
            $"{Path.GetDirectoryName(exeFilePath)}\\out\\{loadedPackage.Entries.Single(q => q.Name.EndsWith(".dll")).Name}");

        var bundler = new Bundler($"{Path.GetFileNameWithoutExtension(exeFilePath)}.exe", $"{Path.GetDirectoryName(exeFilePath)!}\\out1");
        List<FileSpec> x = loadedPackage.Entries.Select(q => new FileSpec($"{Path.GetDirectoryName(exeFilePath)}\\out\\{q.Name}", q.Name)).ToList();

        x.Add(new FileSpec($"{Path.GetDirectoryName(exeFilePath)}\\out\\{Path.GetFileName(exeFilePath)}", Path.GetFileName(exeFilePath)));

        string bundle = bundler.GenerateBundle(x);

        //using FileStream fileStream = File.OpenRead(exeFilePath);
        //using var accessor = new BinaryReader(fileStream);

        //int position = BinaryUtils.SearchInFile(exeFilePath, bundleSignature);

        //accessor.BaseStream.Position = position - sizeof(long);

        //long headerOffset = accessor.ReadInt64();

        //accessor.BaseStream.Position = headerOffset;

        //uint majorVersion = accessor.ReadUInt32();
        //uint minorVersion = accessor.ReadUInt32();
        //int numberOfFiles = accessor.ReadInt32();
        //string base64BundleHash = accessor.ReadString();
        //long depsJsonOffset = accessor.ReadInt64();
        //long depsJsonSize = accessor.ReadInt64();
        //long runtimeJsonOffset = accessor.ReadInt64();
        //long runtimeJsonSize = accessor.ReadInt64();
        //ulong flags = accessor.ReadUInt64();

        //for (int i = 0; i < numberOfFiles; i++)
        //{
        //    long offset = accessor.ReadInt64();
        //    long size = accessor.ReadInt64();
        //    long compressedSize = accessor.ReadInt64();
        //    byte type = accessor.ReadByte();
        //    string relativePath = accessor.ReadString();
        //}
    }

    [SupportedOSPlatform("windows5.0")]
    private static async ValueTask ChangeIcon(string exeFilePath, Icons icons)
    {
        SafeFileHandle? handleExe = PInvoke.BeginUpdateResource(exeFilePath, false);

        if (handleExe.IsInvalid)
            throw new();

        const ushort startIndex = 1;
        ushort index = startIndex;

        foreach (Icon icon in icons)
        {
            //unsafe
            //{

                //if (!PInvoke.UpdateResource(handleExe, $"#{RT_ICON}", index is startIndex ? "MAINICON" : $"#{index}", 0, &pointer, icon.Size))
                //if (!PInvoke.UpdateResource(handleExe, $"{RT_ICON}", $"#{index}", 0, &pointer, icon.Size))
                if (UpdateResource(handleExe.DangerousGetHandle(), RT_ICON, index, 0, icon.Data!, icon.Size) != 1)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                index++;
            //}
        }

        byte[] groupdata = await icons.ToGroupData().ConfigureAwait(false);

        //unsafe
        //{
            //fixed (void* pointer = groupdata)
            //{
                //if (PInvoke.UpdateResource(handleExe, $"{RT_GROUP_ICON}", $"#{32512}", 0, &pointer, (uint)groupdata.Length))
                if (UpdateResource(handleExe.DangerousGetHandle(), RT_GROUP_ICON, 32512, 0, groupdata, (uint)groupdata.Length) == 1)
                {
                    if (!PInvoke.EndUpdateResource(handleExe, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            //}
        //}
    }

    [LibraryImport("kernel32.dll")]
    private static partial int UpdateResource(nint hUpdate, uint lpType, ushort lpName, ushort wLanguage, byte[] lpData, uint cbData);
}
