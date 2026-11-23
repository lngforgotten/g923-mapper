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
    private IDirectInputEffect? _spring, _heavy, _light;
    private int _springPct = -1, _heavyMag = -1, _lightMag = -1;

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

    private IDirectInputEffect Make(Guid kind, TypeSpecificParameters p) => _dev.CreateEffect(kind, new EffectParameters
    {
        Flags = EffectFlags.ObjectIds | EffectFlags.Cartesian,
        Duration = -1,                  // infinite; we stop it ourselves
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
        return $"heavy={State(_heavy)}({_heavyMag}) light={State(_light)}({_lightMag}) spring={State(_spring)}({_springPct}%) device=[{device}]";
    }

    /// <summary>Silence everything, for example when the game stops rumbling or the mapper stops.</summary>
    public void Silence()
    {
        try { _heavy?.Stop(); _light?.Stop(); _spring?.Stop(); } catch (SharpGen.Runtime.SharpGenException) { }
        _heavyMag = _lightMag = 0;
        _springPct = 0;
    }

    public void Dispose()
    {
        Silence();
        foreach (var e in new[] { _spring, _heavy, _light })
        {
            try { e?.Unload(); e?.Dispose(); } catch (SharpGen.Runtime.SharpGenException) { }
        }
        _spring = _heavy = _light = null;
    }
}
