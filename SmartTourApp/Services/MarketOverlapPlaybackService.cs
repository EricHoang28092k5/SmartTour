using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

/// <summary>
/// Khu chợ: nhiều POI chồng bán kính — hàng đợi ưu tiên (Premium → heat → gần),
/// vuốt trái để skip tạm thời, ghim để chỉ auto-play một quán; ra khỏi bán kính ghim thì dừng.
/// </summary>
public sealed class MarketOverlapPlaybackService
{
    private readonly GeofencingEngine _geo;
    private readonly object _lock = new();

    private readonly HashSet<int> _skipped = new();
    private int? _pinnedPoiId;
    private int? _lastDispatchedPoiId;

    /// <summary>Ngưng auto geofence khi GPS quá kém (m).</summary>
    public const double MaxHorizontalAccuracyMeters = 120.0;

    public MarketOverlapPlaybackService(GeofencingEngine geo)
    {
        _geo = geo;
    }

    public int? PinnedPoiId
    {
        get
        {
            lock (_lock) return _pinnedPoiId;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _skipped.Clear();
            _pinnedPoiId = null;
            _lastDispatchedPoiId = null;
        }
    }

    public void SetPinnedPoi(int? poiId)
    {
        lock (_lock)
        {
            _pinnedPoiId = poiId;
            _lastDispatchedPoiId = null;
        }
    }

    public void TogglePinForPoi(int poiId)
    {
        lock (_lock)
        {
            if (_pinnedPoiId == poiId)
                _pinnedPoiId = null;
            else
                _pinnedPoiId = poiId;
            _lastDispatchedPoiId = null;
        }
    }

    /// <summary>Skip POI hiện tại (vuốt trái); cho phép quán kế trong hàng được xét.</summary>
    public void SkipPoiForAutoPlay(int poiId)
    {
        lock (_lock)
        {
            _skipped.Add(poiId);
            if (_lastDispatchedPoiId == poiId)
                _lastDispatchedPoiId = null;
        }

        _geo.ReleaseActiveZone(poiId);
    }

    /// <returns>POI cần gọi NarrationEngine.Play; StopNarration=true khi vừa hết vùng ghim.</returns>
    public (Poi? ToPlay, bool StopNarration) Evaluate(Location loc, List<Poi> pois)
    {
        if (loc.Accuracy is double acc && acc > MaxHorizontalAccuracyMeters)
            return (null, false);

        lock (_lock)
        {
            var ordered = _geo.GetOrderedOverlappingPois(loc, pois);
            var inZone = ordered.Select(p => p.Id).ToHashSet();
            _skipped.RemoveWhere(id => !inZone.Contains(id));

            if (ordered.Count == 0)
            {
                var hadPin = _pinnedPoiId != null;
                _pinnedPoiId = null;
                _lastDispatchedPoiId = null;
                return (null, hadPin);
            }

            IEnumerable<Poi> seq = ordered;
            if (_pinnedPoiId is int pinId)
            {
                if (!ordered.Any(p => p.Id == pinId))
                {
                    _pinnedPoiId = null;
                    _lastDispatchedPoiId = null;
                    return (null, true);
                }

                seq = ordered.Where(p => p.Id == pinId);
            }

            var chain = seq.Where(p => !_skipped.Contains(p.Id)).ToList();
            if (chain.Count == 0)
                return (null, false);

            var head = chain[0];
            if (_lastDispatchedPoiId == head.Id)
                return (null, false);

            _lastDispatchedPoiId = head.Id;
            return (head, false);
        }
    }
}
