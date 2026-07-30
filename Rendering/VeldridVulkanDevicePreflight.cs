// -----------------------------------------------------------------------------
// File: Rendering/VeldridVulkanDevicePreflight.cs
// Purpose: Runs Vulkan/Veldrid device creation in an isolated child process.
//
// Some Vulkan driver/Veldrid failures can terminate the process with a native
// fast-fail such as 0xc0000409 before managed exception handlers can run.  This
// helper lets the main app find and report that exact failure without losing the
// editor process.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Veldrid;

namespace LightingShowcase.Rendering;

public static class VeldridVulkanDevicePreflight
{
    public const string ChildArgument = "--lighting-showcase-vulkan-device-test";

    public static int RunChildDeviceCreationTest()
    {
        try
        {
            using GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            {
                Debug = false,
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                SyncToVerticalBlank = false
            });

            return gd.BackendType == GraphicsBackend.Vulkan ? 0 : 12;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Vulkan device creation exception:");
            Console.Error.WriteLine(ex);
            return 10;
        }
    }

    public static void VerifyInChildProcess(Action<string> stage)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_SKIP_VULKAN_PREFLIGHT"), "1", StringComparison.Ordinal))
        {
            stage("Vulkan preflight skipped by LIGHTINGSHOWCASE_SKIP_VULKAN_PREFLIGHT=1");
            return;
        }

        string? hostPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            stage("Vulkan preflight skipped: Environment.ProcessPath unavailable");
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = hostPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        string hostName = Path.GetFileNameWithoutExtension(hostPath);
        if (string.Equals(hostName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string assemblyPath = Environment.GetCommandLineArgs().FirstOrDefault() ?? string.Empty;
            if (!Path.IsPathRooted(assemblyPath))
                assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyPath);
            if (!File.Exists(assemblyPath))
                throw new InvalidOperationException("Could not locate the renderer assembly for the Vulkan preflight child process.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        startInfo.ArgumentList.Add(ChildArgument);

        using Process process = new() { StartInfo = startInfo };

        stage("Vulkan preflight child process start");
        process.Start();

        if (!process.WaitForExit(15000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Veldrid Vulkan device preflight timed out during GraphicsDevice.CreateVulkan.");
        }

        int exitCode = process.ExitCode;
        stage($"Vulkan preflight child process exit code: 0x{exitCode:X8} ({exitCode})");

        if (exitCode != 0)
        {
            string stdout = SafeRead(process.StandardOutput);
            string stderr = SafeRead(process.StandardError);
            string nativeHint = exitCode == unchecked((int)0xC0000409)
                ? " Native fast-fail 0xC0000409 occurred inside Vulkan/Veldrid device creation. This is before scene upload/dispatch and is usually a driver/runtime/native interop crash, not a shader or material problem."
                : string.Empty;

            throw new InvalidOperationException(
                $"Veldrid Vulkan device creation failed in isolated preflight. ExitCode=0x{exitCode:X8}.{nativeHint}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }

    private static string SafeRead(StreamReader reader)
    {
        try { return reader.ReadToEnd(); }
        catch { return string.Empty; }
    }
}
