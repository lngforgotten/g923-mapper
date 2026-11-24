using Vortice.DirectInput;

namespace G923Mapper;

/// <summary>
/// Force feedback on the wheel's steering axis: a centring spring and two vibrations (heavy and light)
/// driven by the game's rumble. Needs the device acquired exclusively.
/// </summary>
public sealed class WheelForce : IDisposable
{
    private readonly IDirectInputDevice8 _dev;
    private readonly int[] _axis;
    private IDirectInputEffect? _spring, _heavy, _light, _friction, _curve, _damper;
    private int _springPct = -1, _heavyMag = -1, _lightMag = -1, _frictionPct = -1, _curveMag, _damperPct = -1;
    private long _curveSentMs = long.MinValue / 2, _curveStartedMs = long.MinValue / 2;

    /// <summary>Which kind of effect gives the steady resistance: "friction", a "damper" if the wheel has no friction effect, or "none" if it has neither.</summary>
    public string FrictionKind { get; private set; } = "none";

    private WheelForce(IDirectInputDevice8 dev, int axisId)
    {
        _dev = dev;
        _axis = [axisId];
    }

    public static bool IsSupported(IDirectInputDevice8 dev) => dev.Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);

    /// <summary>Create the effects on the first axis (the wheel). Throws if the device refuses them.</summary>
    public static WheelForce Create(IDirectInputDevice8 dev, string? style = null)
    {
        // Sine is a smooth hum, square is a hard buzz, sawtooth is a rough rattle.
        Guid shape = ForceMath.NormaliseStyle(style) switch
        {
            ForceMath.StyleSharp => EffectGuid.Square,
            ForceMath.StyleRough => EffectGuid.SawtoothUp,
            _ => EffectGuid.Sine,
        };
        var axes = dev.GetObjects(DeviceObjectTypeFlags.Axis);
        if (axes.Count == 0) throw new InvalidOperationException("The wheel has no axes to apply force to.");
        var f = new WheelForce(dev, (int)axes[0].ObjectId);
        try
        {
            f._spring = f.Make(EffectGuid.Spring, new ConditionSet
            {
                Conditions = [new Condition { Offset = 0, PositiveCoefficient = 0, NegativeCoefficient = 0, PositiveSaturation = ForceMath.Max, NegativeSaturation = ForceMath.Max, DeadBand = 0 }],
            });
            f.MakeFriction();
            f.MakeCurve();
            f.MakeDamper();
            f._heavy = f.Make(shape, new PeriodicForce { Magnitude = 0, Offset = 0, Phase = 0, Period = ForceMath.HeavyPeriodMicroseconds });
            f._light = f.Make(shape, new PeriodicForce { Magnitude = 0, Offset = 0, Phase = 0, Period = ForceMath.LightPeriodMicroseconds });
        }
        catch
        {
            f.Dispose();
            throw;
        }
        return f;
    }

    private static ConditionSet Resistance(int coefficient) => new()
    {
        Conditions = [new Condition { Offset = 0, PositiveCoefficient = coefficient, NegativeCoefficient = coefficient, PositiveSaturation = ForceMath.Max, NegativeSaturation = ForceMath.Max, DeadBand = 0 }],
    };

    /// <summary>Not every wheel has a friction effect, so a refusal is not an error: try a damper, then go without (the slider then does nothing).</summary>
    private void MakeFriction()
    {
        foreach (var (guid, name) in new[] { (EffectGuid.Friction, "friction"), (EffectGuid.Damper, "damper") })
        {
            try { _friction = Make(guid, Resistance(0)); FrictionKind = name; AppLog.Info($"Wheel friction slider uses the wheel's {name} effect."); return; }
            catch (SharpGen.Runtime.SharpGenException ex) { AppLog.Info($"The wheel has no {name} effect ({ex.ResultCode}); trying the next kind."); }
        }
        AppLog.Warn("The wheel accepts neither a friction nor a damper effect, so the friction slider will do nothing on it.");
    }

    /// <summary>The effect that carries the app''s own centring curve. Not every wheel offers a constant force; without it the curve is simply unavailable.</summary>
    private void MakeCurve()
    {
        try { _curve = Make(EffectGuid.ConstantForce, new ConstantForce { Magnitude = 0 }, CurveLifetimeMicroseconds); }
        catch (SharpGen.Runtime.SharpGenException ex) { AppLog.Warn($"The wheel has no constant-force effect ({ex.ResultCode}), so the centring curve is unavailable; the plain spring still works."); }
    }

    /// <summary>
    /// The curve's damping is the wheel's own damper effect, which the wheel applies at its own rate. (Working it out in software from the wheel's
    /// position is noisy and a little late, which is the wrong thing to feed back into a force.) Not every wheel has one; the curve then has none.
    /// </summary>
    private void MakeDamper()
    {
        try { _damper = Make(EffectGuid.Damper, Resistance(0)); }
        catch (SharpGen.Runtime.SharpGenException ex) { AppLog.Info($"The wheel has no damper effect ({ex.ResultCode}), so the curve's damping does nothing."); }
    }

    /// <summary>Apply the curve's damping strength. Cheap to call every frame; only changes when the value does.</summary>
    public void SetDamper(int percent)
    {
        if (_damper is null || percent == _damperPct) return;
        _damperPct = percent;
        int k = ForceMath.DamperCoefficient(percent);
        if (k == 0) { _damper.Stop(); return; }
        _damper.SetParameters(new EffectParameters { Parameters = Resistance(k) }, EffectParameterFlags.TypeSpecificParameters);
        Begin(_damper, "curve damping");
    }

    /// <summary>The curve effect only lives this long and is renewed while the app is alive, so a frozen app cannot leave the wheel pulling.</summary>
    private const int CurveLifetimeMicroseconds = 400_000;
    private const int CurveRefreshMs = 100, CurveMinGapMs = 8, CurveMinStep = 40;

    /// <summary>True when the wheel took the constant-force effect, so the centring curve can be used.</summary>
    public bool HasCurve => _curve is not null;

    /// <summary>
    /// Send the centring force (the device's signed value, -10000 to 10000; 0 stops it). Cheap to call every frame: it only talks to the wheel when
    /// the value has moved a little or the effect is due to be renewed, and never more than about 125 times a second.
    /// </summary>
    public void SetCurveForce(int magnitude, long nowMs)
    {
        if (_curve is null) return;
        magnitude = Math.Clamp(magnitude, -ForceMath.Max, ForceMath.Max);
        if (magnitude == 0)
        {
            if (_curveMag != 0) { _curve.Stop(); _curveMag = 0; }
            return;
        }
        bool wasOff = _curveMag == 0;
        bool changed = Math.Abs(magnitude - _curveMag) >= CurveMinStep;
        bool renew = nowMs - _curveStartedMs >= CurveRefreshMs;
        if (!wasOff && !changed && !renew) return;
        if (!wasOff && nowMs - _curveSentMs < CurveMinGapMs) return;
        _curveSentMs = nowMs;
        if (wasOff || changed)
        {
            _curve.SetParameters(new EffectParameters { Parameters = new ConstantForce { Magnitude = magnitude } }, EffectParameterFlags.TypeSpecificParameters);
            _curveMag = magnitude;
        }
        if (wasOff || renew) { _curveStartedMs = nowMs; Begin(_curve, "centring curve"); }
    }

    /// <summary>Apply the friction strength. Cheap to call every frame; only changes when the value does.</summary>
    public void SetFriction(int percent)
    {
        if (_friction is null || percent == _frictionPct) return;
        _frictionPct = percent;
        int k = ForceMath.FrictionCoefficient(percent);
        if (k == 0) { _friction.Stop(); return; }
        _friction.SetParameters(new EffectParameters { Parameters = Resistance(k) }, EffectParameterFlags.TypeSpecificParameters);
        Begin(_friction, FrictionKind);
    }

    private IDirectInputEffect Make(Guid kind, TypeSpecificParameters p, int durationMicroseconds = -1) => _dev.CreateEffect(kind, new EffectParameters
    {
        Flags = EffectFlags.ObjectIds | EffectFlags.Cartesian,
        Duration = durationMicroseconds,   // -1 = infinite; we stop it ourselves
        SamplePeriod = 0,
        Gain = ForceMath.Max,
        TriggerButton = -1,             // not tied to a button
        TriggerRepeatInterval = 0,
        StartDelay = 0,
        Axes = _axis,
        Directions = [0],
        Parameters = p,
    });

    /// <summary>Apply the centring spring strength. Cheap to call every frame; only changes when the value does.</summary>
    public void SetSpring(int percent)
    {
        if (_spring is null || percent == _springPct) return;
        _springPct = percent;
        int k = ForceMath.SpringCoefficient(percent);
        if (k == 0) { _spring.Stop(); return; }
        _spring.SetParameters(new EffectParameters
        {
            Parameters = new ConditionSet
            {
                Conditions = [new Condition { Offset = 0, PositiveCoefficient = k, NegativeCoefficient = k, PositiveSaturation = ForceMath.Max, NegativeSaturation = ForceMath.Max, DeadBand = 0 }],
            },
        }, EffectParameterFlags.TypeSpecificParameters);
        Begin(_spring, "spring");
    }

    /// <summary>Set the vibration from the game's two motors at the user's strength. Cheap to call repeatedly.</summary>
    public void SetRumble(byte large, byte small, int percent)
    {
        Drive(_heavy, "heavy rumble", ref _heavyMag, ForceMath.RumbleMagnitude(large, percent), ForceMath.HeavyPeriodMicroseconds);
        Drive(_light, "light rumble", ref _lightMag, ForceMath.RumbleMagnitude(small, percent), ForceMath.LightPeriodMicroseconds);
    }

    /// <summary>The last thing that went wrong starting an effect, or empty. Shown by the health check.</summary>
    public string Problem { get; private set; } = "";

    private void Begin(IDirectInputEffect effect, string name)
    {
        var result = effect.Start();
        if (result.Failure)
        {
            Problem = $"The wheel would not start the {name} effect ({result.Code:X8}).";
            AppLog.Warn(Problem);
        }
        else Problem = "";
    }

    private void Drive(IDirectInputEffect? effect, string name, ref int current, int magnitude, int period)
    {
        if (effect is null || magnitude == current) return;
        bool wasSilent = current <= 0;
        current = magnitude;
        if (magnitude == 0) { effect.Stop(); return; }
        effect.SetParameters(new EffectParameters
        {
            Parameters = new PeriodicForce { Magnitude = magnitude, Offset = 0, Phase = 0, Period = period },
        }, EffectParameterFlags.TypeSpecificParameters);
        if (wasSilent) Begin(effect, name);
    }

    /// <summary>
    /// Another program can interrupt the wheel's effects: a game that opens the wheel itself (it sees the real wheel as well as the virtual
    /// controller) may take it over or reset it while its window is in front, which silences the centring spring and the rest until it lets go.
    /// This looks at every effect that should be playing and starts it again if it is not. Returns what it found, or null when all is well:
    /// the effects that were put back, and the ones the wheel would not restart (with Windows' error code), so the cause can be seen in the log.
    /// </summary>
    public string? Recover()
    {
        var restored = new List<string>();
        var refused = new List<string>();
        void Check(IDirectInputEffect? effect, string name, bool shouldPlay)
        {
            if (effect is null || !shouldPlay) return;
            try
            {
                if (effect.Status.HasFlag(EffectStatus.Playing)) return;
                var result = effect.Start();
                if (result.Failure) refused.Add($"{name} ({result.Code:X8})"); else restored.Add(name);
            }
            catch (SharpGen.Runtime.SharpGenException ex) { refused.Add($"{name} ({ex.ResultCode.Code:X8})"); }
        }
        Check(_spring, "spring", _springPct > 0);
        Check(_friction, FrictionKind, _frictionPct > 0);
        Check(_curve, "centring curve", _curveMag != 0);
        Check(_damper, "curve damping", _damperPct > 0);
        Check(_heavy, "heavy rumble", _heavyMag > 0);
        Check(_light, "light rumble", _lightMag > 0);
        if (restored.Count == 0 && refused.Count == 0) return null;
        string text = restored.Count > 0 ? "put back: " + string.Join(", ", restored) : "";
        if (refused.Count > 0) text += (text.Length > 0 ? "; " : "") + "would not restart: " + string.Join(", ", refused);
        return text;
    }

    /// <summary>
    /// Send the spring and the friction to the wheel again on the next frame, even though their values have not changed. A program that opens the
    /// real wheel can reset it behind DirectInput's back, so the effects still read as playing while the wheel has dropped them; sending them
    /// again once a second puts them back within a second, whatever the cause.
    /// </summary>
    public void Resend()
    {
        _springPct = -1;
        _frictionPct = -1;
        _damperPct = -1;
    }

    /// <summary>What the wheel says about each effect and its actuators. For diagnosing "it stopped" reports.</summary>
    public string Diagnose()
    {
        static string State(IDirectInputEffect? e)
        {
            if (e is null) return "none";
            try { return e.Status.ToString(); } catch (SharpGen.Runtime.SharpGenException ex) { return "err:" + ex.ResultCode.ToString(); }
        }
        string device;
        try { device = _dev.GetForceFeedbackState().ToString(); } catch (SharpGen.Runtime.SharpGenException ex) { device = "err:" + ex.ResultCode.ToString(); }
        return $"heavy={State(_heavy)}({_heavyMag}) light={State(_light)}({_lightMag}) spring={State(_spring)}({_springPct}%) curve={State(_curve)}({_curveMag}) damper={State(_damper)}({_damperPct}%) {FrictionKind}={State(_friction)}({_frictionPct}%) device=[{device}]";
    }

    /// <summary>Silence everything, for example when the game stops rumbling or the mapper stops.</summary>
    public void Silence()
    {
        try { _heavy?.Stop(); _light?.Stop(); _spring?.Stop(); _friction?.Stop(); _curve?.Stop(); _damper?.Stop(); } catch (SharpGen.Runtime.SharpGenException) { }
        _heavyMag = _lightMag = 0;
        _springPct = 0;
        _frictionPct = 0;
        _curveMag = 0;
        _damperPct = 0;
    }

    public void Dispose()
    {
        Silence();
        foreach (var e in new[] { _spring, _heavy, _light, _friction, _curve, _damper })
        {
            try { e?.Unload(); e?.Dispose(); } catch (SharpGen.Runtime.SharpGenException) { }
        }
        _spring = _heavy = _light = _friction = _curve = _damper = null;
    }
}

