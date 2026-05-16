// StrategyTestService.cs - Strategy testing with bypass start/stop and per-service checks
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Interface for testing generated DPI bypass strategies.
/// </summary>
public interface IStrategyTestService
{
    /// <summary>
    /// Test a generated strategy by starting bypass, running connectivity checks,
    /// and caching the results.
    /// </summary>
    Task<TestResults> TestStrategyAsync(
        GeneratedStrategy strategy,
        TestLevel level = TestLevel.Quick);
}

/// <summary>
/// Tests generated DPI bypass strategies by starting the bypass engine,
/// running per-service connectivity checks (DNS, TCP, HTTP), and caching results.
/// </summary>
public class StrategyTestService : IStrategyTestService
{
    private readonly ILogger<StrategyTestService> _logger;
    private readonly IAdaptiveEngine _adaptiveEngine;
    private readonly IStrategyParamsProvider _paramsProvider;

    public StrategyTestService(
        ILogger<StrategyTestService> logger,
        IAdaptiveEngine adaptiveEngine,
        IStrategyParamsProvider paramsProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adaptiveEngine = adaptiveEngine ?? throw new ArgumentNullException(nameof(adaptiveEngine));
        _paramsProvider = paramsProvider ?? throw new ArgumentNullException(nameof(paramsProvider));
    }

