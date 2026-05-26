using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FastGithub
{
    /// <summary>
    /// 嵌入资源释放器，用于单文件发布模式
    /// 将嵌入到程序集中的 dnscrypt-proxy 和 appsettings 文件释放到磁盘
    /// </summary>
    static class ResourceExtractor
    {
        /// <summary>
        /// 释放所有需要的嵌入资源到磁盘（仅在文件不存在时释放）
        /// </summary>
        /// <param name="baseDirectory">基础目录</param>
        public static void ExtractIfNeeded(string baseDirectory)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();

            // 释放 dnscrypt-proxy 相关文件
            ExtractDnscryptProxy(assembly, resourceNames, baseDirectory);

            // 释放 appsettings 相关文件
            ExtractAppsettings(assembly, resourceNames, baseDirectory);
        }

        /// <summary>
        /// 释放 dnscrypt-proxy 二进制和配置文件
        /// </summary>
        private static void ExtractDnscryptProxy(Assembly assembly, string[] resourceNames, string baseDirectory)
        {
            var dnscryptDir = Path.Combine(baseDirectory, "dnscrypt-proxy");
            Directory.CreateDirectory(dnscryptDir);

            // 释放 dnscrypt-proxy 二进制（平台相关，只有一个会被嵌入）
            var binaryResourceName = OperatingSystem.IsWindows() ? "dnscrypt-proxy.exe" : "dnscrypt-proxy";
            var binaryTargetPath = Path.Combine(dnscryptDir, binaryResourceName);

            if (!File.Exists(binaryTargetPath) && resourceNames.Contains(binaryResourceName))
            {
                ExtractResource(assembly, binaryResourceName, binaryTargetPath);

                // Linux/macOS 需要设置可执行权限
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(binaryTargetPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // 某些平台可能不支持，忽略
                    }
                }
            }

            // 释放 dnscrypt-proxy.toml 配置
            var tomlTargetPath = Path.Combine(dnscryptDir, "dnscrypt-proxy.toml");
            if (!File.Exists(tomlTargetPath) && resourceNames.Contains("dnscrypt-proxy.toml"))
            {
                ExtractResource(assembly, "dnscrypt-proxy.toml", tomlTargetPath);
            }
        }

        /// <summary>
        /// 释放 appsettings 配置文件
        /// </summary>
        private static void ExtractAppsettings(Assembly assembly, string[] resourceNames, string baseDirectory)
        {
            // 释放 appsettings.json
            if (!File.Exists(Path.Combine(baseDirectory, "appsettings.json")) && resourceNames.Contains("appsettings.json"))
            {
                ExtractResource(assembly, "appsettings.json", Path.Combine(baseDirectory, "appsettings.json"));
            }

            // 释放 appsettings/*.json
            var appsettingsDir = Path.Combine(baseDirectory, "appsettings");
            Directory.CreateDirectory(appsettingsDir);

            var appsettingPrefixes = new[]
            {
                "appsettings.github", "appsettings.google", "appsettings.microsoft",
                "appsettings.packages", "appsettings.bootcss", "appsettings.fastly",
                "appsettings.imgur", "appsettings.v2ex", "appsettings.amazonaws"
            };

            foreach (var prefix in appsettingPrefixes)
            {
                var resourceName = $"{prefix}.json";
                var targetPath = Path.Combine(appsettingsDir, resourceName);

                if (!File.Exists(targetPath) && resourceNames.Contains(resourceName))
                {
                    ExtractResource(assembly, resourceName, targetPath);
                }
            }
        }

        /// <summary>
        /// 从程序集中释放单个资源到文件
        /// </summary>
        private static void ExtractResource(Assembly assembly, string resourceName, string targetPath)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            using var fileStream = File.Create(targetPath);
            stream.CopyTo(fileStream);
        }
    }
}
