using System.Globalization;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Screens;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps.Screens;

/// <summary>What a binding key currently says: display text plus a numeric reading (NaN when it has none). Instances are replaced, never mutated, so reference equality means "unchanged".</summary>
internal sealed class BindValue(string text, double number)
{
    public static readonly BindValue Empty = new("--", double.NaN);

    public string Text { get; } = text;
    public double Number { get; } = number;
}

/// <summary>Starts a background poll for the resolver (<c>MatrixAppBase.Poll</c> is protected, so the app hands this out).</summary>
internal interface IPollHost
{
    ILiveData<T> Poll<T>(TimeSpan interval, Func<CancellationToken, Task<T>> fetch);
}

/// <summary>A value you set directly. Test seam, and the building block for sources that need no polling.</summary>
internal sealed class StaticLive : ILiveData<BindValue>
{
    public StaticLive(string text, double number = double.NaN) => Value = new BindValue(text, number);

    public BindValue? Value { get; set; }
    public bool IsLoading => false;
    public Exception? Error => null;
    public DateTimeOffset? LastUpdated => null;
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary>Maps a polled value to a <see cref="BindValue"/>, re-mapping only when the polled object changes.</summary>
internal sealed class MappedLive<T>(ILiveData<T> source, Func<T, BindValue> map) : ILiveData<BindValue> where T : class
{
    private T? _last;
    private BindValue? _cached;

    public BindValue? Value
    {
        get
        {
            var v = source.Value;
            if (_cached is null || !ReferenceEquals(v, _last))
            {
                _last = v;
                _cached = v is null ? BindValue.Empty : map(v);
            }
            return _cached;
        }
    }

    public bool IsLoading => source.IsLoading;
    public Exception? Error => source.Error;
    public DateTimeOffset? LastUpdated => source.LastUpdated;
    public event EventHandler? Changed { add => source.Changed += value; remove => source.Changed -= value; }
}

/// <summary>Local time as HH:mm, from the app's <see cref="TimeProvider"/>. Changes once a minute.</summary>
internal sealed class TimeLive(TimeProvider time) : ILiveData<BindValue>
{
    private long _minute = long.MinValue;
    private BindValue? _cached;

    public BindValue? Value
    {
        get
        {
            var now = time.GetLocalNow();
            long minute = now.Ticks / TimeSpan.TicksPerMinute;
            if (minute != _minute || _cached is null)
            {
                _minute = minute;
                _cached = new BindValue(now.ToString("HH:mm", CultureInfo.InvariantCulture), now.Hour * 60 + now.Minute);
            }
            return _cached;
        }
    }