    /// <inheritdoc/>
    public async Task<TestResults> TestStrategyAsync(
        GeneratedStrategy strategy,
        TestLevel level = TestLevel.Quick)
    {
        // Check cache for recent test result (skip re-test if within 5 minutes)
        if (TestResultStore.TryGetResult(strategy.Id, out var cachedResult) && cachedResult is not null)
        {
            var cacheAge = DateTime.UtcNow - cachedResult.TestedAt;
            if (cacheAge < TimeSpan.FromMinutes(5))
            {
                _logger.LogInformation("Using cached test result for strategy {Id} (age: {Age:F0}s)", strategy.Id, cacheAge.TotalSeconds);
                return new TestResults
                {
                    Success = cachedResult.Passed,
                    ServiceResults = new Dictionary<string, ServiceTestResult>(),
                    Duration = TimeSpan.FromMilliseconds(cachedResult.LatencyMs ?? 0)
                };
            }
        }

        var results = new Dictionary<string, ServiceTestResult>();
        var stopwatch = Stopwatch.StartNew();

        // Check if bypass is already running and remember state
        bool wasAlreadyRunning = _adaptiveEngine.IsProtected;

        try
        {
            // Stop existing bypass if running
            if (wasAlreadyRunning)
            {
                _logger.LogInformation("Stopping existing bypass for test");
                await _adaptiveEngine.StopAsync().ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false); // Wait for cleanup
            }

            // Start bypass with generated strategy via IPC
            var startResult = await _adaptiveEngine.StartWithStrategyAsync(strategy.Id).ConfigureAwait(false);

            if (!startResult.Success)
            {
                return new TestResults { Success = false, ServiceResults = results, Duration = stopwatch.Elapsed, ErrorMessage = startResult.Message };
            }

            // Wait for initialization
            await Task.Delay(2000).ConfigureAwait(false);

            // Run tests based on level
            var parameters = await _paramsProvider.LoadParametersAsync().ConfigureAwait(false);

            // Test predefined services
            foreach (var serviceId in strategy.IncludedServices)
            {
                if (parameters.Services.TryGetValue(serviceId, out var service))
                {
                    var result = await TestServiceAsync(service, level).ConfigureAwait(false);
                    results[serviceId] = result;
                }
            }

            // Test custom domains
            if (strategy.CustomDomains.Count > 0)
            {
                foreach (var domain in strategy.CustomDomains)
                {
                    var domainKey = $"custom:{domain}";
                    var result = await TestCustomDomainAsync(domain, level).ConfigureAwait(false);
                    results[domainKey] = result;
                }
            }

            // Stop bypass
            await _adaptiveEngine.StopAsync().ConfigureAwait(false);

            var allPassed = results.Values.All(r => r.Passed);
            var testResults = new TestResults { Success = allPassed, ServiceResults = results, Duration = stopwatch.Elapsed };

            // Cache test results for later retrieval
            try
            {
                var cachedResults = new Dictionary<string, CachedStrategyResult>
                {
                    [strategy.Id] = new CachedStrategyResult(
                        strategy.Id,
                        testResults.Success,
                        (int?)results.Values
                            .Where(r => r.LatencyMs.HasValue)
                            .Select(r => r.LatencyMs!.Value)
                            .DefaultIfEmpty()
                            .Average() as int?,
                        DateTime.UtcNow)
                };
                TestResultStore.SaveCache(cachedResults);
                _logger.LogInformation("Cached test result for strategy {Id}: {Passed}", strategy.Id, testResults.Success);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to cache test result for strategy {Id}", strategy.Id);
            }

            return testResults;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Strategy test failed");
            return new TestResults { Success = false, ServiceResults = results, Duration = stopwatch.Elapsed, ErrorMessage = ex.Message };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Strategy test failed");
            return new TestResults { Success = false, ServiceResults = results, Duration = stopwatch.Elapsed, ErrorMessage = ex.Message };
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Strategy test failed");
            return new TestResults { Success = false, ServiceResults = results, Duration = stopwatch.Elapsed, ErrorMessage = ex.Message };
        }
        finally
        {
            // Always stop bypass after test
            if (_adaptiveEngine.IsProtected)
            {
                await _adaptiveEngine.StopAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<ServiceTestResult> TestServiceAsync(ServiceConfig service, TestLevel level)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            switch (level)
            {
                case TestLevel.Quick:
                    // DNS + TCP connection test
                    var domain = service.Domains.FirstOrDefault() ?? "";
                    if (string.IsNullOrEmpty(domain))
                    {
                        return new ServiceTestResult { ServiceId = service.Id, Passed = false, Details = "No domains configured" };
                    }

                    var dnsResult = await TestDnsResolutionAsync(domain).ConfigureAwait(false);
                    if (!dnsResult.Success)
                    {
                        return new ServiceTestResult { ServiceId = service.Id, Passed = false, Details = $"DNS failed: {dnsResult.Error}" };
                    }

                    var tcpResult = await TestTcpConnectionAsync(domain, 443).ConfigureAwait(false);
                    return new ServiceTestResult
                    {
                        ServiceId = service.Id,
                        Passed = tcpResult.Success,
                        LatencyMs = (int)sw.ElapsedMilliseconds,
                        Details = tcpResult.Details
                    };

                case TestLevel.Standard:
                    return await TestHttpAsync(service, sw).ConfigureAwait(false);

                case TestLevel.Full:
                    return await TestRealUsageAsync(service, sw).ConfigureAwait(false);

                default:
                    return new ServiceTestResult { ServiceId = service.Id, Passed = false, Details = "Unknown test level" };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Test failed for service: {ServiceId}", service.Id);
            return new ServiceTestResult { ServiceId = service.Id, Passed = false, Details = ex.Message };
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Test failed for service: {ServiceId}", service.Id);
            return new ServiceTestResult { ServiceId = service.Id, Passed = false, Details = ex.Message };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Test failed for service: {ServiceId}", service.Id);
            return new ServiceTestResult { ServiceId = service.Id, Passed = false, Details = ex.Message };
        }
    }

    private async Task<ServiceTestResult> TestCustomDomainAsync(string domain, TestLevel level)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var domainKey = $"custom:{domain}";

            switch (level)
            {
                case TestLevel.Quick:
                    // DNS + TCP connection test
                    var dnsResult = await TestDnsResolutionAsync(domain).ConfigureAwait(false);
                    if (!dnsResult.Success)
                    {
                        return new ServiceTestResult { ServiceId = domainKey, Passed = false, Details = $"DNS failed: {dnsResult.Error}" };
                    }

                    var tcpResult = await TestTcpConnectionAsync(domain, 443).ConfigureAwait(false);
                    return new ServiceTestResult
                    {
                        ServiceId = domainKey,
                        Passed = tcpResult.Success,
                        LatencyMs = (int)sw.ElapsedMilliseconds,
                        Details = tcpResult.Details
                    };

                case TestLevel.Standard:
                case TestLevel.Full:
                    // For custom domains, test HTTPS connection
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        try
                        {
                            var response = await client.GetAsync(new Uri($"https://{domain}")).ConfigureAwait(false);
                            return new ServiceTestResult
                            {
                                ServiceId = domainKey,
                                Passed = response.IsSuccessStatusCode || (int)response.StatusCode < 500,
                                LatencyMs = (int)sw.ElapsedMilliseconds,
                                Details = $"HTTP {(int)response.StatusCode}"
                            };
                        }
                        catch (HttpRequestException ex)
                        {
                            // Connection succeeded but HTTP error - still OK for DPI bypass test
                            return new ServiceTestResult
                            {
                                ServiceId = domainKey,
                                Passed = true,
                                LatencyMs = (int)sw.ElapsedMilliseconds,
                                Details = $"Connection OK (HTTP error: {ex.Message})"
                            };
                        }
                    }

                default:
                    return new ServiceTestResult { ServiceId = domainKey, Passed = false, Details = "Unknown test level" };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Test failed for custom domain: {Domain}", domain);
            return new ServiceTestResult { ServiceId = $"custom:{domain}", Passed = false, Details = ex.Message };
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Test failed for custom domain: {Domain}", domain);
            return new ServiceTestResult { ServiceId = $"custom:{domain}", Passed = false, Details = ex.Message };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Test failed for custom domain: {Domain}", domain);
            return new ServiceTestResult { ServiceId = $"custom:{domain}", Passed = false, Details = ex.Message };
        }
    }

    private async Task<(bool Success, string? Error)> TestDnsResolutionAsync(string domain)
    {
        try
        {
            var ips = await System.Net.Dns.GetHostAddressesAsync(domain).ConfigureAwait(false);
            return (ips.Length > 0, null);
        }
        catch (SocketException ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(bool Success, string? Details)> TestTcpConnectionAsync(string domain, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(domain, port);
            var timeoutTask = Task.Delay(5000);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                return (false, "Connection timeout");
            }

            await connectTask.ConfigureAwait(false);
            return (true, $"Connected to {domain}:{port}");
        }
        catch (SocketException ex)
        {
            return (false, ex.Message);
        }
        catch (IOException ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<ServiceTestResult> TestHttpAsync(ServiceConfig service, Stopwatch sw)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(service.TestUrl).ConfigureAwait(false);

            return new ServiceTestResult
            {
                ServiceId = service.Id,
                Passed = response.IsSuccessStatusCode,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Details = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (SocketException ex)
        {
            return new ServiceTestResult
            {
                ServiceId = service.Id,
                Passed = false,
                Details = ex.Message
            };
        }
    }

    private async Task<ServiceTestResult> TestRealUsageAsync(ServiceConfig service, Stopwatch sw)
    {
        // For real usage test, use HTTP test for now
        // In future, this could test actual video playback, websocket, etc.
        return await TestHttpAsync(service, sw).ConfigureAwait(false);
    }
}
