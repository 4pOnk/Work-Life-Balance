namespace WorkLifeBalance.Domain;

public static class Tracker
{
    public static TrackerState Start(Observation observation, TrackerSettings settings) =>
        new(observation.Now, observation.Now, observation.Activity, settings.Classify(observation.Activity));

    public static Transition Advance(TrackerState previous, Observation observation, TrackerSettings settings,
        Command command = Command.Sample)
    {
        var now = Math.Max(previous.At, observation.Now);
        var lastInput = Math.Clamp(observation.LastInput, previous.IdleFloor, now);
        var next = previous with { At = now, Activity = observation.Activity, Category = settings.Classify(observation.Activity) };
        Slice? slice = null;
        AwayRange? away = null;
        var wasAway = previous.Presence != Presence.Active;

        // A missed collector heartbeat is an unknown gap, unless a known away period explains it.
        if (!previous.Paused && now > previous.At && (now - previous.At <= 5000 || wasAway))
        {
            slice = new(previous.At, now, previous.Activity.Executable, previous.Category, previous.Activity.Browser);
            if (wasAway) away = new(previous.At, now, previous.Presence.ToString());
        }
        else if (now - previous.At > 5000 && !wasAway)
        {
            next = next with { IdleFloor = now };
            lastInput = now;
        }

        switch (command)
        {
            case Command.TogglePause:
                next = next with
                {
                    Paused = !previous.Paused,
                    Manual = false,
                    AutoSince = null,
                    AwaySince = null,
                    IdleFloor = now
                };
                break;
            case Command.ToggleAfk when !previous.Paused && !previous.Locked && !previous.Suspended:
                next = wasAway
                    ? next with { Manual = false, AutoSince = null, AwaySince = null, IdleFloor = now }
                    : next with { Manual = true, AwaySince = now };
                break;
            case Command.Lock: next = next with { Locked = true }; break;
            case Command.Unlock: next = next with { Locked = false, AutoSince = null, IdleFloor = now }; break;
            case Command.Suspend: next = next with { Suspended = true }; break;
            case Command.Resume: next = next with { Suspended = false, AutoSince = null, IdleFloor = now }; break;
        }

        if (command == Command.Sample && !next.Paused && !next.Manual && !next.Locked && !next.Suspended)
        {
            if (previous.AutoSince.HasValue && lastInput > previous.AutoSince.Value)
                next = next with { AutoSince = null, AwaySince = null, IdleFloor = now };
            else if (!previous.AutoSince.HasValue && settings.AutomaticAfk &&
                     now - lastInput >= settings.AfkMinutes * 60000)
            {
                next = next with { AutoSince = lastInput, AwaySince = lastInput };
                away = new(lastInput, now, Presence.AutomaticAfk.ToString());
            }
        }
        if (next.Presence != Presence.Active && !next.AwaySince.HasValue) next = next with { AwaySince = now };
        if (next.Presence == Presence.Active) next = next with { AwaySince = null };

        StatusChange? notice = null;
        if (next.Presence != previous.Presence || next.Paused != previous.Paused)
            notice = new(next.Presence, next.Paused, next.AwaySince ?? now);
        else if (command == Command.ToggleAfk && previous.Paused)
            notice = new(next.Presence, true, now, "Учёт приостановлен");

        return new(next, slice, away, notice, previous.Paused && now > previous.At &&
            (now - previous.At <= 5000 || previous.Suspended || previous.Locked)
            ? new(previous.At, now) : null);
    }
}
