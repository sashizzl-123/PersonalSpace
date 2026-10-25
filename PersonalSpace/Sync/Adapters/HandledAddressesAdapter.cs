using System;
using System.Collections.Generic;
using Dalamud.Plugin.Ipc;

namespace PersonalSpace.Sync.Adapters;

/// <summary>
/// Mare-family clients (Lightless, Snowcloak, PlayerSync/MareSempiterne) all expose
/// <c>&lt;Prefix&gt;.GetHandledAddresses</c>: the game object addresses of the remote pairs they're currently handling.
/// That's public, read-only IPC that needs no registration, and the only thing we call.
/// </summary>
public sealed class HandledAddressesAdapter : ISyncAdapter
{
    private readonly ICallGateSubscriber<List<nint>> getHandledAddresses;
    private bool loggedFailure;

    public HandledAddressesAdapter(string name, string internalName, string ipcPrefix, SyncSupport support, bool verified, string note)
    {
        Name = name;
        InternalName = internalName;
        Support = support;
        Verified = verified;
        Note = note;
        getHandledAddresses = Plugin.PluginInterface.GetIpcSubscriber<List<nint>>($"{ipcPrefix}.GetHandledAddresses");
    }

    public string Name { get; }
    public string InternalName { get; }
    public SyncSupport Support { get; }
    public bool Verified { get; }
    public string Note { get; }
    public bool IsAvailable { get; set; }
    public bool CanReadPairs => true;
    public int NearbyPairs { get; private set; }

    public void CollectPairs(HashSet<nint> into)
    {
        NearbyPairs = 0;
        if (!IsAvailable)
            return;

        try
        {
            var addresses = getHandledAddresses.InvokeFunc();
            foreach (var address in addresses)
            {
                if (address != nint.Zero && into.Add(address))
                    NearbyPairs++;
            }

            loggedFailure = false;
        }
        catch (Exception ex)
        {
            // Usually just "not ready yet" while the client is still starting up.
            if (!loggedFailure)
            {
                Plugin.Log.Debug($"{Name}: GetHandledAddresses unavailable ({ex.GetType().Name}: {ex.Message})");
                loggedFailure = true;
            }
        }
    }
}
