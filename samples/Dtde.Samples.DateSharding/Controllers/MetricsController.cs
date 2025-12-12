using Dtde.Samples.DateSharding.Data;
using Dtde.Samples.DateSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.DateSharding.Controllers;

/// <summary>
/// API controller demonstrating hourly-sharded metrics queries.
/// Hourly sharding is ideal for high-volume time-series data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly DateShardingDbContext _context;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        DateShardingDbContext context,
        ILogger<MetricsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get metrics for a specific hour (single shard, most efficient).
    /// </summary>
    [HttpGet("hour")]
    public async Task<ActionResult<IEnumerable<MetricDataPointDto>>> GetMetricsForHour(
        [FromQuery] DateTime timestamp,
        [FromQuery] string? source,
        [FromQuery] string? metricName,
        [FromQuery] int limit = 1000)
    {
        // Truncate to hour boundary
        var hourStart = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, DateTimeKind.Utc);
        var hourEnd = hourStart.AddHours(1);

        _logger.LogInformation("Querying metrics for hour starting at {HourStart} (single hourly shard)", hourStart);

        var query = _context.MetricDataPoints
            .Where(m => m.Timestamp >= hourStart && m.Timestamp < hourEnd);

        if (!string.IsNullOrEmpty(source))
            query = query.Where(m => m.Source == source);

        if (!string.IsNullOrEmpty(metricName))
            query = query.Where(m => m.MetricName == metricName);

        var metrics = await query
            .OrderBy(m => m.Timestamp)
            .Take(limit)
            .Select(m => new MetricDataPointDto
            {
                Id = m.Id,
                Timestamp = m.Timestamp,
                MetricName = m.MetricName,
                Value = m.Value,
                Source = m.Source ?? string.Empty,
                Tags = m.Tags ?? string.Empty
            })
            .ToListAsync();

        return Ok(metrics);
    }

    /// <summary>
    /// Get aggregated metrics over a time range.
    /// </summary>
    [HttpGet("aggregate")]
    public async Task<ActionResult<MetricAggregation>> GetAggregatedMetrics(
        [FromQuery] DateTime fromTime,
        [FromQuery] DateTime toTime,
        [FromQuery] string metricName,
        [FromQuery] string? source,
        [FromQuery] string granularity = "hour")
    {
        _logger.LogInformation(
            "Aggregating metrics {MetricName} from {From} to {To} with {Granularity} granularity",
            metricName, fromTime, toTime, granularity);

        var query = _context.MetricDataPoints
            .Where(m => m.Timestamp >= fromTime && m.Timestamp <= toTime)
            .Where(m => m.MetricName == metricName);

        if (!string.IsNullOrEmpty(source))
            query = query.Where(m => m.Source == source);

        var dataPoints = await query.ToListAsync();

        if (dataPoints.Count == 0)
        {
            return Ok(new MetricAggregation
            {
                MetricName = metricName,
                FromTime = fromTime,
                ToTime = toTime,
                DataPoints = []
            });
        }

        var groupedData = granularity.ToLower() switch
        {
            "minute" => dataPoints.GroupBy(m => new DateTime(
                m.Timestamp.Year, m.Timestamp.Month, m.Timestamp.Day,
                m.Timestamp.Hour, m.Timestamp.Minute, 0, DateTimeKind.Utc)),
            "hour" => dataPoints.GroupBy(m => new DateTime(
                m.Timestamp.Year, m.Timestamp.Month, m.Timestamp.Day,
                m.Timestamp.Hour, 0, 0, DateTimeKind.Utc)),
            "day" => dataPoints.GroupBy(m => m.Timestamp.Date),
            _ => dataPoints.GroupBy(m => new DateTime(
                m.Timestamp.Year, m.Timestamp.Month, m.Timestamp.Day,
                m.Timestamp.Hour, 0, 0, DateTimeKind.Utc))
        };

        var aggregatedPoints = groupedData
            .Select(g => new AggregatedDataPoint
            {
                Timestamp = g.Key,
                Min = g.Min(m => m.Value),
                Max = g.Max(m => m.Value),
                Avg = g.Average(m => m.Value),
                Sum = g.Sum(m => m.Value),
                Count = g.Count()
            })
            .OrderBy(p => p.Timestamp)
            .ToList();

        return Ok(new MetricAggregation
        {
            MetricName = metricName,
            FromTime = fromTime,
            ToTime = toTime,
            TotalPoints = dataPoints.Count,
            OverallMin = dataPoints.Min(m => m.Value),
            OverallMax = dataPoints.Max(m => m.Value),
            OverallAvg = dataPoints.Average(m => m.Value),
            DataPoints = aggregatedPoints
        });
    }

    /// <summary>
    /// Get latest metrics (queries recent hourly shards).
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<IEnumerable<LatestMetric>>> GetLatestMetrics(
        [FromQuery] int minutes = 5)
    {
        var since = DateTime.UtcNow.AddMinutes(-minutes);

        _logger.LogInformation("Getting latest metrics since {Since}", since);

        var latestMetrics = await _context.MetricDataPoints
            .Where(m => m.Timestamp >= since)
            .GroupBy(m => new { m.MetricName, m.Source })
            .Select(g => new LatestMetric
            {
                MetricName = g.Key.MetricName,
                Source = g.Key.Source ?? string.Empty,
                LatestValue = g.OrderByDescending(m => m.Timestamp).First().Value,
                LatestTimestamp = g.Max(m => m.Timestamp),
                PointsInWindow = g.Count()
            })
            .ToListAsync();

        return Ok(latestMetrics);
    }

    /// <summary>
    /// Batch ingest metrics (routes to appropriate hourly shards).
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<BatchIngestResult>> BatchIngestMetrics(BatchIngestRequest request)
    {
        if (request.DataPoints == null || request.DataPoints.Count == 0)
        {
            return BadRequest("No data points provided");
        }

        var dataPoints = request.DataPoints.Select(dp => new MetricDataPoint
        {
            Timestamp = dp.Timestamp ?? DateTime.UtcNow,
            MetricName = dp.MetricName,
            Value = dp.Value,
            Unit = dp.Unit,
            Source = dp.Source ?? "unknown",
            Tags = dp.Tags
        }).ToList();

        // Group by hour to show routing
        var hourGroups = dataPoints.GroupBy(dp =>
            new DateTime(dp.Timestamp.Year, dp.Timestamp.Month, dp.Timestamp.Day,
                         dp.Timestamp.Hour, 0, 0, DateTimeKind.Utc));

        _logger.LogInformation(
            "Ingesting {Count} metrics across {ShardCount} hourly shards",
            dataPoints.Count, hourGroups.Count());

        _context.MetricDataPoints.AddRange(dataPoints);
        await _context.SaveChangesAsync();

        return Ok(new BatchIngestResult
        {
            Ingested = dataPoints.Count,
            HourlyShards = hourGroups.Select(g => new ShardInfo
            {
                ShardHour = g.Key,
                PointCount = g.Count()
            }).ToList()
        });
    }

    /// <summary>
    /// Create a single metric data point.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MetricDataPointDto>> CreateMetric(CreateMetricRequest request)
    {
        var timestamp = request.Timestamp ?? DateTime.UtcNow;

        var metric = new MetricDataPoint
        {
            Timestamp = timestamp,
            MetricName = request.MetricName,
            Value = request.Value,
            Unit = request.Unit,
            Source = request.Source ?? "api",
            Tags = request.Tags
        };

        _context.MetricDataPoints.Add(metric);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetMetricsForHour),
            new { timestamp = timestamp },
            new MetricDataPointDto
            {
                Id = metric.Id,
                Timestamp = metric.Timestamp,
                MetricName = metric.MetricName,
                Value = metric.Value,
                Source = metric.Source,
                Tags = metric.Tags
            });
    }

    /// <summary>
    /// Get available metric names.
    /// </summary>
    [HttpGet("names")]
    public async Task<ActionResult<IEnumerable<string>>> GetMetricNames(
        [FromQuery] int hoursBack = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hoursBack);

        var names = await _context.MetricDataPoints
            .Where(m => m.Timestamp >= since)
            .Select(m => m.MetricName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        return Ok(names);
    }

    /// <summary>
    /// Get metric sources.
    /// </summary>
    [HttpGet("sources")]
    public async Task<ActionResult<IEnumerable<string>>> GetMetricSources(
        [FromQuery] int hoursBack = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hoursBack);

        var sources = await _context.MetricDataPoints
            .Where(m => m.Timestamp >= since)
            .Select(m => m.Source)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();

        return Ok(sources);
    }
}

