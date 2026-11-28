namespace AudioTaskbar;

/// <summary>The ambient sound effects the app can synthesize (v5.4). Ids are the keys of <c>soundVolumes</c> in config.json.</summary>
internal static class SoundEffects
{
    public const string Rain = "rain", Thunder = "thunder", Wind = "wind", Traffic = "traffic", Horns = "horns",
        Train = "train", Airplane = "airplane", Birds = "birds", Insects = "insects", Animals = "animals", Waves = "waves";

    public static readonly (string Id, string NameVi)[] All =
    {
        (Rain, "Mưa rơi"), (Thunder, "Sấm sét"), (Wind, "Gió"), (Traffic, "Xe cộ"), (Horns, "Còi xe"),
        (Train, "Tàu hỏa (xa / gần)"), (Airplane, "Máy bay (cất cánh)"), (Birds, "Chim hót"), (Insects, "Côn trùng, ve, dế"),
        (Animals, "Ếch, chó, gà gáy"), (Waves, "Sóng nước")
    };

    public static IEnumerable<string> Ids => All.Select(a => a.Id);
    public static bool IsKnown(string? id) => id is not null && All.Any(a => a.Id == id);
}

/// <summary>How loud a sound should be in the current scene (0..1) plus one effect-specific colour (0..1).</summary>
internal readonly record struct SoundTarget(float Level, float Aux = 0f);

/// <summary>What a region sounds like: relative presence of each sound before weather, season and time of day are applied.</summary>
internal readonly record struct RegionSoundTraits(float Traffic, float Horns, float Birds, float Insects, float Waves, float Wind, float Animals, float Train);

/// <summary>
/// Mixes the soundscape from the scene: region × weather × season × time of day. Pure functions (no audio device), so
/// it is unit-tested. The result is the *scene level* of each effect; the user's master and per-effect volumes
/// (both 0 on first run) are applied afterwards by <see cref="AmbientMixer"/>.
/// </summary>
internal static class SoundScape
{
    public static RegionSoundTraits Traits(string regionId) => regionId switch
    {
        "hcmc" => new(0.95f, 0.95f, 0.30f, 0.35f, 0.12f, 0.20f, 0.10f, 0.45f),
        "baria-vungtau" => new(0.45f, 0.30f, 0.50f, 0.50f, 0.90f, 0.45f, 0.20f, 0.35f),
        "hanoi" => new(0.85f, 0.85f, 0.40f, 0.40f, 0.10f, 0.20f, 0.10f, 0.50f),
        "danang-hoian" => new(0.50f, 0.35f, 0.50f, 0.50f, 0.65f, 0.35f, 0.25f, 0.40f),
        "tokyo" => new(0.60f, 0.08f, 0.40f, 0.60f, 0.05f, 0.20f, 0.20f, 1.00f),
        "paris" => new(0.55f, 0.50f, 0.50f, 0.20f, 0.08f, 0.25f, 0.10f, 0.55f),
        "sydney" => new(0.50f, 0.30f, 0.75f, 0.40f, 0.85f, 0.50f, 0.20f, 0.60f),
        "newyork" => new(1.00f, 1.00f, 0.20f, 0.15f, 0.10f, 0.30f, 0.05f, 0.80f),
        "rome" => new(0.70f, 0.75f, 0.65f, 0.40f, 0.03f, 0.25f, 0.15f, 0.40f),
        "giza" => new(0.40f, 0.50f, 0.20f, 0.30f, 0.00f, 0.90f, 0.55f, 0.00f),
        "santorini" => new(0.12f, 0.05f, 0.45f, 0.80f, 0.85f, 0.70f, 0.35f, 0.00f),
        _ => new(0.30f, 0.15f, 0.80f, 0.55f, 0.05f, 0.30f, 0.55f, 0.35f) // default: quiet town and countryside
    };

    private static float Bump(float h, float center, float width)
    {
        float d = MathF.Abs(h - center);
        if (d > 12f) d = 24f - d;
        return MathF.Exp(-(d * d) / (2f * width * width));
    }

    /// <summary>Traffic density across the day: quiet at night, rush hours around 8:00 and 17:30.</summary>
    public static float TrafficOfDay(float hour)
        => Math.Clamp(0.22f + 0.55f * Bump(hour, 8f, 1.6f) + 0.65f * Bump(hour, 17.5f, 2f) + 0.35f * Bump(hour, 12.5f, 2.5f), 0.12f, 1f);

