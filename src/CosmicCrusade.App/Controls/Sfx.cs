using System.IO;
using System.Windows.Media;

namespace CosmicCrusade.App.Controls;

/// <summary>The sound effects. Each is synthesised in code (no asset files) the first time it plays.</summary>
public enum SfxKind
{
    Blaster, Impact, Splat, Cog, Death, Aura, Decay, Shield, Land, Lock, Draw, Fanfare, Smoke,
    // Second effects batch
    Vortex, Portal, Rift, Slash, Shatter, LockOn, Rewind, Laser, Mirror, Wind, Claw, Tractor, Tick, Thruster, Thunder,
    Dust, Bones, PsiHum, Squelch, Servo, Holo, Horn, Gavel, Sweep, Heartbeat, Flick, Hover,
    // Effects pass 3: Warzone ambients
    Geiger, Frost, Crackle, Sizzle, Skitter, HiveBuzz, EggPulse, EyeOpen, Shimmer, Toll, Crowd, Conveyor, Gong, Bugle,
    Coins, Bullseye, Scalpel, Bubble, Prism, Suction,
    // Effects pass 3: board, atmosphere, big moments
    Riffle, Deal, Taunt, Cycle, Crack, SlowMo, LiftOff,
}

/// <summary>
/// Procedural sound effects: each sound is generated once as a 16-bit mono WAV in %TEMP%\CosmicCrusadeSfx and played
/// through its own <see cref="MediaPlayer"/> so sounds can overlap. Sound is optional: any failure is swallowed.
/// </summary>
public static class Sfx
{
    public static bool Enabled { get; set; } = true;

    /// <summary>Master volume 0–1.</summary>
    public static double Volume { get; set; } = 0.6;

    private const int Rate = 22050;
    private const int MaxVoices = 14;
    private static readonly Dictionary<SfxKind, string> Files = new();
    private static readonly Dictionary<SfxKind, DateTime> LastPlayed = new();
    private static readonly List<MediaPlayer> Voices = new();

    /// <summary>Plays a sound. <paramref name="minGapMs"/> drops repeats of the same sound that come too close together.</summary>
    public static void Play(SfxKind kind, double volume = 1, double minGapMs = 0)
    {
        if (!Enabled || Volume <= 0.001 || Voices.Count >= MaxVoices) return;
        var now = DateTime.UtcNow;
        if (minGapMs > 0 && LastPlayed.TryGetValue(kind, out var last) && (now - last).TotalMilliseconds < minGapMs) return;
        LastPlayed[kind] = now;

        try
        {
            var player = new MediaPlayer { Volume = Math.Clamp(Volume * volume, 0, 1) };
            void Done(object? s, EventArgs e)
            {
                player.Close();
                Voices.Remove(player);
            }
            player.MediaEnded += Done;
            player.MediaFailed += (s, e) => Done(s, e);
            Voices.Add(player);
            player.Open(new Uri(FileFor(kind)));
            player.Play();
        }
        catch
        {
            // Sound is decoration: never let it break the game.
        }
    }