// DTOs
public record MetricDataPointDto
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string MetricName { get; init; } = string.Empty;
    public double Value { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? Tags { get; init; }
}

public record CreateMetricRequest
{
    public required string MetricName { get; init; }
    public required double Value { get; init; }
    public string? Unit { get; init; }
    public string? Source { get; init; }
    public string? Tags { get; init; }
    public DateTime? Timestamp { get; init; }
}

public record BatchIngestRequest
{
    public required List<CreateMetricRequest> DataPoints { get; init; }
}

public record BatchIngestResult
{
    public int Ingested { get; init; }
    public List<ShardInfo> HourlyShards { get; init; } = [];
}

public record ShardInfo
{
    public DateTime ShardHour { get; init; }
    public int PointCount { get; init; }
}

public record MetricAggregation
{
    public string MetricName { get; init; } = string.Empty;
    public DateTime FromTime { get; init; }
    public DateTime ToTime { get; init; }
    public int TotalPoints { get; init; }
    public double? OverallMin { get; init; }
    public double? OverallMax { get; init; }
    public double? OverallAvg { get; init; }
    public List<AggregatedDataPoint> DataPoints { get; init; } = [];
}

public record AggregatedDataPoint
{
    public DateTime Timestamp { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Avg { get; init; }
    public double Sum { get; init; }
    public int Count { get; init; }
}

public record LatestMetric
{
    public string MetricName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public double LatestValue { get; init; }
    public DateTime LatestTimestamp { get; init; }
    public int PointsInWindow { get; init; }
}
