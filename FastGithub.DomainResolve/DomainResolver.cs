using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 域名解析器
    /// </summary> 
    sealed class DomainResolver : IDomainResolver
    {
        private const int MAX_IP_COUNT = 3;
        private readonly DnsClient dnsClient;
        private readonly PersistenceService persistence;
        private readonly IPAddressService addressService;
        private readonly ILogger<DomainResolver> logger;
        private readonly ConcurrentDictionary<DnsEndPoint, IPAddress[]> dnsEndPointAddress = new();

        /// <summary>
        /// 域名解析器
        /// </summary>
        /// <param name="dnsClient"></param>
        /// <param name="persistence"></param>
        /// <param name="addressService"></param>
        /// <param name="logger"></param>
        public DomainResolver(
            DnsClient dnsClient,
            PersistenceService persistence,
            IPAddressService addressService,
            ILogger<DomainResolver> logger)
        {
            this.dnsClient = dnsClient;
            this.persistence = persistence;
            this.addressService = addressService;
            this.logger = logger;

            // 从磁盘恢复域名及其上次的IP地址缓存
            // 使用 Task.Run 避免在构造函数中同步阻塞异步方法
            var cachedEndPoints = Task.Run(() => this.persistence.ReadDnsEndPointsAsync()).GetAwaiter().GetResult();
            foreach (var kv in cachedEndPoints)
            {
                this.dnsEndPointAddress.TryAdd(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// 解析域名
        /// </summary>
        /// <param name="endPoint">节点</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<IPAddress> ResolveAsync(DnsEndPoint endPoint, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (this.dnsEndPointAddress.TryGetValue(endPoint, out var addresses) && addresses.Length > 0)
            {
                foreach (var address in addresses)
                {
                    yield return address;
                }
            }
            else
            {
                if (this.dnsEndPointAddress.TryAdd(endPoint, Array.Empty<IPAddress>()))
                {
                    await this.persistence.WriteDnsEndPointsAsync(this.dnsEndPointAddress, cancellationToken);
                }

                await foreach (var address in this.dnsClient.ResolveAsync(endPoint, fastSort: true, cancellationToken))
                {
                    yield return address;
                }
            }
        }

        /// <summary>
        /// 对所有节点进行测速
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task TestSpeedAsync(CancellationToken cancellationToken)
        {
            // 并行测速：所有域名同时测速，而非串行等待
            var items = this.dnsEndPointAddress.OrderBy(item => item.Value.Length).ToList();
            var tasks = items.Select(async keyValue =>
            {
                var dnsEndPoint = keyValue.Key;
                var oldAddresses = keyValue.Value;

                var newAddresses = await this.addressService.GetAddressesAsync(dnsEndPoint, oldAddresses, cancellationToken);
                this.dnsEndPointAddress[dnsEndPoint] = newAddresses;

                var oldSegmentums = oldAddresses.Take(MAX_IP_COUNT);
                var newSegmentums = newAddresses.Take(MAX_IP_COUNT);
                if (oldSegmentums.SequenceEqual(newSegmentums) == false)
                {
                    var addressArray = string.Join(", ", newSegmentums.Select(item => item.ToString()));
                    this.logger.LogInformation($"{dnsEndPoint.Host}:{dnsEndPoint.Port}->[{addressArray}]");
                }
            });

            await Task.WhenAll(tasks);

            // 测速完成后持久化IP地址到磁盘
            await this.persistence.WriteDnsEndPointsAsync(this.dnsEndPointAddress, cancellationToken);
        }
    }
}
