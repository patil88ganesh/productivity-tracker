using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MiniStopwatch.Installer;

internal static class Program
{
    private const string ProductName = "Productivity Tracker";
    private const string ProductFolderName = "ProductivityTracker";
    private const string AppProcessName = "ProductivityTracker";
    private const string AppExecutable = "ProductivityTracker.exe";
    private const string UninstallerExecutable = "Uninstall Productivity Tracker.exe";
    private const string UninstallRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ProductivityTracker";
    private const string LegacyProductName = "MiniStopwatch";
    private const string LegacyUninstallRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MiniStopwatch";
    private const string NativeHostName = "com.patil88ganesh.productivity_tracker";
    private const string NativeHostManifestFile = "native-messaging-host.json";
    private const string NativeHostExecutable = "ProductivityTracker.NativeHost.exe";
    private const string NativeHostProcessName = "ProductivityTracker.NativeHost";
    private const string ExtensionId = "dhnpejafolnigilfhbbdiaanpfegpggd";
    private const string ChromeNativeHostRegistryPath =
        @"Software\Google\Chrome\NativeMessagingHosts\com.patil88ganesh.productivity_tracker";
    private const string EdgeNativeHostRegistryPath =
        @"Software\Microsoft\Edge\NativeMessagingHosts\com.patil88ganesh.productivity_tracker";

    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        ProductFolderName);

    private static readonly string LegacyInstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        LegacyProductName);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--uninstall-temp", StringComparer.OrdinalIgnoreCase))
            {
                return CompleteUninstall(args);
            }

            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                return BeginUninstall();
            }

            return Install();
        }
        catch (Exception exception)
        {
            ShowMessage(
                $"Setup could not complete.\n\n{exception.Message}",
                ProductName,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Install()
    {
        if (Process.GetProcessesByName(AppProcessName).Length > 0 ||
            Process.GetProcessesByName(LegacyProductName).Length > 0)
        {
            ShowMessage(
                "Productivity Tracker or MiniStopwatch is running. Close it and run setup again.",
                ProductName,
                MessageBoxIcon.Warning);
            return 1;
        }

        var choice = ShowMessage(
            "Install Productivity Tracker for your Windows account?",
            ProductName,
            MessageBoxIcon.Question,
            MessageBoxButtons.YesNo);
        if (choice != MessageBoxResult.Yes)
        {
            return 0;
        }

        RemoveNativeMessagingHostRegistration();
        StopNativeMessagingHosts();
        Directory.CreateDirectory(InstallDirectory);
        ExtractPayload(InstallDirectory);
        RegisterNativeMessagingHost();
        CopyUninstaller();
        CreateShortcuts();
        RegisterUninstaller();
        RemoveLegacyInstallation();

        ShowMessage(
            "Productivity Tracker was installed.\n\nOptional Focus Protection is available from the right-click menu. It pauses tracking on supported social-media sites and WhatsApp Web after browser extension setup.",
            ProductName,
            MessageBoxIcon.Information);

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(InstallDirectory, AppExecutable),
            UseShellExecute = true,
        });
        return 0;
    }

    private static void ExtractPayload(string destination)
    {
        using var payload = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ProductivityTracker.Payload.zip")
            ?? throw new InvalidOperationException("The application payload is missing from setup.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!destinationPath.StartsWith(
                    Path.GetFullPath(destination) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The setup payload contains an invalid path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (destinationDirectory == null)
            {
                throw new InvalidDataException("The setup payload contains an invalid destination.");
            }

            Directory.CreateDirectory(destinationDirectory);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CopyUninstaller()
    {
        var source = GetCurrentExecutablePath();
        File.Copy(source, Path.Combine(InstallDirectory, UninstallerExecutable), overwrite: true);
    }

    private static void CreateShortcuts()
    {
        var appPath = Path.Combine(InstallDirectory, AppExecutable);
        var desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"{ProductName}.lnk");
        var startMenuDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            ProductName);

        Directory.CreateDirectory(startMenuDirectory);
        CreateShortcut(desktopShortcut, appPath);
        CreateShortcut(Path.Combine(startMenuDirectory, $"{ProductName}.lnk"), appPath);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Unable to create a Windows shortcut.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = InstallDirectory;
        shortcut.Description = "Compact productivity time tracker";
        shortcut.IconLocation = targetPath;
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void RegisterUninstaller()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath);
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", "2.5.2");
        key.SetValue("Publisher", ProductName);
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", Path.Combine(InstallDirectory, AppExecutable));
        key.SetValue(
            "UninstallString",
            $"\"{Path.Combine(InstallDirectory, UninstallerExecutable)}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static int BeginUninstall()
    {
        var choice = ShowMessage(
            "Uninstall Productivity Tracker?",
            ProductName,
            MessageBoxIcon.Question,
            MessageBoxButtons.YesNo);
        if (choice != MessageBoxResult.Yes)
        {
            return 0;
        }

        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            process.CloseMainWindow();
            process.WaitForExit(3000);
        }

        var temporaryUninstaller = Path.Combine(
            Path.GetTempPath(),
            $"ProductivityTracker-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(GetCurrentExecutablePath(), temporaryUninstaller);

        Process.Start(new ProcessStartInfo
        {
            FileName = temporaryUninstaller,
            Arguments = $"--uninstall-temp \"{InstallDirectory}\" {Process.GetCurrentProcess().Id}",
            UseShellExecute = true,
        });
        return 0;
    }

    private static int CompleteUninstall(string[] args)
    {
        var markerIndex = Array.FindIndex(
            args,
            value => string.Equals(value, "--uninstall-temp", StringComparison.OrdinalIgnoreCase));
        if (markerIndex < 0 || markerIndex + 2 >= args.Length)
        {
            throw new ArgumentException("Invalid uninstall arguments.");
        }

        var directory = args[markerIndex + 1];
        if (!int.TryParse(args[markerIndex + 2], out var parentProcessId))
        {
            throw new ArgumentException("Invalid uninstall process identifier.");
        }

        try
        {
            Process.GetProcessById(parentProcessId).WaitForExit(10000);
        }
        catch (ArgumentException)
        {
        }

        RemoveShortcuts(ProductName);
        RemoveNativeMessagingHostRegistration();
        StopNativeMessagingHosts();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        ShowMessage("Productivity Tracker was uninstalled.", ProductName, MessageBoxIcon.Information);
        return 0;
    }

    private static void RemoveLegacyInstallation()
    {
        RemoveShortcuts(LegacyProductName);
        Registry.CurrentUser.DeleteSubKeyTree(
            LegacyUninstallRegistryPath,
            throwOnMissingSubKey: false);

        if (Directory.Exists(LegacyInstallDirectory))
        {
            Directory.Delete(LegacyInstallDirectory, recursive: true);
        }
    }

    private static void RegisterNativeMessagingHost()
    {
        var executablePath = Path.Combine(InstallDirectory, NativeHostExecutable);
        var manifestPath = Path.Combine(InstallDirectory, NativeHostManifestFile);
        var escapedExecutablePath = executablePath.Replace("\\", "\\\\");
        var manifest =
            "{\n" +
            $"  \"name\": \"{NativeHostName}\",\n" +
            "  \"description\": \"Productivity Tracker Focus Protection bridge\",\n" +
            $"  \"path\": \"{escapedExecutablePath}\",\n" +
            "  \"type\": \"stdio\",\n" +
            $"  \"allowed_origins\": [\"chrome-extension://{ExtensionId}/\"]\n" +
            "}";
        File.WriteAllText(manifestPath, manifest, new System.Text.UTF8Encoding(false));

        RegisterNativeHostForBrowser(ChromeNativeHostRegistryPath, manifestPath);
        RegisterNativeHostForBrowser(EdgeNativeHostRegistryPath, manifestPath);
    }

    private static void RegisterNativeHostForBrowser(
        string registryPath,
        string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath);
        key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
    }

    private static void RemoveNativeMessagingHostRegistration()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            ChromeNativeHostRegistryPath,
            throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            EdgeNativeHostRegistryPath,
            throwOnMissingSubKey: false);
    }

    private static void StopNativeMessagingHosts()
    {
        var expectedExecutablePath = Path.GetFullPath(
            Path.Combine(InstallDirectory, NativeHostExecutable));

        foreach (var process in Process.GetProcessesByName(NativeHostProcessName))
        {
            try
            {
                var processPath = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(processPath) ||
                    !string.Equals(
                        Path.GetFullPath(processPath),
                        expectedExecutablePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void RemoveShortcuts(string productName)
    {
        var desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"{productName}.lnk");
        var startMenuDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            productName);

        if (File.Exists(desktopShortcut))
        {
            File.Delete(desktopShortcut);
        }

        if (Directory.Exists(startMenuDirectory))
        {
            Directory.Delete(startMenuDirectory, recursive: true);
        }
    }

    private static MessageBoxResult ShowMessage(
        string text,
        string caption,
        MessageBoxIcon icon,
        MessageBoxButtons buttons = MessageBoxButtons.Ok)
    {
        var result = MessageBox(
            IntPtr.Zero,
            text,
            caption,
            (uint)icon | (uint)buttons | 0x00040000);
        return result == 6 ? MessageBoxResult.Yes : MessageBoxResult.Other;
    }

    private static string GetCurrentExecutablePath()
    {
        using (var process = Process.GetCurrentProcess())
        {
            if (process.MainModule == null || string.IsNullOrEmpty(process.MainModule.FileName))
            {
                throw new InvalidOperationException("Setup executable path is unavailable.");
            }

            return process.MainModule.FileName;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr windowHandle,
        string text,
        string caption,
        uint type);

    private enum MessageBoxResult
    {
        Other,
        Yes,
    }

    private enum MessageBoxButtons : uint
    {
        Ok = 0x00000000,
        YesNo = 0x00000004,
    }

    private enum MessageBoxIcon : uint
    {
        Error = 0x00000010,
        Question = 0x00000020,
        Warning = 0x00000030,
        Information = 0x00000040,
    }
}
