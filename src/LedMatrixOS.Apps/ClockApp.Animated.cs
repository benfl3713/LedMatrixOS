using System.Numerics;
using LedMatrixOS.Apps.Clocks;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Particles;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

// Animated style: the showy clock, a piece of living light art. Glowing neon digits float over three flowing aurora ribbons;
// every second a ripple runs out from the colon and kicks the ribbons into a wave front, embers drift up from the floor, and
// when a digit changes the new one drops in with a bounce and a spray of sparks as it lands. The digits keep a white-hot core
// and a dark underlay so they stay readable whatever the waves are doing.
public sealed partial class ClockApp
{
    private readonly AnimatedFx _fx = new();
    private SparkBursts? _bursts;
    private Emitter? _embers;
    private WaveField? _waves;

    // Palette names only the Digital style knows fall back to the Animated default, so the shared setting never breaks it.
    private string AnimatedPaletteName() => Palette is "Sunset" or "Neon" or "Ember" or "Mono" ? "Aurora" : Palette;

    private void AnimatedSettingChanged(string key)
    {
        switch (key)
        {
            case "palette":
                _fx.SetPalette(AnimatedPaletteName());
                Recolor();
                break;
            case "show24Hour":
                break;
            case "waves":
                if (_waves is not null) _waves.Intensity = Waves ? 1f : 0f;
                break;
            case "sparks":
                if (_embers is not null) _embers.Enabled = Sparks;
                break;
            case "showSeconds":
            case "showDate":
                Host.Root = Build();
                break;
        }
    }

    private void Recolor()
    {
        _bursts?.Recolor();
        if (_embers is null) return;
        if (_fx.Rainbow)
        {
            _embers.Palette = [Pixel.FromHsv(10, .8f, 1), Pixel.FromHsv(70, .8f, 1), Pixel.FromHsv(150, .8f, 1), Pixel.FromHsv(210, .8f, 1), Pixel.FromHsv(290, .8f, 1)];
        }
        else
        {
            _embers.Palette = null;
            var p = _fx.Palette;
            _embers.Gradient = [Pixel.Lerp(p.G0, Pixel.White, 0.55f), p.G0, p.G1, p.G2];
        }
    }

    private Node BuildAnimated()
    {
        _fx.SetPalette(AnimatedPaletteName());
        _state = new ClockState(Time);
        _state.Refresh();
        _fx.Time = (float)_state.SecondsOfDay;
        var st = _state;

        var atlas = GlyphAtlas.Get(25, 44, 6f, 3);
        var small = GlyphAtlas.Get(12, 19, 3.4f, 2);

        var embersSystem = new ParticleSystem(256, 64, 140, new Random(5));
        _embers = new Emitter
        {
            X = 0, Y = 62, Width = 255, Height = 2,
            Rate = 11f,
            LifetimeMin = 2.2f, LifetimeMax = 4.2f,
            SpeedMin = 5f, SpeedMax = 16f,
            Angle = -90f, Spread = 70f,
            GravityY = -3f,
            AlphaStart = 0.95f, AlphaEnd = 0f,
            Enabled = Sparks,
        };
        embersSystem.Add(_embers);
        var burstSystem = new ParticleSystem(256, 64, 200, new Random(9));
        _bursts = new SparkBursts(burstSystem, _fx);
        Recolor();
        for (int i = 0; i < 70; i++) embersSystem.Update(0.05f); // already glowing on the first frame

        NeonDigit Big(Func<int> src, int index, bool sparks = true)
        {
            var d = new NeonDigit(atlas, src, _fx, st, index) { Roll = DigitRoll.Drop, RollDuration = TimeSpan.FromMilliseconds(620) };
            d.StartEntrance(TimeSpan.FromMilliseconds(100 + index * 120));
            if (sparks) d.Changed = digit => { if (Sparks) _bursts?.Queue(digit.ScreenBounds, 0.2f); };
            return d;
        }

        NeonDigit Tiny(Func<int> src, int index)
        {
            var d = new NeonDigit(small, src, _fx, st, index + 4) { Roll = DigitRoll.Drop, RollDuration = TimeSpan.FromMilliseconds(420), BobAmount = 0.8f };
            d.StartEntrance(TimeSpan.FromMilliseconds(600 + index * 100));
            return d;
        }

        int clockWidth = 4 * atlas.Stride + 14;
        int total = clockWidth + (ShowSeconds ? 6 + 2 * small.Stride : 0);
        int left = (256 - total) / 2;

        var row = new Stack(Orientation.Horizontal)
        {
            Margin = new Thickness(left, 7, 0, 0),
            Height = atlas.Rows,
            HAlign = Align.Start,
            CrossAlign = Align.Center,
            Children =
            {
                Big(() => Show24Hour ? st.Hour / 10 : (st.Hour12 >= 10 ? 1 : -1), 0),
                Big(() => (Show24Hour ? st.Hour : st.Hour12) % 10, 1),
                new ColonNode(st, () => _fx.Glow(0.4f).WithBrightness(1.15f), atlas.Rows, 3.2f),
                Big(() => st.Minute / 10, 2),
                Big(() => st.Minute % 10, 3),
            },
        };

        _waves = new WaveField(_state, _fx) { Intensity = Waves ? 1f : 0f };
        var root = new Panel
        {
            new ClockStateNode(_state),
            new FxDriver(_state, _fx),
            _waves,
            new ParticleLayer(embersSystem),
            new BurstDriver(_bursts),
            row,
        };

        if (ShowSeconds)
        {
            var secs = new Stack(Orientation.Horizontal)
            {
                Margin = new Thickness(left + clockWidth + 6, 31, 0, 0),
                HAlign = Align.Start,
                VAlign = Align.Start,
                Children = { Tiny(() => st.Second / 10, 0), Tiny(() => st.Second % 10, 1) },
            };
            root.Add(secs);
        }

        if (ShowDate)
        {
            int dx = ShowSeconds ? left + clockWidth + 8 : left + clockWidth - 40;
            var date = new Label(() => st.LongDateText)
            {
                Margin = new Thickness(dx, ShowSeconds ? 18 : 3, 0, 0),
                Style = new TextStyle(Fonts.ExtraSmall, new Pixel(235, 240, 255)),
            };
            if (Show24Hour == false && ShowSeconds)
            {
                // The 12-hour marker sits just under the date.
                root.Add(new Label(() => st.IsPm ? "PM" : "AM")
                {
                    Margin = new Thickness(dx, 25, 0, 0),
                    Style = new TextStyle(Fonts.ExtraSmall, new Pixel(255, 210, 120)),
                });
            }
            date.Position = new Vector2(0, -30);
            date.AnimatePosition(Vector2.Zero, TimeSpan.FromMilliseconds(900), Easing.OutBack);
            root.Add(date);
        }

        root.Add(new ParticleLayer(burstSystem));
        return root;
    }
}
