using System.Collections.Concurrent;
using System.Text;

namespace SmartTourBackend.Services;

public class RequestMetrics
{
    private long _totalRequests;
    private long _totalErrors;
    private readonly ConcurrentDictionary<string, EndpointMetric> _endpointMetrics = new();

    public void Record(string endpoint, int statusCode, long elapsedMs)
    {
        Interlocked.Increment(ref _totalRequests);
        if (statusCode >= 500) Interlocked.Increment(ref _totalErrors);

        var metric = _endpointMetrics.GetOrAdd(endpoint, _ => new EndpointMetric());
        metric.Record(statusCode, elapsedMs);
    }

    public string ToOpenMetrics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# TYPE smarttour_requests_total counter");
        sb.AppendLine($"smarttour_requests_total {Interlocked.Read(ref _totalRequests)}");
        sb.AppendLine("# TYPE smarttour_requests_5xx_total counter");
        sb.AppendLine($"smarttour_requests_5xx_total {Interlocked.Read(ref _totalErrors)}");
        sb.AppendLine("# TYPE smarttour_endpoint_requests_total counter");
        sb.AppendLine("# TYPE smarttour_endpoint_latency_ms_avg gauge");

        foreach (var item in _endpointMetrics.OrderBy(x => x.Key))
        {
            var snapshot = item.Value.Snapshot();
            var safeEndpoint = item.Key.Replace("\"", "\\\"");
            sb.AppendLine($"smarttour_endpoint_requests_total{{endpoint=\"{safeEndpoint}\"}} {snapshot.RequestCount}");
            sb.AppendLine($"smarttour_endpoint_latency_ms_avg{{endpoint=\"{safeEndpoint}\"}} {snapshot.AvgLatencyMs:F2}");
            sb.AppendLine($"smarttour_endpoint_4xx_total{{endpoint=\"{safeEndpoint}\"}} {snapshot.ClientErrors}");
            sb.AppendLine($"smarttour_endpoint_5xx_total{{endpoint=\"{safeEndpoint}\"}} {snapshot.ServerErrors}");
        }

        return sb.ToString();
    }

    private sealed class EndpointMetric
    {
        private long _count;
        private long _sumLatencyMs;
        private long _clientErrors;
        private long _serverErrors;

        public void Record(int statusCode, long elapsedMs)
        {
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _sumLatencyMs, elapsedMs);
            if (statusCode is >= 400 and <= 499) Interlocked.Increment(ref _clientErrors);
            if (statusCode >= 500) Interlocked.Increment(ref _serverErrors);
        }

        public (long RequestCount, double AvgLatencyMs, long ClientErrors, long ServerErrors) Snapshot()
        {
            var count = Interlocked.Read(ref _count);
            var sum = Interlocked.Read(ref _sumLatencyMs);
            var avg = count == 0 ? 0 : (double)sum / count;
            return (count, avg, Interlocked.Read(ref _clientErrors), Interlocked.Read(ref _serverErrors));
        }
    }
}
