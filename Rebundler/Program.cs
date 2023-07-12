using System.ComponentModel;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using ICSharpCode.ILSpyX;
using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;

internal static partial class Program
{
    private const uint RT_ICON = 3;
    private const uint RT_GROUP_ICON = 14;
    private const string APP_HOST_TEMPLATE = "apphost.exe"; // Copied from C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-<architecture>\7.0.9\runtimes\win-<architecture>\native\apphost.exe

    [SupportedOSPlatform("windows5.0")]
    private static async Task Main(string[] args)
    {
        if (args.Length is not 2)
        {
            Console.Write("Example usage: Rebundler application.exe icon.ico");
            Environment.Exit(1);
        }

        string exeFilePath = args[0];
        string iconFilePath = args[1];
        LoadedPackage? loadedPackage = LoadedPackage.FromBundle(exeFilePath);
        var packageEntries = loadedPackage!.Entries;
        string currentDirectory = Path.GetDirectoryName(exeFilePath)!;
        string workingDirectory = $"{currentDirectory}\\out";

        Directory.CreateDirectory(workingDirectory);
        await ExtractPackageEntriesAsync(workingDirectory, packageEntries).ConfigureAwait(false);

        string assemblyDllName = packageEntries.Single(q => q.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Name;
        Architecture architecture;
        var fs = new FileStream(exeFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        await using (fs.ConfigureAwait(false))
        {
            using var peReader = new PEReader(fs);

            architecture = peReader.PEHeaders.CoffHeader.Machine switch
            {
                Machine.I386 => Architecture.X86,
                Machine.Arm => Architecture.Arm,
                Machine.Amd64 => Architecture.X64,
                Machine.Arm64 => Architecture.Arm64,
                _ => throw new ArgumentOutOfRangeException(nameof(Machine), peReader.PEHeaders.CoffHeader.Machine.ToString(), null)
            };
        }

        await UpdateIconsAsync(workingDirectory, iconFilePath, assemblyDllName).ConfigureAwait(false);

        string appHostTemplateFilePath = await ExtractAppHostTemplateAsync(workingDirectory, architecture).ConfigureAwait(false);
        string assemblyExeName = $"{Path.GetFileNameWithoutExtension(assemblyDllName)}.exe";
        string newAssemblyExeFilePath = $"{currentDirectory}\\out\\{assemblyExeName}";

        CreateAppHost(workingDirectory, assemblyDllName, newAssemblyExeFilePath, appHostTemplateFilePath);

        string bundle = CreateBundle(workingDirectory, assemblyExeName, packageEntries);

        File.Move(bundle, $"{currentDirectory}\\New{assemblyExeName}");
        Directory.Delete(workingDirectory, true);
    }

    private static string CreateBundle(string workingDirectory, string assemblyExeName, IEnumerable<PackageEntry> packageEntries)
    {
        List<FileSpec> fileSpecs = packageEntries.Select(q => new FileSpec($"{workingDirectory}\\{q.Name}", q.Name)).ToList();

        fileSpecs.Add(new($"{workingDirectory}\\{assemblyExeName}", assemblyExeName));

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

    private static async ValueTask<string> ExtractAppHostTemplateAsync(string workingDirectory, Architecture architecture)
    {
        var assembly = Assembly.GetAssembly(typeof(Program));
        Stream appHostTemplateStream = assembly!.GetManifestResourceStream($"Rebundler.Resources.{architecture}.{APP_HOST_TEMPLATE}")!;
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
    private static async ValueTask UpdateIconsAsync(string workingDirectory, string iconFilePath, string assemblyName)
    {
        FileStream fs = File.OpenRead(iconFilePath);

        await using (fs.ConfigureAwait(false))
        {
            var reader = new IconReader(fs);

            await ChangeIconAsync($"{workingDirectory}\\{assemblyName}", reader.Icons).ConfigureAwait(false);
        }
    }

    private static async ValueTask ExtractPackageEntriesAsync(string workingDirectory, IEnumerable<PackageEntry> packageEntries)
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
    private static async ValueTask ChangeIconAsync(string exeFilePath, Icons icons)
    {
        HANDLE handleExe = PInvoke.BeginUpdateResource(exeFilePath, false);

        if (handleExe.IsNull)
            throw new();

        const ushort startIndex = 1;
        ushort index = startIndex;

        foreach (Icon icon in icons)
        {
            if (UpdateResourceW(handleExe, RT_ICON, index, 0, icon.Data!, icon.Size) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            index++;
        }

        byte[] groupData = await icons.ToGroupData().ConfigureAwait(false);

        if (UpdateResourceW(handleExe, RT_GROUP_ICON, 32512, 0, groupData, (uint)groupData.Length) == 1)
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
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UpdateResourceW(nint hUpdate, uint lpType, ushort lpName, ushort wLanguage, byte[] lpData, uint cbData);
}