﻿﻿﻿using FastGithub.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 域名持久化
    /// </summary>
    sealed partial class PersistenceService
    {
        private static readonly string dataFile = "dnsendpoints.json";
        private static readonly SemaphoreSlim dataLocker = new(1, 1); 

        private readonly FastGithubConfig fastGithubConfig;
        private readonly ILogger<PersistenceService> logger;


        private record EndPointItem(string Host, int Port, string[]? Addresses);

        [JsonSerializable(typeof(EndPointItem[]))]
        [JsonSourceGenerationOptions(
            WriteIndented = true,
            PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        private partial class EndPointItemsContext : JsonSerializerContext
        {
        }


        /// <summary>
        /// 域名持久化
        /// </summary> 
        /// <param name="fastGithubConfig"></param>
        /// <param name="logger"></param>
        public PersistenceService(
            FastGithubConfig fastGithubConfig,
            ILogger<PersistenceService> logger)
        {
            this.fastGithubConfig = fastGithubConfig;
            this.logger = logger;
        }


        /// <summary>
        /// 读取保存的节点及其IP地址
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IDictionary<DnsEndPoint, IPAddress[]>> ReadDnsEndPointsAsync(CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<DnsEndPoint, IPAddress[]>();

            if (File.Exists(dataFile) == false)
            {
                return result;
            }

            try
            {
                await dataLocker.WaitAsync(cancellationToken);

                var utf8Json = await File.ReadAllBytesAsync(dataFile, cancellationToken);
                var endPointItems = JsonSerializer.Deserialize(utf8Json, EndPointItemsContext.Default.EndPointItemArray);
                if (endPointItems == null)
                {
                    return result;
                }

                foreach (var item in endPointItems)
                {
                    if (this.fastGithubConfig.IsMatch(item.Host) == true)
                    {
                        var endPoint = new DnsEndPoint(item.Host, item.Port);
                        var addresses = Array.Empty<IPAddress>();
                        if (item.Addresses != null)
                        {
                            addresses = item.Addresses
                                .Select(a => IPAddress.TryParse(a, out var ip) ? ip : null)
                                .Where(a => a != null)
                                .Cast<IPAddress>()
                                .ToArray();
                        }
                        result[endPoint] = addresses;
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "读取dns记录异常");
                return result;
            }
            finally
            {
                dataLocker.Release();
            }
        }

        /// <summary>
        /// 保存节点及其IP地址到文件
        /// </summary>
        /// <param name="dnsEndPointAddresses"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task WriteDnsEndPointsAsync(IEnumerable<KeyValuePair<DnsEndPoint, IPAddress[]>> dnsEndPointAddresses, CancellationToken cancellationToken)
        {
            try
            {
                await dataLocker.WaitAsync(CancellationToken.None);

                var endPointItems = dnsEndPointAddresses.Select(item => new EndPointItem(
                    item.Key.Host,
                    item.Key.Port,
                    item.Value.Select(a => a.ToString()).ToArray()
                )).ToArray();
                var utf8Json = JsonSerializer.SerializeToUtf8Bytes(endPointItems, EndPointItemsContext.Default.EndPointItemArray);
                await File.WriteAllBytesAsync(dataFile, utf8Json, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "保存dns记录异常");
            }
            finally
            {
                dataLocker.Release();
            }
        }
    }
}
