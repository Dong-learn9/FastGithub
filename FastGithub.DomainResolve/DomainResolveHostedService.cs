﻿﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 域名解析后台服务
    /// </summary>
    sealed class DomainResolveHostedService : BackgroundService
    {
        private readonly DnscryptProxy dnscryptProxy;
        private readonly IDomainResolver domainResolver;
        private readonly ILogger<DomainResolveHostedService> logger;
        private readonly TimeSpan dnscryptProxyMaxDelay = TimeSpan.FromSeconds(5d);
        private readonly TimeSpan testPeriodTimeSpan = TimeSpan.FromSeconds(1d);

        /// <summary>
        /// 域名解析后台服务
        /// </summary>
        /// <param name="dnscryptProxy"></param>
        /// <param name="domainResolver"></param>
        public DomainResolveHostedService(
            DnscryptProxy dnscryptProxy,
            IDomainResolver domainResolver,
            ILogger<DomainResolveHostedService> logger)
        {
            this.dnscryptProxy = dnscryptProxy;
            this.domainResolver = domainResolver;
            this.logger = logger;
        }

        /// <summary>
        /// 后台任务
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await this.dnscryptProxy.StartAsync(stoppingToken);
                await this.WaitForDnscryptProxyAsync(stoppingToken);

                while (stoppingToken.IsCancellationRequested == false)
                {
                    await this.domainResolver.TestSpeedAsync(stoppingToken);
                    await Task.Delay(this.testPeriodTimeSpan, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "域名解析异常");
            }
        }

        /// <summary>
        /// 动态等待dnscrypt-proxy就绪，替代固定5秒延迟
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task WaitForDnscryptProxyAsync(CancellationToken cancellationToken)
        {
            var endPoint = this.dnscryptProxy.LocalEndPoint;
            if (endPoint == null)
            {
                return;
            }

            using var timeoutTokenSource = new CancellationTokenSource(this.dnscryptProxyMaxDelay);
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);

            while (linkedTokenSource.IsCancellationRequested == false)
            {
                try
                {
                    using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(endPoint, linkedTokenSource.Token);
                    this.logger.LogInformation("dnscrypt-proxy已就绪");
                    return;
                }
                catch
                {
                    await Task.Delay(200, linkedTokenSource.Token);
                }
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            this.dnscryptProxy.Stop();
            return base.StopAsync(cancellationToken);
        }
    }
}