    public bool IsLoading => false;
    public Exception? Error => null;
    public DateTimeOffset? LastUpdated => null;
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary>The next bin collection ("Black tomorrow"), recomputed when the local date changes. Rules come from <c>BinDay:Bins</c> (same syntax as the Bin Day app's Bins setting).</summary>
internal sealed class BinDayLive(TimeProvider time, string? bins) : ILiveData<BindValue>
{
    private readonly CollectionSchedule _schedule = new(BinParser.ParseBins(bins).Rules);
    private DateOnly _day;
    private BindValue? _cached;

    public BindValue? Value
    {
        get
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            if (_cached is null || today != _day)
            {
                _day = today;
                var next = _schedule.NextPerBin(today).Cast<Collection?>().FirstOrDefault();
                if (next is null) _cached = BindValue.Empty;
                else
                {
                    int days = next.Value.Date.DayNumber - today.DayNumber;
                    string when = days == 0 ? "today" : days == 1 ? "tomorrow" : next.Value.Date.DayOfWeek.ToString()[..3];
                    _cached = new BindValue(next.Value.Name + " " + when, days);
                }
            }
            return _cached;
        }
    }

    public bool IsLoading => false;
    public Exception? Error => null;
    public DateTimeOffset? LastUpdated => null;
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary>A compiled template string. Constant when it references no bindings; otherwise <see cref="Get"/> returns the same string instance until a referenced value changes.</summary>
internal sealed class CompiledText
{
    private readonly string[]? _literals;          // literal i precedes source i; the last entry trails
    private readonly ILiveData<BindValue>[]? _sources;
    private readonly BindValue?[]? _seen;
    private readonly string[]? _pieces;
    private string? _cached;

    public CompiledText(string constant)
    {
        Constant = constant;
        Get = () => constant;
    }

    public CompiledText(string[] literals, ILiveData<BindValue>[] sources)
    {
        _literals = literals;
        _sources = sources;
        _seen = new BindValue?[sources.Length];
        _pieces = new string[literals.Length + sources.Length];
        Get = Evaluate;
    }

    public string? Constant { get; }
    public bool IsConstant => Constant is not null;

    /// <summary>The single source when the whole template is exactly one <c>{key}</c>.</summary>
    public ILiveData<BindValue>? Single => _sources is { Length: 1 } && _literals!.All(l => l.Length == 0) ? _sources[0] : null;

    public Func<string> Get { get; } = null!;

    private string Evaluate()
    {
        bool changed = _cached is null;
        for (int i = 0; i < _sources!.Length; i++)
        {
            var v = _sources[i].Value ?? BindValue.Empty;
            if (!ReferenceEquals(v, _seen![i])) { _seen[i] = v; changed = true; }
        }
        if (changed)
        {
            int p = 0;
            for (int i = 0; i < _sources.Length; i++)
            {
                _pieces![p++] = _literals![i];
                _pieces[p++] = _seen![i]!.Text;
            }
            _pieces![p] = _literals![^1];
            _cached = string.Concat(_pieces);
        }
        return _cached!;
    }
}

/// <summary>What <c>{item}</c> and <c>{item.label}</c> mean inside a list's item template.</summary>
internal sealed record ItemScope(string Label, string Text, ILiveData<BindValue>? Live);

/// <summary>
/// Turns binding keys into live sources: one source per distinct key (weather shares a single poll across its five fields),
/// and compiles template strings and numeric props into delegates that do no work and allocate nothing until a value changes.
/// </summary>
internal sealed class BindingResolver
{
    private readonly TimeProvider _time;
    private readonly IConfiguration? _config;
    private readonly HttpClient _http;
    private readonly IPollHost _polls;
    private readonly IReadOnlyDictionary<string, ILiveData<BindValue>> _overrides;
    private readonly Dictionary<BindingKey, ILiveData<BindValue>> _sources = new();
    private ILiveData<WeatherSnapshot>? _weather;
    private TflApi? _tfl;
    private HaApi? _ha;

    public BindingResolver(TimeProvider time, IConfiguration? config, HttpClient http, IPollHost polls,
        IReadOnlyDictionary<string, ILiveData<BindValue>>? overrides = null)
    {
        _time = time;
        _config = config;
        _http = http;
        _polls = polls;
        _overrides = overrides ?? new Dictionary<string, ILiveData<BindValue>>();
    }

    /// <summary>Number of distinct sources created so far (tests assert deduplication with it).</summary>
    public int SourceCount => _sources.Count;

    public ILiveData<BindValue> Get(BindingKey key)
    {
        if (_sources.TryGetValue(key, out var existing)) return existing;
        var made = Create(key);
        _sources[key] = made;
        return made;
    }

    private ILiveData<BindValue> Create(BindingKey key)
    {
        if (_overrides.TryGetValue(KeyText(key), out var forced)) return forced;
        switch (key.Kind)
        {
            case BindingKind.Time: return new TimeLive(_time);
            case BindingKind.BinDay: return new BinDayLive(_time, _config?["BinDay:Bins"]);
            case BindingKind.Weather: return WeatherField(key.Name);
            case BindingKind.Tube:
                {
                    _tfl ??= new TflApi(_http) { AppKey = _config?["TFL:AppKey"] };
                    var api = _tfl;
                    var line = key.Name;
                    var data = _polls.Poll(TimeSpan.FromMinutes(5), ct => api.GetLineStatusesAsync([line], ct));
                    return new MappedLive<LineStatus[]>(data, s => s.Length == 0 ? BindValue.Empty : new BindValue(s[0].Description, s[0].Severity));
                }
            case BindingKind.HomeAssistant:
                {
                    _ha ??= new HaApi(_http) { BaseUrl = _config?["HomeAssistant:BaseUrl"] ?? "", Token = _config?["HomeAssistant:Token"] ?? "" };
                    var api = _ha;
                    if (!api.IsConfigured) return new StaticLive("--");
                    var entities = new List<EntityRef> { new(key.Name, null) };
                    var data = _polls.Poll(TimeSpan.FromSeconds(30), ct => api.GetStatesAsync(entities, ct));
                    return new MappedLive<HaState?[]>(data, s => s.Length == 0 || s[0] is null ? BindValue.Empty : HaValue(s[0]!));
                }
            default:
                return new StaticLive("--");
        }
    }

    private static string KeyText(BindingKey key) => key.Kind switch
    {
        BindingKind.Weather => "weather." + key.Name,
        BindingKind.Tube => "tube." + key.Name,
        BindingKind.HomeAssistant => "ha:" + key.Name,
        _ => key.Name,
    };

    private static BindValue HaValue(HaState s)
    {
        if (s.State is "unavailable" or "unknown") return BindValue.Empty;
        double number = double.TryParse(s.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
        string unit = s.Unit ?? "";
        string text = unit.Length == 0 ? s.State : char.IsLetter(unit[0]) ? s.State + " " + unit : s.State + unit;
        return new BindValue(text, number);
    }

    private ILiveData<BindValue> WeatherField(string field)
    {
        if (_weather is null)
        {
            IWeatherSource source = string.Equals(_config?["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase)
                ? new FakeWeatherSource()
                : new OpenMeteoWeatherSource(_http);
            var query = new WeatherQuery(_config?["Weather:Location"] is { Length: > 0 } loc ? loc : "London",
                string.Equals(_config?["Weather:Units"], "Fahrenheit", StringComparison.OrdinalIgnoreCase));
            _weather = _polls.Poll(TimeSpan.FromMinutes(10), ct => source.GetAsync(query, ct));
        }
        return new MappedLive<WeatherSnapshot>(_weather, s => field switch
        {
            "temp" => Whole(s.Temp),
            "feels" => Whole(s.Feels),
            "high" => Whole(s.High),
            "low" => Whole(s.Low),
            _ => Whole(s.PrecipChance),
        });
    }

    private static BindValue Whole(double v) => new(Math.Round(v).ToString("0", CultureInfo.InvariantCulture), v);

    // ---- compilation ----------------------------------------------------------------------------------------------------------

    private ILiveData<BindValue> Resolve(BindingKey key, ItemScope? scope)
    {
        if (key.Kind != BindingKind.Item) return Get(key);
        if (scope is null) return new StaticLive("--");
        if (key.Name == "item.label") return new StaticLive(scope.Label);
        return scope.Live ?? new StaticLive(scope.Text);
    }

    /// <summary>Compiles a <c>{key}</c> template. Returns null when the template is malformed (the caller skips the node).</summary>
    public CompiledText? CompileText(string template, ItemScope? scope = null)
    {
        if (!BindingKey.TryParseTemplate(template, out var keys, out _)) return null;
        if (keys.Count == 0) return new CompiledText(Unescape(template));

        var literals = new List<string>();
        var sources = new List<ILiveData<BindValue>>();
        var sb = new StringBuilder();
        int k = 0;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{' && template[i + 1] != '{')
            {
                int end = template.IndexOf('}', i + 1);
                literals.Add(sb.ToString());
                sb.Clear();
                sources.Add(Resolve(keys[k++], scope));
                i = end;
            }
            else if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c) { sb.Append(c); i++; }
            else sb.Append(c);
        }
        literals.Add(sb.ToString());
        return new CompiledText(literals.ToArray(), sources.ToArray());
    }

    private static string Unescape(string s) => s.Replace("{{", "{").Replace("}}", "}");

    /// <summary>Compiles a prop that is a literal number, a template/number string, or <c>{"bind":"key"}</c> into a reader of a number (NaN when unknown).</summary>
    public Func<float>? CompileNumber(JsonElement value, ItemScope? scope = null)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                float n = value.GetSingle();
                return () => n;
            case JsonValueKind.Object:
                return BoundNumber(ObjectSource(value, scope));
            case JsonValueKind.String:
                var text = CompileText(value.GetString()!, scope);
                if (text is null) return null;
                if (text.IsConstant)
                {
                    float parsed = ParseNumber(text.Constant!);
                    return () => parsed;
                }
                if (text.Single is { } single) return BoundNumber(single);
                var get = text.Get;
                string? last = null;
                float cached = float.NaN;
                return () =>
                {
                    var s = get();
                    if (!ReferenceEquals(s, last)) { last = s; cached = ParseNumber(s); }
                    return cached;
                };
            default:
                return null;
        }
    }

    private ILiveData<BindValue>? ObjectSource(JsonElement value, ItemScope? scope)
    {
        foreach (var p in value.EnumerateObject())
            if (p.Name == "bind" && p.Value.ValueKind == JsonValueKind.String && BindingKey.TryParse(p.Value.GetString(), out var key, out _))
                return Resolve(key, scope);
        return null;
    }

    private static Func<float>? BoundNumber(ILiveData<BindValue>? source) =>
        source is null ? null : () => (float)(source.Value?.Number ?? double.NaN);

    private static float ParseNumber(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : float.NaN;

    /// <summary>
    /// Compiles a series prop for the charts. A string is a list of numbers separated by commas, spaces or semicolons (templates allowed:
    /// <c>"{weather.low},15,{weather.high}"</c>); a bare binding builds a rolling history of that value instead.
    /// </summary>
    public Func<IReadOnlyList<float>>? CompileSeries(JsonElement value, ItemScope? scope = null, int historyLength = 48)
    {
        ILiveData<BindValue>? bound = null;
        CompiledText? text = null;
        if (value.ValueKind == JsonValueKind.Object) bound = ObjectSource(value, scope);
        else if (value.ValueKind == JsonValueKind.String)
        {
            text = CompileText(value.GetString()!, scope);
            if (text is null) return null;
            bound = text.Single;
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            IReadOnlyList<float> one = [value.GetSingle()];
            return () => one;
        }
        else return null;

        if (bound is not null)
        {
            var history = new List<float>(historyLength + 1);
            BindValue? seen = null;
            return () =>
            {
                var v = bound.Value;
                if (v is not null && !ReferenceEquals(v, seen))
                {
                    seen = v;
                    if (double.IsFinite(v.Number))
                    {
                        if (history.Count >= historyLength) history.RemoveAt(0);
                        history.Add((float)v.Number);
                    }
                }
                return history;
            };
        }
        if (text is null) return null;

        if (text.IsConstant)
        {
            IReadOnlyList<float> fixedList = ParseSeries(text.Constant!);
            return () => fixedList;
        }
        var get = text.Get;
        string? last = null;
        IReadOnlyList<float> cached = [];
        return () =>
        {
            var s = get();
            if (!ReferenceEquals(s, last)) { last = s; cached = ParseSeries(s); }
            return cached;
        };
    }

    internal static float[] ParseSeries(string s)
    {
        var list = new List<float>();
        foreach (var part in s.Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f)) list.Add(f);
        return list.ToArray();
    }

    /// <summary>The live source a list row stands for when the row text is itself a binding key.</summary>
    public ILiveData<BindValue>? SourceForRow(string text) =>
        BindingKey.TryParse(text, out var key, out _) && key.Kind != BindingKind.Item ? Get(key) : null;
}
