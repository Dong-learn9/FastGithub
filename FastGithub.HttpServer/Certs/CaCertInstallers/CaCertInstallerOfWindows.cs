using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography.X509Certificates;

namespace FastGithub.HttpServer.Certs.CaCertInstallers
{
    sealed class CaCertInstallerOfWindows : ICaCertInstaller
    {
        private readonly ILogger<CaCertInstallerOfWindows> logger;

        public CaCertInstallerOfWindows(ILogger<CaCertInstallerOfWindows> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// 是否支持
        /// </summary>
        /// <returns></returns>
        public bool IsSupported()
        {
            return OperatingSystem.IsWindows();
        }

        /// <summary>
        /// 安装ca证书
        /// </summary>
        /// <param name="caCertFilePath">证书文件路径</param>
        public void Install(string caCertFilePath)
        {
            var caCert = new X509Certificate2(caCertFilePath);
            var subjectName = caCert.Subject[3..];

            // 优先安装到 LocalMachine（需要管理员权限，对所有用户生效）
            if (TryInstallToStore(caCert, subjectName, StoreLocation.LocalMachine))
            {
                return;
            }

            // 回退安装到 CurrentUser（不需要管理员权限，仅当前用户生效）
            if (TryInstallToStore(caCert, subjectName, StoreLocation.CurrentUser))
            {
                this.logger.LogWarning($"由于没有管理员权限，CA证书已安装到当前用户存储。建议以管理员身份运行以安装到系统级存储。");
                return;
            }

            this.logger.LogWarning($"请手动安装CA证书{caCertFilePath}到「受信任的根证书颁发机构」");
        }

        /// <summary>
        /// 尝试安装证书到指定存储位置
        /// </summary>
        private bool TryInstallToStore(X509Certificate2 caCert, string subjectName, StoreLocation storeLocation)
        {
            try
            {
                using var store = new X509Store(StoreName.Root, storeLocation);
                store.Open(OpenFlags.ReadWrite);

                foreach (var item in store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false))
                {
                    if (item.Thumbprint != caCert.Thumbprint)
                    {
                        store.Remove(item);
                    }
                }
                if (store.Certificates.Find(X509FindType.FindByThumbprint, caCert.Thumbprint, true).Count == 0)
                {
                    store.Add(caCert);
                    this.logger.LogInformation($"CA证书已安装到{storeLocation}\\Root存储");
                }
                else
                {
                    this.logger.LogInformation($"CA证书已存在于{storeLocation}\\Root存储");
                }
                store.Close();
                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, $"安装CA证书到{storeLocation}\\Root失败");
                return false;
            }
        }
    }
}
