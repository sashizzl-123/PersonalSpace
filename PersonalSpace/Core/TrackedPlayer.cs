namespace PersonalSpace.Core;

/// <summary>
/// Per-player state kept across frames. One instance per nearby player, reused every frame.
/// </summary>
public sealed class TrackedPlayer(ulong gameObjectId)
{
    public ulong GameObjectId { get; } = gameObjectId;

    /// <summary>Native GameObject address. Only valid during the frame in <see cref="LastSeenFrame"/>.</summary>
    public nint Address { get; internal set; }

    /// <summary>Squared XZ distance to the local player, in yalms².</summary>
    public float DistanceSq { get; internal set; }

    /// <summary>Outside the bubble by distance (with hysteresis). Ignores exemptions.</summary>
    public bool IsOutside { get; internal set; }

    /// <summary>Never faded, name-hidden or sync-paused, regardless of distance.</summary>
    public bool IsExempt { get; internal set; }

    /// <summary><see cref="System.Environment.TickCount64"/> when the player last crossed outward, or 0 while inside.</summary>
    public long OutsideSinceTick { get; internal set; }

    public uint LastSeenFrame { get; internal set; }

    /// <summary>A synced pair whose mods Sync Shield is currently swapping for vanilla through Penumbra.</summary>
    public bool IsSyncShielded { get; internal set; }

    /// <summary>On the Always-show list. Cached; recomputed only when the list changes.</summary>
    internal bool IsAlwaysShown { get; set; }

    /// <summary>Which version of the Always-show list <see cref="IsAlwaysShown"/> was computed against.</summary>
    internal int AlwaysShowVersion { get; set; }

    /// <summary>Beyond the hide-beyond radius (with hysteresis). Only meaningful while <see cref="IsOutside"/>.</summary>
    public bool IsFar { get; internal set; }

    /// <summary>Pushed out of the translucent cap: too many closer players are already translucent.</summary>
    public bool IsCapped { get; internal set; }

    /// <summary>Whether this player should currently be treated as outside the bubble.</summary>
    public bool ShouldHide => IsOutside && !IsExempt;

    /// <summary>Outside and, for performance, not drawn at all rather than faded.</summary>
    public bool ShouldHideFully => ShouldHide && (IsFar || IsCapped);
}
