using LedMatrixOS.Apps.Pet;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// A small creature that lives on the display. Its belly empties slowly (and refills when a calendar event finishes or you feed it); its
/// mood follows its belly, the finished events, the weather and the hour (it sleeps at night). The belly level is an Advanced setting, so
/// it is kept in the normal settings storage and the pet picks up where it left off. The inputs sit behind small interfaces
/// (<see cref="ICalendarProgress"/>, <see cref="IDaylight"/>, <see cref="IRainSensor"/>) with plain defaults.
/// </summary>
public sealed class PetApp : WidgetApp
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(5);

    private readonly ICalendarProgress _calendar;
    private readonly IDaylight _daylight;
    private readonly IRainSensor _rain;

    private float _decay;
    private int _events = -1;
    private TimeSpan _nextRead = TimeSpan.Zero;
    private bool _night, _raining;
    private PetMood _mood = PetMood.Content;
    private int _shownFullness = -1, _shownEvents = -1;
    private PetMood _shownMood = (PetMood)(-1);
    private string? _shownName;
    private bool _shownRain, _shownNight, _shownWeatherInit;

    private PetField _field = null!;
    private Label _nameLabel = null!, _moodLabel = null!, _fedLabel = null!, _eventsLabel = null!, _weatherLabel = null!;
    private ProgressBar _bar = null!;

    public override string Id => "pet";
    public override string Name => "Pet";
    public override int FrameRate => 30;

    [Setting("Name", Description = "What the pet is called.")]
    public string PetName { get; set; } = "Pip";

    [Setting("Hours To Empty", Description = "How many hours a full belly lasts.", Min = 1, Max = 48)]
    public int HoursToEmpty { get; set; } = 8;

    [Setting("Feed", Description = "Turn on to feed the pet (it switches itself back off).")]
    public bool Feed { get; set; }

    [Setting("Fullness", Description = "How full the pet's belly is (0-100). Saved between runs.", Min = 0, Max = PetRules.MaxFullness, Advanced = true)]
    public int Fullness { get; set; } = 80;

    [ActivatorUtilitiesConstructor]
    public PetApp() : this(new NoCalendarProgress(), new ClockDaylight(), new NoRain()) { }

    public PetApp(ICalendarProgress calendar, IDaylight daylight, IRainSensor rain)
    {
        _calendar = calendar;
        _daylight = daylight;
        _rain = rain;
    }

    public PetMood Mood => _mood;

    protected override void OnSettingChanged(string key)
    {
        if (key == "feed" && Feed)
        {
            Feed = false;
            Fullness = PetRules.Clamp(Fullness + 40);
            _decay = 0;
        }
    }

    // ---- view --------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var name = new TextStyle(Fonts.Small, new Pixel(255, 205, 140), Shadow: false);
        var muted = new TextStyle(Fonts.QuiteSmall, new Pixel(160, 160, 175), Shadow: false);
        var normal = new TextStyle(Fonts.QuiteSmall, new Pixel(225, 225, 235), Shadow: false);

        _field = new PetField { HAlign = Align.Stretch, VAlign = Align.Stretch };
        _nameLabel = new Label(PetName) { Style = name };
        _moodLabel = new Label("") { Style = normal };
        _fedLabel = new Label("") { Style = muted };
        _bar = new ProgressBar { Thickness = 5, Background = new Pixel(30, 30, 38), Border = new Pixel(90, 90, 105) };
        _eventsLabel = new Label("") { Style = muted };
        _weatherLabel = new Label("") { Style = muted };

        var stats = new Stack(Orientation.Vertical, gap: 2)
        {
            Width = 84,
            HAlign = Align.End,
            VAlign = Align.Stretch,
            Padding = new Thickness(6, 2, 6, 0),
            Children = { _nameLabel, _moodLabel, _fedLabel, _bar, _eventsLabel, _weatherLabel },
        };

        _shownName = null;
        _shownFullness = _shownEvents = -1;
        _shownMood = (PetMood)(-1);
        _shownWeatherInit = false;
        _nextRead = TimeSpan.Zero;
        _events = -1;
        return new Dock { Right = stats, Fill = _field, HAlign = Align.Stretch, VAlign = Align.Stretch };
    }

    // ---- per frame ---------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        if (context.Time >= _nextRead)
        {
            _nextRead = context.Time + ReadEvery;
            ReadInputs();
        }

        // The belly empties a little every frame; the saved level changes only when a whole point has gone.
        _decay += (float)context.Delta.TotalSeconds * PetRules.MaxFullness / (Math.Max(1, HoursToEmpty) * 3600f);
        if (_decay >= 1f)
        {
            int points = (int)_decay;
            _decay -= points;
            Fullness = PetRules.Clamp(Fullness - points);
        }

        Fullness = PetRules.Clamp(Fullness);
        var mood = PetRules.MoodOf(Fullness, Math.Max(0, _events), _night, _raining);
        _mood = mood;
        _field.Mood = mood;
        _field.IsNight = _night;
        _field.IsRaining = _raining;
        RefreshLabels();

        base.Update(context, cancellationToken);
    }

    private void ReadInputs()
    {
        var now = Time.GetLocalNow();
        _night = _daylight.IsNight(now);
        _raining = _rain.IsRaining;

        int events = Math.Max(0, _calendar.EventsCompletedToday(now));
        if (_events >= 0 && events > _events) Fullness = PetRules.Clamp(Fullness + (events - _events) * PetRules.FoodPerEvent);
        _events = events;
    }

    /// <summary>Rebuilds label text only when a value changes, so a steady pet allocates nothing.</summary>
    private void RefreshLabels()
    {
        if (!ReferenceEquals(_shownName, PetName)) { _shownName = PetName; _nameLabel.Text = PetName.ToUpperInvariant(); }
        if (_mood != _shownMood)
        {
            _shownMood = _mood;
            _moodLabel.Text = PetRules.Label(_mood);
        }

        if (Fullness != _shownFullness)
        {
            _shownFullness = Fullness;
            _fedLabel.Text = "FED " + Fullness + "%";
            _bar.Value = Fullness / (float)PetRules.MaxFullness;
            _bar.Fill = Fullness < PetRules.HungryBelow ? new Pixel(255, 90, 70) : Fullness < 60 ? new Pixel(255, 190, 50) : new Pixel(80, 210, 110);
        }

        if (_events != _shownEvents)
        {
            _shownEvents = _events;
            _eventsLabel.Text = "DONE TODAY " + Math.Max(0, _events);
        }

        if (!_shownWeatherInit || _raining != _shownRain || _night != _shownNight)
        {
            _shownWeatherInit = true;
            _shownRain = _raining;
            _shownNight = _night;
            _weatherLabel.Text = (_night ? "NIGHT" : "DAY") + (_raining ? ", RAIN" : "");
        }
    }
}
