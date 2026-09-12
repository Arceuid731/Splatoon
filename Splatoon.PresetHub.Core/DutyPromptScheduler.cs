namespace Splatoon.PresetHub.Core;

/// <summary>Keep a visit pending until the player and the catalogue are ready.</summary>
public sealed class DutyPromptScheduler
{
    private uint pending;

    public void Schedule(uint territoryId) => pending = territoryId;

    public bool ShouldCheck(uint currentTerritory, bool ready, bool enabled)
    {
        if(currentTerritory != pending) pending = 0;
        return pending != 0 && ready && enabled;
    }

    public void CompleteCheck(bool hasSuggestions, bool syncing)
    {
        // A usable cache can be shown immediately. Empty caches wait for the refresh.
        if(hasSuggestions || !syncing) pending = 0;
    }
}
