using System.Diagnostics.CodeAnalysis;
using FrooxEngine;
using HarmonyLib;

namespace DynTunes.Patches;

[HarmonyPatch(typeof(UserRoot))]
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "UnusedType.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Local")]
[SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
[SuppressMessage("ReSharper", "VariableCanBeNotNullable")]
public class UserRootPatches
{
    private static Slot GetOrCreateDynTunesSlot(Slot userRootSlot)
    {
        string slotName = DynTunes.SlotName;
        Slot? dynTunesSlot = userRootSlot.FindChild(s => s.Name == slotName);
        if (dynTunesSlot == null)
        {
            dynTunesSlot = userRootSlot.AddSlot(slotName);
        }
        return dynTunesSlot;
    }

    private static bool WriteOrAttachDynVar<T>(Slot userRootSlot, Slot dynTunesSlot, string name, T value)
    {
        // Use the original KeySpace to create the variable name (e.g., "User/Music_Title")
        string varName = $"{DynTunes.KeySpace}/{name}";
        
        // Ensure the variable component exists on the DynTunes slot with the original variable name
        DynamicValueVariable<T> variable = dynTunesSlot.GetComponentOrAttach<DynamicValueVariable<T>>(x => x.VariableName.Value == varName);
        
        // Set the variable name if it's not already set
        if (variable.VariableName.Value != varName)
        {
            variable.VariableName.Value = varName;
        }
        
        // Update the variable value directly
        variable.Value.Value = value;
        
        // Also try to write to the space for compatibility
        DynamicVariableSpace? space = userRootSlot.FindSpace(DynTunes.KeySpace);
        if (space != null)
        {
            space.TryWriteValue(varName, value);
        }
        
        return true;
    }

			
    [HarmonyPatch("OnCommonUpdate")]
    [HarmonyPostfix]
    private static void OnCommonUpdate(UserRoot __instance)
    {
        if (__instance == null || __instance.ActiveUser != __instance.LocalUser || __instance.World.IsUserspace()) return;

        Slot dynTunesSlot = GetOrCreateDynTunesSlot(__instance.Slot);
        
        MediaPlayerState state = DynTunes.Connector.GetState();

        bool failed = false;

        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.Title, state.Title);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.Artist, state.Artist);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.Album, state.Album);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.AlbumArtUrl, state.AlbumArtUrl);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.Playing, state.IsPlaying);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.Position, state.PositionSeconds);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.Length, state.LengthSeconds);
        failed |= !WriteOrAttachDynVar(__instance.Slot, dynTunesSlot, DynTunes.KeyPrefix + DynTunes.IsConnected, state.IsConnected);

        _ = failed; // Todo: warn
    }
}