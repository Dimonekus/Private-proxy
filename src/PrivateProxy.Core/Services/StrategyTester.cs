using System.Diagnostics;
using System.Net;
using System.Text.Json;
using PrivateProxy.Core.Models;

namespace PrivateProxy.Core.Services;

public sealed record TestProgress(int Index, int Total, string StrategyName, string Phase);

/// <summary>
/// Starts each strategy in turn, waits for the engine to settle and probes a set
/// of endpoints. Any HTTP response means the TLS handshake survived, which is
/// exactly what we want to measure.
/// </summary>
public sealed class StrategyTester
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly TargetEndpoint[] FallbackTargets =
    [
        new() { Name = "YouTube", Url = "https://www.youtube.com/generate_204" },
        new() { Name = "Discord", Url = "https://discord.com" },
        new() { Name = "Google", Url = "https://www.google.com/generate_204" },
        new() { Name = "Googlevideo", Url = "https://redirector.googlevideo.com/generate_204" }
    ];

    public async Task<IReadOnlyList<StrategyTestResult>> TestAllAsync(
        IReadOnlyList<Strategy> strategies,
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Elevation.IsAdministrator)
        {
            return strategies
                .Select(s => new StrategyTestResult
                {
                    StrategyId = s.Id,
                    StrategyName = s.Name,
                    Started = false,
                    Error = "нужны права администратора"
                })
                .ToArray();
        }

        var targets = LoadTargets();
        var results = new List<StrategyTestResult>();

        for (var i = 0; i < strategies.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var strategy = strategies[i];
            progress?.Report(new TestProgress(i + 1, strategies.Count, strategy.Name, "запуск"));

            var result = await TestSingleAsync(strategy, targets, cancellationToken);
            results.Add(result);

            progress?.Report(new TestProgress(i + 1, strategies.Count, strategy.Name, result.Summary));
            Log.Info($"Тест «{strategy.Name}»: {result.Summary}");
        }

        return results;
    }

    private static async Task<StrategyTestResult> TestSingleAsync(
        Strategy strategy,
        IReadOnlyList<TargetEndpoint> targets,
        CancellationToken cancellationToken)
    {
        var exe = Path.Combine(AppPaths.EngineDir, "winws.exe");
        if (!File.Exists(exe))
        {
            return new StrategyTestResult
            {
                StrategyId = strategy.Id,
                StrategyName = strategy.Name,
                Started = false,
                Error = "движок не установлен"
            };
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = AppPaths.EngineDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var arg in WinwsRunner.ExpandArguments(strategy))
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Delay(2500, cancellationToken);
            if (process.HasExited)
            {
                return new StrategyTestResult
                {
                    StrategyId = strategy.Id,
                    StrategyName = strategy.Name,
                    Started = false,
                    Error = $"winws завершился с кодом {process.ExitCode}"
                };
            }

            var probes = targets.Select(t => ProbeAsync(t, cancellationToken)).ToArray();
            var probeResults = await Task.WhenAll(probes);

            var succeeded = probeResults.Count(r => r.Ok);
            var latencies = probeResults.Where(r => r.Ok).Select(r => r.LatencyMs).ToArray();

            return new StrategyTestResult
            {
                StrategyId = strategy.Id,
                StrategyName = strategy.Name,
                Started = true,
                Succeeded = succeeded,
                Total = probeResults.Length,
                AverageLatencyMs = latencies.Length == 0 ? 0 : latencies.Average(),
                FailedTargets = probeResults.Where(r => !r.Ok).Select(r => r.Name).ToList()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StrategyTestResult
            {
                StrategyId = strategy.Id,
                StrategyName = strategy.Name,
                Started = false,
                Error = ex.Message
            };
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                // ignored
            }

            // Let the driver release its handle before the next strategy starts.
            await Task.Delay(700, CancellationToken.None);
        }
    }

    private static async Task<ProbeResult> ProbeAsync(TargetEndpoint target, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(6),
                PooledConnectionLifetime = TimeSpan.Zero,
                AutomaticDecompression = DecompressionMethods.None
            };

            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            request.Headers.ConnectionClose = true;
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            sw.Stop();
            return new ProbeResult(target.Name, true, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception)
        {
            sw.Stop();
            return new ProbeResult(target.Name, false, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static IReadOnlyList<TargetEndpoint> LoadTargets()
    {
        var path = Path.Combine(AppPaths.DataDir, "targets.json");
        try
        {
            if (!File.Exists(path))
            {
                return FallbackTargets;
            }

            var list = JsonSerializer.Deserialize<TargetList>(File.ReadAllText(path), JsonOptions);
            var targets = list?.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Url)).ToArray();
            return targets is { Length: > 0 } ? targets : FallbackTargets;
        }
        catch (Exception ex)
        {
            Log.Warn($"Список целей не прочитан: {ex.Message}");
            return FallbackTargets;
        }
    }

    private readonly record struct ProbeResult(string Name, bool Ok, double LatencyMs);
}