    private static string FileFor(SfxKind kind)
    {
        if (Files.TryGetValue(kind, out var path) && File.Exists(path)) return path;
        var dir = Path.Combine(Path.GetTempPath(), "CosmicCrusadeSfx");
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, $"{kind}.v1.wav");
        if (!File.Exists(path)) File.WriteAllBytes(path, ToWav(Synth(kind)));
        Files[kind] = path;
        return path;
    }

    // ------------------------------------------------------------------ synthesis

    private static float[] Synth(SfxKind kind) => kind switch
    {
        // "Pew": a fast downward sweep of a buzzy wave.
        SfxKind.Blaster => Render(0.24, (t, n) =>
        {
            // f(t) = 1500·e^(−9t) + 180, integrated for the phase.
            double phase = 2 * Math.PI * (1500 / 9.0 * (1 - Math.Exp(-9 * t)) + 180 * t);
            return (Soft(Math.Sin(phase), 0.3) * 0.7 + n * 0.12) * Math.Exp(-t * 11);
        }),

        // A short crack: filtered noise over a low thump.
        SfxKind.Impact => Lowpass(Render(0.16, (t, n) => n * Math.Exp(-t * 30) + Math.Sin(2 * Math.PI * 90 * t) * Math.Exp(-t * 22) * 0.8), 0.35),

        // Wet splat: a lowpassed noise burst with a wobble, then a falling "blorp".
        SfxKind.Splat => Mix(
            Lowpass(Render(0.32, (t, n) => n * Math.Exp(-t * 12) * (1 + 0.5 * Math.Sin(2 * Math.PI * 38 * t))), 0.12),
            Render(0.32, (t, _) => Math.Sin(2 * Math.PI * (320 * t - 350 * t * t)) * Math.Exp(-t * 9) * 0.6)),

        // Ratchet: six metallic clicks.
        SfxKind.Cog => Render(0.62, (t, n) =>
        {
            double local = t % 0.1;
            double click = Math.Exp(-local * 160);
            return (Math.Sin(2 * Math.PI * 3200 * t) * 0.5 + Math.Sin(2 * Math.PI * 4700 * t) * 0.3 + n * 0.4) * click * 0.8;
        }),

        // Death: a falling tone dissolving into noise.
        SfxKind.Death => Lowpass(Render(0.95, (t, n) =>
        {
            double phase = 2 * Math.PI * (380 / 2.2 * (1 - Math.Exp(-2.2 * t)) + 50 * t);
            double tone = Math.Sin(phase) * (1 - t);
            return (tone * 0.7 + n * t * 0.6) * Math.Min(1, (0.95 - t) * 6);
        }), 0.4),

        // Psionic shimmer: a rising, vibrating chord that fades in and out.
        SfxKind.Aura => Render(1.15, (t, _) =>
        {
            double vib = 1 + 0.012 * Math.Sin(2 * Math.PI * 6 * t);
            double rise = 1 + t * 0.25;
            double chord = Math.Sin(2 * Math.PI * 440 * vib * rise * t) + Math.Sin(2 * Math.PI * 554 * vib * rise * t)
                           + Math.Sin(2 * Math.PI * 659 * vib * rise * t);
            return chord / 3 * Math.Sin(Math.PI * t / 1.15) * 0.6;
        }),

        // Decay: a low, beating growl with bubbling.
        SfxKind.Decay => Lowpass(Render(1.1, (t, n) =>
        {
            double growl = Saw(55, t) * 0.5 + Saw(58.5, t) * 0.5;
            double trem = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 7 * t);
            double bubbles = n * (Math.Sin(2 * Math.PI * 13 * t) > 0.6 ? 0.5 : 0.05);
            return (growl * trem + bubbles) * Math.Sin(Math.PI * t / 1.1) * 0.7;
        }), 0.18),

        // Shield: a bright two-tone chime.
        SfxKind.Shield => Render(0.7, (t, _) =>
            (Math.Sin(2 * Math.PI * 1320 * t) * 0.5 + Math.Sin(2 * Math.PI * 1980 * t) * 0.35) * Math.Exp(-t * 6)),

        // Landing thump.
        SfxKind.Land => Lowpass(Render(0.22, (t, n) => Math.Sin(2 * Math.PI * (75 * t - 30 * t * t)) * Math.Exp(-t * 16) + n * Math.Exp(-t * 40) * 0.3), 0.3),

        // Lock: a heavy metallic clank.
        SfxKind.Lock => Render(0.3, (t, n) =>
            (Math.Sin(2 * Math.PI * 820 * t) * 0.4 + Math.Sin(2 * Math.PI * 1230 * t) * 0.3 + n * 0.3) * Math.Exp(-t * 18)),

        // Card draw: a soft whoosh.
        SfxKind.Draw => Lowpass(Render(0.14, (t, n) => n * Math.Sin(Math.PI * t / 0.14) * 0.6), 0.2),

        // Round won: a quick arpeggio.
        SfxKind.Fanfare => Render(0.75, (t, _) =>
        {
            double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
            int i = Math.Min(3, (int)(t / 0.12));
            double local = t - i * 0.12;
            double env = i == 3 ? Math.Exp(-local * 3) : Math.Exp(-local * 10);
            return Soft(Math.Sin(2 * Math.PI * notes[i] * t), 0.25) * env * 0.45;
        }),

        // Smoke: a breathy hiss.
        SfxKind.Smoke => Lowpass(Render(0.8, (t, n) => n * Math.Sin(Math.PI * t / 0.8) * 0.45), 0.08),

        // ---------------------------------------------------------------- second batch

        // Black Hole: a deep, falling, swirling whoosh.
        SfxKind.Vortex => Lowpass(Render(1.3, (t, n) =>
        {
            double phase = 2 * Math.PI * (160 / 1.5 * (1 - Math.Exp(-1.5 * t)) + 30 * t);
            double swirl = 0.6 + 0.4 * Math.Sin(2 * Math.PI * (3 + 6 * t) * t);
            return (Math.Sin(phase) * 0.6 + n * 0.5) * swirl * Math.Sin(Math.PI * t / 1.3);
        }), 0.12),

        // Wormhole portal: a rising, phasing hum.
        SfxKind.Portal => Render(1.2, (t, _) =>
        {
            double f = 110 + 220 * t;
            double a = Math.Sin(2 * Math.PI * f * t) + Math.Sin(2 * Math.PI * f * 1.5 * t + Math.Sin(2 * Math.PI * 4 * t));
            return a * 0.3 * Math.Sin(Math.PI * t / 1.2);
        }),

        // Dimensional Rift: a tearing crackle.
        SfxKind.Rift => Render(0.7, (t, n) => n * (Math.Sin(2 * Math.PI * 47 * t) > 0.2 ? 0.9 : 0.15) * Math.Exp(-t * 3)),

        // Shadow Strike: a sharp swish.
        SfxKind.Slash => Render(0.25, (t, n) => n * Math.Sin(Math.PI * t / 0.25) * (0.4 + t * 2.4)),

        // Glass shattering: bright crackle with tinkles.
        SfxKind.Shatter => Render(0.6, (t, n) =>
            (n * Math.Exp(-t * 14) + Math.Sin(2 * Math.PI * 3900 * t) * Math.Exp(-((t * 7) % 1) * 9) * 0.3) * 0.8),

        // Target Practice lock-on: two quick beeps.
        SfxKind.LockOn => Render(0.32, (t, _) => (t % 0.16 < 0.07 ? Math.Sin(2 * Math.PI * 1760 * t) * 0.5 : 0)),

        // Second Thought: a backwards swell.
        SfxKind.Rewind => Render(0.6, (t, n) =>
        {
            double phase = 2 * Math.PI * (200 * t + 900 * t * t);
            return (Math.Sin(phase) * 0.4 + n * 0.2) * (t / 0.6) * (t / 0.6);
        }),

        // Xeno Laser: a charging whine, then a buzzing beam.
        SfxKind.Laser => Render(0.9, (t, n) =>
            t < 0.4 ? Math.Sin(2 * Math.PI * (300 * t + 1500 * t * t)) * (t / 0.4) * 0.35
                    : (Soft(Math.Sin(2 * Math.PI * 140 * t), 0.2) * 0.5 + n * 0.15) * Math.Exp(-(t - 0.4) * 4)),

        // Respond in Kind: a bright reflected ping.
        SfxKind.Mirror => Render(0.5, (t, _) =>
            (Math.Sin(2 * Math.PI * 2093 * t) + Math.Sin(2 * Math.PI * 2637 * t) * 0.6) * Math.Exp(-t * 7) * 0.4),

        // High Noon: desert wind.
        SfxKind.Wind => Lowpass(Render(2.2, (t, n) => n * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.7 * t)) * Math.Sin(Math.PI * t / 2.2)), 0.06),

        // Commandeer: a cable zip and a clamp.
        SfxKind.Claw => Render(0.45, (t, n) =>
            t < 0.3 ? n * 0.3 * Math.Sin(Math.PI * t / 0.3) : Math.Sin(2 * Math.PI * 600 * t) * Math.Exp(-(t - 0.3) * 30) * 0.7),

        // Tractor beam: a wobbling hum.
        SfxKind.Tractor => Render(0.9, (t, _) =>
            Math.Sin(2 * Math.PI * (180 + 20 * Math.Sin(2 * Math.PI * 5 * t)) * t) * Math.Sin(Math.PI * t / 0.9) * 0.4),

        // Roulette tick.
        SfxKind.Tick => Render(0.04, (t, n) => (Math.Sin(2 * Math.PI * 2400 * t) * 0.5 + n * 0.2) * Math.Exp(-t * 90)),

        // Thruster burst.
        SfxKind.Thruster => Lowpass(Render(0.4, (t, n) => n * Math.Exp(-t * 6)), 0.25),

        // Polarity Storm thunder crack.
        SfxKind.Thunder => Lowpass(Render(1.2, (t, n) => n * (Math.Exp(-t * 6) + 0.3 * Math.Exp(-t * 1.5))), 0.15),

        // Race landings.
        SfxKind.Dust => Lowpass(Render(0.5, (t, n) => n * Math.Exp(-t * 6) * 0.8), 0.1),
        SfxKind.Bones => Render(0.45, (t, n) =>
        {
            double local = (t * 13) % 1;
            return (n * 0.6 + Math.Sin(2 * Math.PI * 900 * t) * 0.3) * Math.Exp(-local * 12) * (1 - t);
        }),
        SfxKind.PsiHum => Render(0.8, (t, _) =>
            Math.Sin(2 * Math.PI * 220 * (1 + 0.01 * Math.Sin(2 * Math.PI * 7 * t)) * t) * Math.Sin(Math.PI * t / 0.8) * 0.45),
        SfxKind.Squelch => Lowpass(Render(0.4, (t, n) =>
            (n * 0.6 + Math.Sin(2 * Math.PI * (180 * t + 300 * t * t))) * Math.Exp(-t * 6) * 0.7), 0.2),
        SfxKind.Servo => Render(0.45, (t, _) =>
            Saw(180 + 500 * t, t) * 0.3 * Math.Sin(Math.PI * t / 0.45)),
        SfxKind.Holo => Render(0.7, (t, _) =>
        {
            double[] notes = { 1046.5, 1318.5, 1568, 2093 };
            int i = Math.Min(3, (int)(t / 0.08));
            return Math.Sin(2 * Math.PI * notes[i] * t) * Math.Exp(-t * 3) * 0.3;
        }),

        // Leader arrival: a brass chord swelling in.
        SfxKind.Horn => Lowpass(Render(1.3, (t, _) =>
        {
            double env = Math.Min(1, t / 0.25) * Math.Exp(-Math.Max(0, t - 0.6) * 3);
            return (Saw(130.8, t) + Saw(196, t) * 0.8 + Saw(261.6, t) * 0.6) / 2.4 * env;
        }), 0.15),

        // The Empress / Senate Chambers: a gavel.
        SfxKind.Gavel => Render(0.5, (t, n) =>
            (Math.Sin(2 * Math.PI * 140 * t) * 0.8 + n * 0.5) * Math.Exp(-t * 18)
            + (t > 0.22 ? (Math.Sin(2 * Math.PI * 160 * t) * 0.6 + n * 0.3) * Math.Exp(-(t - 0.22) * 20) : 0)),

        // Clean-up sweep.
        SfxKind.Sweep => Lowpass(Render(0.7, (t, n) => n * Math.Sin(Math.PI * t / 0.7) * 0.8), 0.15),

        // Close round: a heartbeat (lub-dub).
        SfxKind.Heartbeat => Lowpass(Render(0.55, (t, _) =>
            Math.Sin(2 * Math.PI * 55 * t) * (Math.Exp(-t * 25) + (t > 0.2 ? Math.Exp(-(t - 0.2) * 25) * 0.7 : 0))), 0.25),

        // Hand: picking a card up, hovering one.
        SfxKind.Flick => Render(0.08, (t, n) => n * Math.Exp(-t * 60) * 0.7),
        SfxKind.Hover => Render(0.03, (t, _) => Math.Sin(2 * Math.PI * 3000 * t) * Math.Exp(-t * 150) * 0.25),

        // ---------------------------------------------------------------- effects pass 3: Warzone ambients

        // Fallout Shelter: one Geiger-counter click (played in random bursts).
        SfxKind.Geiger => Render(0.025, (t, n) => n * Math.Exp(-t * 400)),

        // Cryogenic Chambers: ice creaking and crackling.
        SfxKind.Frost => Render(0.9, (t, n) =>
        {
            double crack = Math.Sin(2 * Math.PI * 23 * t) > 0.85 ? n * 0.8 : n * 0.05;
            return (crack + Math.Sin(2 * Math.PI * (2600 - 900 * t) * t) * 0.12) * Math.Sin(Math.PI * t / 0.9);
        }),

        // The Scorched Lands: a fire's crackle.
        SfxKind.Crackle => Lowpass(Render(1.0, (t, n) =>
            n * (Math.Sin(2 * Math.PI * 17 * t + Math.Sin(2 * Math.PI * 5 * t) * 3) > 0.7 ? 0.9 : 0.12) * Math.Sin(Math.PI * t)), 0.5),

        // Corrosive Channels: a hissing sizzle.
        SfxKind.Sizzle => Render(0.9, (t, n) => n * (0.35 + 0.25 * Math.Sin(2 * Math.PI * 9 * t)) * Math.Sin(Math.PI * t / 0.9) * 0.7),

        // Swarm Warrens: tiny legs skittering.
        SfxKind.Skitter => Render(0.5, (t, n) =>
        {
            double local = (t * 38) % 1;
            return n * Math.Exp(-local * 18) * 0.6 * Math.Sin(Math.PI * t / 0.5);
        }),

        // Central Hive: a buzzing drone.
        SfxKind.HiveBuzz => Lowpass(Render(1.4, (t, _) =>
            (Saw(118, t) * 0.5 + Saw(121, t) * 0.5) * (0.7 + 0.3 * Math.Sin(2 * Math.PI * 11 * t)) * Math.Sin(Math.PI * t / 1.4) * 0.6), 0.25),

        // Nyxthian Hatchery: a wet, low pulse.
        SfxKind.EggPulse => Lowpass(Render(0.6, (t, n) =>
            (Math.Sin(2 * Math.PI * (70 + 30 * t) * t) + n * 0.25) * Math.Sin(Math.PI * t / 0.6) * 0.8), 0.2),

        // The Oculus / Oracle's Arch: an ominous swelling tone.
        SfxKind.EyeOpen => Render(1.4, (t, _) =>
            (Math.Sin(2 * Math.PI * 82.4 * t) + Math.Sin(2 * Math.PI * 87.3 * t) * 0.7 + Math.Sin(2 * Math.PI * 164.8 * t) * 0.3)
            / 2 * Math.Sin(Math.PI * t / 1.4)),

        // Planet Earth / Cyber Moon / Galactic Capital: a rising sparkle.
        SfxKind.Shimmer => Render(1.2, (t, _) =>
        {
            double sum = 0;
            for (int k = 0; k < 5; k++) sum += Math.Sin(2 * Math.PI * (880 + k * 330) * (1 + t * 0.3) * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * (3 + k) * t));
            return sum / 5 * Math.Sin(Math.PI * t / 1.2) * 0.4;
        }),

        // Hills of Decay: a distant bell toll.
        SfxKind.Toll => Render(2.0, (t, _) =>
            (Math.Sin(2 * Math.PI * 196 * t) + Math.Sin(2 * Math.PI * 392 * 1.01 * t) * 0.5 + Math.Sin(2 * Math.PI * 587 * t) * 0.25)
            / 1.75 * Math.Exp(-t * 2) * 0.8),

        // The Campaign Trail: a distant crowd cheering.
        SfxKind.Crowd => Lowpass(Render(1.6, (t, n) => n * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 2.5 * t + n * 0.3)) * Math.Sin(Math.PI * t / 1.6)), 0.22),

        // Production Facility: a conveyor's rhythmic clank.
        SfxKind.Conveyor => Render(1.2, (t, n) =>
        {
            double local = t % 0.3;
            return (Math.Sin(2 * Math.PI * 330 * t) * 0.4 + n * 0.4) * Math.Exp(-local * 40) + Saw(60, t) * 0.08;
        }),

        // Senate Chambers: a resonant marble gong.
        SfxKind.Gong => Render(2.2, (t, _) =>
            (Math.Sin(2 * Math.PI * 110 * t) + Math.Sin(2 * Math.PI * 277 * t) * 0.6 + Math.Sin(2 * Math.PI * 416 * t) * 0.4)
            / 2 * Math.Exp(-t * 1.6) * Math.Min(1, t * 40)),

        // Military Academy: a short bugle call.
        SfxKind.Bugle => Lowpass(Render(1.0, (t, _) =>
        {
            double[] notes = { 392, 523.25, 659.25, 783.99 };
            int i = Math.Min(3, (int)(t / 0.2));
            double local = t - i * 0.2;
            double env = Math.Min(1, local * 30) * (i == 3 ? Math.Exp(-local * 2.5) : Math.Exp(-local * 6));
            return (Saw(notes[i], t) * 0.6 + Math.Sin(2 * Math.PI * notes[i] * t) * 0.4) * env * 0.6;
        }), 0.35),

        // Mercenary Camp: coins clinking.
        SfxKind.Coins => Render(0.6, (t, _) =>
        {
            double sum = 0;
            for (int k = 0; k < 4; k++)
            {
                double at = k * 0.11;
                if (t >= at) sum += Math.Sin(2 * Math.PI * (3100 + k * 410) * t) * Math.Exp(-(t - at) * 30);
            }
            return sum * 0.35;
        }),

        // Training Grounds: a dart thunking into a target.
        SfxKind.Bullseye => Render(0.3, (t, n) =>
            (Math.Sin(2 * Math.PI * (220 - 120 * t) * t) * 0.8 + n * 0.3) * Math.Exp(-t * 18)),

        // Vivisection Laboratory: a surgical whine and a monitor beep.
        SfxKind.Scalpel => Render(0.8, (t, _) =>
            (t % 0.4 < 0.08 ? Math.Sin(2 * Math.PI * 1046.5 * t) * 0.35 : 0) + Math.Sin(2 * Math.PI * 4200 * t) * 0.04),

        // Blaarxis / Primordial Marsh: a bubble popping.
        SfxKind.Bubble => Render(0.18, (t, _) => Math.Sin(2 * Math.PI * (300 + 2200 * t) * t) * Math.Exp(-t * 25) * 0.7),

        // Refraction Network: a glassy, prismatic chord.
        SfxKind.Prism => Render(1.2, (t, _) =>
            (Math.Sin(2 * Math.PI * 1318.5 * t) + Math.Sin(2 * Math.PI * 1661.2 * t) + Math.Sin(2 * Math.PI * 1975.5 * t))
            / 3 * Math.Exp(-t * 2.5) * 0.5),

        // Vacuum Spire: air being sucked upward.
        SfxKind.Suction => Lowpass(Render(1.3, (t, n) => n * (t / 1.3) * Math.Sin(Math.PI * t / 1.3)), 0.06),

        // ---------------------------------------------------------------- effects pass 3: board, atmosphere, moments

        // Riffle shuffle: a run of quick card flaps.
        SfxKind.Riffle => Render(0.7, (t, n) =>
        {
            double local = (t * 55) % 1;
            return n * Math.Exp(-local * 9) * 0.6 * Math.Sin(Math.PI * t / 0.7);
        }),

        // Dealing one card: a crisp snap.
        SfxKind.Deal => Render(0.06, (t, n) => n * Math.Exp(-t * 80) * 0.8),

        // AI taunt: a little robotic "heh-heh" blip.
        SfxKind.Taunt => Render(0.45, (t, _) =>
        {
            double local = t % 0.15;
            double f = 520 - local * 1400;
            return (t < 0.3 ? Soft(Math.Sin(2 * Math.PI * f * t), 0.3) * Math.Exp(-local * 22) : 0) * 0.5;
        }),

        // The weakness cycle lighting up.
        SfxKind.Cycle => Render(0.6, (t, _) =>
        {
            int i = Math.Min(4, (int)(t / 0.08));
            double[] notes = { 659.25, 783.99, 880, 987.77, 1318.5 };
            return Math.Sin(2 * Math.PI * notes[i] * t) * Math.Exp(-(t - i * 0.08) * 12) * 0.4;
        }),

        // Defeat: the screen cracks.
        SfxKind.Crack => Render(0.5, (t, n) =>
            (n * Math.Exp(-t * 20) + Math.Sin(2 * Math.PI * 180 * t) * Math.Exp(-t * 10) * 0.5) * 0.9),

        // Deciding Duel: a deep slow-motion "whoom".
        SfxKind.SlowMo => Lowpass(Render(1.4, (t, n) =>
            (Math.Sin(2 * Math.PI * (90 - 40 * t) * t) * 0.8 + n * 0.2) * Math.Sin(Math.PI * t / 1.4)), 0.1),

        // Victory: Units lift off.
        SfxKind.LiftOff => Lowpass(Render(1.4, (t, n) =>
            (n * 0.5 + Math.Sin(2 * Math.PI * (120 + 300 * t) * t) * 0.4) * Math.Sin(Math.PI * t / 1.4)), 0.3),

        _ => Array.Empty<float>(),
    };

    // ------------------------------------------------------------------ music

    private static readonly List<MediaPlayer> MusicLayers = new();
    private static double _tension;

    public static bool MusicEnabled { get; private set; }

    /// <summary>Starts or stops the looping music bed (a calm pad plus a pulse layer that rises with tension).</summary>
    public static void SetMusic(bool on)
    {
        if (on == MusicEnabled) return;
        MusicEnabled = on;
        try
        {
            if (!on)
            {
                foreach (var p in MusicLayers) p.Close();
                MusicLayers.Clear();
                return;
            }
            foreach (var (name, data) in new (string, Func<float[]>)[] { ("MusicPad", MusicPad), ("MusicPulse", MusicPulse) })
            {
                var path = Path.Combine(Path.GetTempPath(), "CosmicCrusadeSfx", $"{name}.v1.wav");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) File.WriteAllBytes(path, ToWav(data()));
                var player = new MediaPlayer();
                player.MediaEnded += (_, _) =>
                {
                    player.Position = TimeSpan.Zero;
                    player.Play();
                };
                player.Open(new Uri(path));
                MusicLayers.Add(player);
            }
            ApplyMusicVolume();
            foreach (var p in MusicLayers) p.Play();
        }
        catch
        {
            MusicEnabled = false;
        }
    }

    /// <summary>0 = calm, 1 = the round is on a knife edge: the pulse layer fades in.</summary>
    public static void SetTension(double tension)
    {
        _tension = Math.Clamp(tension, 0, 1);
        ApplyMusicVolume();
    }

    public static void ApplyMusicVolume()
    {
        if (MusicLayers.Count < 2) return;
        MusicLayers[0].Volume = Volume * 0.35;
        MusicLayers[1].Volume = Volume * 0.4 * _tension;
    }

    // 4 chords × 4 s: Am – F – C – G. Every layer loops seamlessly at 16 s.
    private static readonly double[][] Chords =
    {
        new[] { 110.0, 130.81, 164.81 }, new[] { 87.31, 110.0, 130.81 },
        new[] { 130.81, 164.81, 196.0 }, new[] { 98.0, 123.47, 146.83 },
    };

    private static float[] MusicPad()
    {
        const double len = 16;
        var buf = new float[(int)(len * Rate)];
        double y = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double t = i / (double)Rate;
            int c = (int)(t / 4) % 4;
            double local = t % 4;
            double env = Math.Min(1, local / 0.8) * Math.Min(1, (4 - local) / 0.8) * 0.6 + 0.4; // soft crossfade
            double x = 0;
            foreach (var f in Chords[c])
                x += Saw(f, t) * 0.5 + Saw(f * 1.004, t) * 0.5; // detuned pair = warmth
            x = x / Chords[c].Length * env * (0.8 + 0.2 * Math.Sin(2 * Math.PI * t / 8));
            y += 0.04 * (x - y); // dark lowpass
            buf[i] = (float)(y * 1.6);
        }
        return Normalise(buf, 0.7f);
    }

    private static float[] MusicPulse()
    {
        const double len = 16, step = 0.25;
        var buf = new float[(int)(len * Rate)];
        for (int i = 0; i < buf.Length; i++)
        {
            double t = i / (double)Rate;
            int c = (int)(t / 4) % 4;
            int k = (int)(t / step);
            double local = t - k * step;
            double f = Chords[c][k % 3] * 2;
            double note = Soft(Math.Sin(2 * Math.PI * f * t), 0.4) * Math.Exp(-local * 14);
            double kick = k % 4 == 0 ? Math.Sin(2 * Math.PI * (60 * local - 40 * local * local)) * Math.Exp(-local * 18) : 0;
            buf[i] = (float)(note * 0.5 + kick * 0.8);
        }
        return Normalise(buf, 0.7f);
    }

    private static float[] Normalise(float[] x, float peak)
    {
        float max = x.Length == 0 ? 1 : x.Max(Math.Abs);
        if (max > 0.0001f)
            for (int i = 0; i < x.Length; i++) x[i] = x[i] / max * peak;
        return x;
    }

    /// <summary>Renders <paramref name="seconds"/> of audio from f(time, white noise sample).</summary>
    private static float[] Render(double seconds, Func<double, double, double> f)
    {
        var rng = new Random(1234);
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double noise = rng.NextDouble() * 2 - 1;
            buf[i] = (float)Math.Clamp(f(t, noise), -1, 1);
        }
        // 5 ms fade in/out against clicks.
        int edge = Math.Min(n / 2, Rate / 200);
        for (int i = 0; i < edge; i++)
        {
            float k = i / (float)edge;
            buf[i] *= k;
            buf[n - 1 - i] *= k;
        }
        return buf;
    }

    /// <summary>Soft-clips a sine towards a square wave (smaller softness = buzzier).</summary>
    private static double Soft(double sine, double softness) => Math.Tanh(sine / Math.Max(0.01, softness)) * 0.8;

    private static double Saw(double freq, double t) => 2 * (t * freq - Math.Floor(t * freq + 0.5));

    /// <summary>One-pole lowpass; <paramref name="a"/> 0–1 (smaller = darker).</summary>
    private static float[] Lowpass(float[] x, double a)
    {
        double y = 0;
        for (int i = 0; i < x.Length; i++)
        {
            y += a * (x[i] - y);
            x[i] = (float)y;
        }
        // Lowpassing loses level: normalise back up to 0.9 peak.
        float peak = x.Length == 0 ? 1 : x.Max(Math.Abs);
        if (peak > 0.0001f)
            for (int i = 0; i < x.Length; i++) x[i] = x[i] / peak * 0.9f;
        return x;
    }

    private static float[] Mix(float[] a, float[] b)
    {
        var r = new float[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < r.Length; i++)
            r[i] = Math.Clamp((i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0), -1f, 1f);
        return r;
    }

    private static byte[] ToWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(Rate);
        w.Write(Rate * 2);      // byte rate
        w.Write((short)2);      // block align
        w.Write((short)16);     // bits
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (var s in samples) w.Write((short)(s * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
