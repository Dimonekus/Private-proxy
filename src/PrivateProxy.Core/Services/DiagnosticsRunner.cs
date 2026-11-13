using System.Diagnostics;
using System.Net;
using System.Text.Json;
using PrivateProxy.Core.Models;

namespace PrivateProxy.Core.Services;

/// <summary>
/// Runs a battery of checks so the user (and we) can see at a glance whether the
/// engine, reachability of each service and raw throughput are healthy. It does
/// not require admin; it reflects whatever the engine is currently doing.
/// </summary>
public sealed class DiagnosticsRunner
{
    private const long ThroughputBytes = 20_000_000; // 20 MB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(EngineStatus engine, CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosticResult>();

        results.Add(engine.Present && engine.Verified
            ? new DiagnosticResult { Name = "Движок", Status = DiagnosticStatus.Ok, Detail = engine.Summary }
            : new DiagnosticResult { Name = "Движок", Status = DiagnosticStatus.Fail, Detail = engine.Summary });

        foreach (var target in LoadTargets())
        {
            var (ok, detail) = await ProbeAsync(target.Url, cancellationToken);
            results.Add(new DiagnosticResult
            {
                Name = target.Name,
                Status = ok ? DiagnosticStatus.Ok : DiagnosticStatus.Fail,
                Detail = detail
            });
        }

        var (speedStatus, speedDetail) = await ThroughputAsync(cancellationToken);
        results.Add(new DiagnosticResult { Name = "Скорость (Cloudflare)", Status = speedStatus, Detail = speedDetail });

        return results;
    }

    private static async Task<(bool Ok, string Detail)> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AutomaticDecompression = DecompressionMethods.None
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();

            var ok = (int)response.StatusCode < 400;
            return (ok, $"{(int)response.StatusCode} · {sw.Elapsed.TotalMilliseconds:0} мс");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return (false, "таймаут");
        }
        catch (Exception)
        {
            sw.Stop();
            return (false, "нет соединения");
        }
    }

    private static async Task<(DiagnosticStatus Status, string Detail)> ThroughputAsync(CancellationToken cancellationToken)
    {
        var url = "https://speed.cloudflare.com/__down?bytes=" + ThroughputBytes;
        var sw = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(40));

        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[65536];
            long total = 0;
            int read;
            while (total < ThroughputBytes && (read = await stream.ReadAsync(buffer, cts.Token)) > 0)
            {
                total += read;
            }

            sw.Stop();
            var mbps = total * 8.0 / sw.Elapsed.TotalSeconds / 1_000_000.0;

            var status = mbps >= 20
                ? DiagnosticStatus.Ok
                : mbps >= 8
                    ? DiagnosticStatus.Warn
                    : DiagnosticStatus.Fail;

            return (status, $"{mbps:0.0} Мбит/с ({total / 1_000_000} МБ)");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return (DiagnosticStatus.Fail, "таймаут");
        }
        catch (Exception)
        {
            sw.Stop();
            return (DiagnosticStatus.Fail, "не удалось измерить");
        }
    }

    private static IReadOnlyList<TargetEndpoint> LoadTargets()
    {
        var path = Path.Combine(AppPaths.DataDir, "targets.json");
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var list = JsonSerializer.Deserialize<TargetList>(File.ReadAllText(path), JsonOptions);
            return list?.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Url)).ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
