using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using ICSharpCode.ILSpyX;
using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    private const uint RT_ICON = 3;
    private const uint RT_GROUP_ICON = 14;
    private const string APP_HOST_TEMPLATE = "apphost.exe"; // Copied from C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x86\7.0.2\runtimes\win-x86\native\apphost.exe

    [SupportedOSPlatform("windows5.0")]
    private static async Task Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Write("Example usage: Rebundler application.exe icon.ico");
            Environment.Exit(0);
        }

        string exeFilePath = args[0];
        string iconFilePath = args[1];
        var loadedPackage = LoadedPackage.FromBundle(exeFilePath);
        var packageEntries = loadedPackage!.Entries;
        string currentDirectory = Path.GetDirectoryName(exeFilePath)!;
        string workingDirectory = $"{currentDirectory}\\out";

        Directory.CreateDirectory(workingDirectory);
        await ExtractPackageEntries(workingDirectory, packageEntries).ConfigureAwait(false);

        string assemblyDllName = packageEntries.Single(q => q.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Name;

        await UpdateIcons(workingDirectory, iconFilePath, assemblyDllName).ConfigureAwait(false);

        string appHostTemplateFilePath = await ExtractAppHostTemplate(workingDirectory).ConfigureAwait(false);
        string assemblyExeName = $"{Path.GetFileNameWithoutExtension(assemblyDllName)}.exe";
        string newAssemblyExeFilePath = $"{currentDirectory}\\out\\{assemblyExeName}";

        CreateAppHost(workingDirectory, assemblyDllName, newAssemblyExeFilePath, appHostTemplateFilePath);

        string bundle = CreateBundle(workingDirectory, assemblyExeName, packageEntries);

        File.Move(bundle, $"{currentDirectory}\\New{assemblyExeName}");
        Directory.Delete(workingDirectory, true);
    }

    private static string CreateBundle(string workingDirectory, string assemblyExeName, IReadOnlyList<PackageEntry> packageEntries)
    {
        List<FileSpec> fileSpecs = packageEntries.Select(q => new FileSpec($"{workingDirectory}\\{q.Name}", q.Name)).ToList();

        fileSpecs.Add(new FileSpec($"{workingDirectory}\\{assemblyExeName}", assemblyExeName));

        var bundler = new Bundler(assemblyExeName, $"{workingDirectory}\\out1");

        return bundler.GenerateBundle(fileSpecs);
    }

    private static void CreateAppHost(string workingDirectory, string assemblyDllName, string newAssemblyExeFilePath, string appHostTemplateFilePath)
    {
        HostWriter.CreateAppHost(
            appHostTemplateFilePath,
            newAssemblyExeFilePath,
            assemblyDllName,
            true,
            $"{workingDirectory}\\{assemblyDllName}");
    }

    private static async Task<string> ExtractAppHostTemplate(string workingDirectory)
    {
        var assembly = Assembly.GetAssembly(typeof(Program));
        Stream appHostTemplateStream = assembly!.GetManifestResourceStream($"Rebundler.Resources.{APP_HOST_TEMPLATE}")!;
        string appHostTemplateFilePath = $"{workingDirectory}\\{APP_HOST_TEMPLATE}";
        FileStream appHostFileStream = File.Create(appHostTemplateFilePath);

        await using (appHostTemplateStream.ConfigureAwait(false))
        {
            await using (appHostFileStream.ConfigureAwait(false))
            {
                await appHostTemplateStream.CopyToAsync(appHostFileStream).ConfigureAwait(false);
            }
        }

        return appHostTemplateFilePath;
    }

    [SupportedOSPlatform("windows5.0")]
    private static async Task UpdateIcons(string workingDirectory, string iconFilePath, string assemblyName)
    {
        FileStream fs = File.OpenRead(iconFilePath);

        await using (fs.ConfigureAwait(false))
        {
            var reader = new IconReader(fs);

            await ChangeIcon($"{workingDirectory}\\{assemblyName}", reader.Icons).ConfigureAwait(false);
        }
    }

    private static async Task ExtractPackageEntries(string workingDirectory, IReadOnlyList<PackageEntry> packageEntries)
    {
        foreach (PackageEntry packageEntry in packageEntries)
        {
            Stream? stream = packageEntry.TryOpenStream();

            await using (stream!.ConfigureAwait(false))
            {
                FileStream fileStream = File.Create($"{workingDirectory}\\{packageEntry.Name}");

                await using (fileStream.ConfigureAwait(false))
                {
                    await stream!.CopyToAsync(fileStream).ConfigureAwait(false);
                }
            }
        }
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
            if (UpdateResourceW(handleExe.DangerousGetHandle(), RT_ICON, index, 0, icon.Data!, icon.Size) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            index++;
        }

        byte[] groupdata = await icons.ToGroupData().ConfigureAwait(false);

        if (UpdateResourceW(handleExe.DangerousGetHandle(), RT_GROUP_ICON, 32512, 0, groupdata, (uint)groupdata.Length) == 1)
        {
            if (!PInvoke.EndUpdateResource(handleExe, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [LibraryImport("kernel32.dll")]
    [SupportedOSPlatform("windows5.0")]
    private static partial int UpdateResourceW(nint hUpdate, uint lpType, ushort lpName, ushort wLanguage, byte[] lpData, uint cbData);
}