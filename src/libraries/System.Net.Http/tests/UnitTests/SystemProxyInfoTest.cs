// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace System.Net.Http.Tests
{
    public class SystemProxyInfoTest
    {
        // This will clean specific environmental variables
        // to be sure they do not interfere with the test.
        private void CleanEnv()
        {
            var envVars = new List<string> { "http_proxy", "HTTP_PROXY",
                                             "https_proxy", "HTTPS_PROXY",
                                             "all_proxy", "ALL_PROXY",
                                             "no_proxy", "NO_PROXY",
                                             "GATEWAY_INTERFACE" };

            foreach (string v in envVars)
            {
                Environment.SetEnvironmentVariable(v, null);
            }
        }

        public SystemProxyInfoTest()
        {
            CleanEnv();
        }

        [ConditionalFact(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
        public async Task Ctor_NoEnvironmentVariables_NotHttpEnvironmentProxy()
        {
            await RemoteExecutor.Invoke(() =>
            {
                IWebProxy proxy = SystemProxyInfo.ConstructSystemProxy();
                Assert.NotNull(proxy);

                HttpEnvironmentProxy envProxy = proxy as HttpEnvironmentProxy;
                Assert.Null(envProxy);
            }).DisposeAsync();
        }

        [ConditionalFact(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
        public async Task Ctor_ProxyEnvironmentVariableSet_IsHttpEnvironmentProxy()
        {
            var options = new RemoteInvokeOptions();
            options.StartInfo.EnvironmentVariables.Add("http_proxy", "http://proxy.contoso.com");
            await RemoteExecutor.Invoke(() =>
            {
                IWebProxy proxy = SystemProxyInfo.ConstructSystemProxy();
                HttpEnvironmentProxy envProxy = proxy as HttpEnvironmentProxy;
                Assert.NotNull(envProxy);
            }, options).DisposeAsync();
        }
    }
}
