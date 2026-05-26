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
        /// 释放所有需要的嵌入资源到磁盘
        /// dnscrypt-proxy 二进制：仅在不存在时释放（避免覆盖用户自定义配置）
        /// appsettings 配置文件：始终释放（确保版本更新后配置同步）
        /// </summary>
        /// <param name="baseDirectory">基础目录</param>
        public static void ExtractIfNeeded(string baseDirectory)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();

            if (resourceNames.Length == 0)
            {
                return;
            }

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

            if (!File.Exists(binaryTargetPath))
            {
                var fullResourceName = FindResourceName(resourceNames, binaryResourceName);
                if (fullResourceName != null)
                {
                    ExtractResource(assembly, fullResourceName, binaryTargetPath);

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
            }

            // 释放 dnscrypt-proxy.toml 配置（仅在不存在时释放，保留用户自定义修改）
            var tomlTargetPath = Path.Combine(dnscryptDir, "dnscrypt-proxy.toml");
            if (!File.Exists(tomlTargetPath))
            {
                var fullResourceName = FindResourceName(resourceNames, "dnscrypt-proxy.toml");
                if (fullResourceName != null)
                {
                    ExtractResource(assembly, fullResourceName, tomlTargetPath);
                }
            }
        }

        /// <summary>
        /// 释放 appsettings 配置文件
        /// 仅在不存在时释放，保留用户自定义修改
        /// </summary>
        private static void ExtractAppsettings(Assembly assembly, string[] resourceNames, string baseDirectory)
        {
            // 释放 appsettings.json（仅在不存在时释放）
            var appsettingsPath = Path.Combine(baseDirectory, "appsettings.json");
            if (!File.Exists(appsettingsPath))
            {
                var appsettingsResourceName = FindResourceName(resourceNames, "appsettings.json");
                if (appsettingsResourceName != null)
                {
                    ExtractResource(assembly, appsettingsResourceName, appsettingsPath);
                }
            }

            // 释放 appsettings/*.json（仅在不存在时释放）
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
                var fileName = $"{prefix}.json";
                var targetPath = Path.Combine(appsettingsDir, fileName);
                if (!File.Exists(targetPath))
                {
                    var fullResourceName = FindResourceName(resourceNames, fileName);
                    if (fullResourceName != null)
                    {
                        ExtractResource(assembly, fullResourceName, targetPath);
                    }
                }
            }
        }

        /// <summary>
        /// 在资源名称列表中查找匹配的资源全名
        /// 嵌入资源的完整名称格式为 "命名空间.文件名" 或 "命名空间.子目录.文件名"
        /// </summary>
        private static string? FindResourceName(string[] resourceNames, string logicalName)
        {
            // 精确匹配（LogicalName 就是完整名称）
            var exactMatch = resourceNames.FirstOrDefault(r => r == logicalName);
            if (exactMatch != null) return exactMatch;

            // 后缀匹配（名称以 logicalName 结尾，前面可能有命名空间前缀）
            var suffixMatch = resourceNames.FirstOrDefault(r => r.EndsWith("." + logicalName));
            return suffixMatch;
        }

        /// <summary>
        /// 从程序集中释放单个资源到文件
        /// </summary>
        private static void ExtractResource(Assembly assembly, string fullResourceName, string targetPath)
        {
            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null) return;

            using var fileStream = File.Create(targetPath);
            stream.CopyTo(fileStream);
        }
    }
}