    public static Dictionary<string, SoundTarget> Targets(RegionProfile region, SceneMood mood, SeasonId season)
    {
        var t = Traits(region.Id);
        float hour = mood.Hour;
        float day = mood.DayK;
        float night = 1f - day;
        float dawn = Bump(hour, 6f, 1.4f);
        float dusk = Bump(hour, 18f, 1.2f);
        float rain = mood.IsRainy ? Math.Clamp(mood.RainAmt, 0.3f, 1f) : 0f;
        float storm = mood.IsStorm ? 1f : 0f;
        float snow = mood.IsSnow ? Math.Clamp(mood.SnowAmt, 0.3f, 1f) : 0f;
        float wet = Math.Max(rain, snow * 0.4f);
        float calm = 1f - Math.Max(rain, storm) * 0.85f - snow * 0.7f;

        float birdSeason = season switch { SeasonId.Spring => 1.25f, SeasonId.Summer => 1f, SeasonId.Autumn => 0.7f, _ => 0.35f };
        float bugSeason = season switch { SeasonId.Spring => 0.7f, SeasonId.Summer => 1.2f, SeasonId.Autumn => 0.6f, _ => 0.05f };
        float animalSeason = season switch { SeasonId.Winter => 0.4f, SeasonId.Autumn => 0.8f, _ => 1f };
        float windSeason = season switch { SeasonId.Autumn => 1.25f, SeasonId.Winter => 1.1f, SeasonId.Spring => 1f, _ => 0.9f };

        float traffic = t.Traffic * TrafficOfDay(hour) * (1f - snow * 0.45f);
        float horns = t.Horns * TrafficOfDay(hour) * (1f - wet * 0.7f);
        float birds = t.Birds * birdSeason * (0.12f + 0.55f * day + 0.9f * dawn + 0.3f * dusk) * calm;
        float insects = t.Insects * bugSeason * (0.15f + 0.85f * night + 0.35f * day) * calm * (1f - snow);
        float animals = t.Animals * animalSeason * (0.2f + 0.7f * night + 0.6f * dawn) * calm;
        float waves = t.Waves * (0.55f + 0.45f * Math.Min(1f, 0.4f + mood.Cloudiness * 0.6f + storm)) * (1f + 0.4f * storm);
        float wind = Math.Clamp(t.Wind * windSeason * (0.35f + 0.5f * mood.Cloudiness + 0.65f * storm) + 0.12f * rain, 0f, 1f);
        float train = (mood.IsStorm ? 0.8f : 1f) * t.Train * (0.35f + 0.65f * TrafficOfDay(hour));

        var d = new Dictionary<string, SoundTarget>
        {
            [SoundEffects.Rain] = new(rain),
            [SoundEffects.Thunder] = new(storm, rain),   // aux = rain intensity: more rain, more frequent thunder
            [SoundEffects.Wind] = new(wind),
            [SoundEffects.Traffic] = new(Math.Clamp(traffic, 0f, 1f), wet),
            [SoundEffects.Horns] = new(Math.Clamp(horns, 0f, 1f)),
            [SoundEffects.Train] = new(Math.Clamp(train, 0f, 1f)),
            // The airplane's own loudness follows the plane on the taskbar; the scene only lowers it a little in a storm.
            [SoundEffects.Airplane] = new(mood.IsStorm ? 0.7f : 1f),
            [SoundEffects.Birds] = new(Math.Clamp(birds, 0f, 1f)),
            // aux: 0 = night crickets, 1 = daytime cicadas (hot-season daylight)
            [SoundEffects.Insects] = new(Math.Clamp(insects, 0f, 1f), season == SeasonId.Summer ? day : day * 0.3f),
            // aux: 0 = night (frogs, dogs), 1 = daybreak (roosters)
            [SoundEffects.Animals] = new(Math.Clamp(animals, 0f, 1f), Math.Clamp(dawn * 1.5f + day * 0.3f, 0f, 1f)),
            [SoundEffects.Waves] = new(Math.Clamp(waves, 0f, 1f))
        };
        return d;
    }

    /// <summary>Master (0..100) × per-effect volume (0..100) → linear 0..1 gain. 0 whenever either is 0.</summary>
    public static float Gain(int master, int effect)
        => Math.Clamp(master, 0, 100) / 100f * (Math.Clamp(effect, 0, 100) / 100f);
}
