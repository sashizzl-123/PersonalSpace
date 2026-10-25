using System.Collections.Generic;

namespace PersonalSpace.Sync;

/// <summary>What Sync Shield can do for a given sync client.</summary>
public enum SyncSupport
{
    /// <summary>The client has a local, client-side pause IPC we can call. No current client offers one.</summary>
    Supported,

    /// <summary>
    /// The client applies mods through a Penumbra temporary collection, so Sync Shield can swap a shielded pair to an
    /// empty collection and they show as vanilla. See <see cref="PenumbraShield"/>.
    /// </summary>
    PenumbraSwap,

    /// <summary>The client doesn't tell other plugins who its pairs are, so Sync Shield can't act on it at all.</summary>
    NoEffect,
}

/// <summary>
/// One sync client (Mare-family or otherwise). Adapters only use the client's public IPC: they tell Sync Shield which
/// nearby players are pairs, and <see cref="PenumbraShield"/> does the rest through Penumbra.
/// </summary>
public interface ISyncAdapter
{
    string Name { get; }

    /// <summary>Matched against <see cref="Dalamud.Plugin.IExposedPlugin.InternalName"/>.</summary>
    string InternalName { get; }

    SyncSupport Support { get; }

    /// <summary>
    /// True when the client's behaviour was read from its source code. False when it was inferred
    /// (e.g. a closed-source client that was only decompiled).
    /// </summary>
    bool Verified { get; }

    /// <summary>Explanation for the status tooltip.</summary>
    string Note { get; }

    /// <summary>Installed and loaded.</summary>
    bool IsAvailable { get; set; }

    /// <summary>Whether this client tells us who its pairs are. If not, Sync Shield can't act on it.</summary>
    bool CanReadPairs { get; }

    /// <summary>Pairs found nearby at the last poll.</summary>
    int NearbyPairs { get; }

    /// <summary>Adds the game object addresses of this client's currently handled pairs.</summary>
    void CollectPairs(HashSet<nint> into);
}
