using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace MoveMapAppliesToMouse;

internal enum DragAxisMode
{
    PanViewTiltView,
    MoveLateralLongitudinal,
    MoveMapHorizontalVertical
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<DragAxisMode> DragAxisConfig = null!;
    public new static ManualLogSource Logger { get; private set; } = null!;
    
    private Harmony? Harmony { get; set; }
    
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        
        DragAxisConfig = Config.Bind(
            "Mouse map move mode",
            "Input axis bind to use",
            DragAxisMode.PanViewTiltView,
            "Choose which keybind axes to use to move map with mouse. Note that setting it to using Move Map"
            + "Horizontal and Vertical disables being able to use those axes without holding down left click (essentially"
            + "making them mouse only).\n\nDon't forget that you might need to invert your up/down mouse axis!"
        );
        
        DragAxisConfig.SettingChanged += (_, _) =>
        {
            if (Harmony == null)
                return;
            
            Logger.LogInfo($"Changing mouse map axis mode to {DragAxisConfig.Value}");
            
            Repatch();
        };
        
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Repatch();
    }
    
    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }
    
    private void Repatch()
    {
        Harmony?.UnpatchSelf();
        Harmony?.PatchAll();
    }
}

[HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.MapControls))]
internal static class Patches
{
    [HarmonyTranspiler]
    // ReSharper disable once UnusedMember.Local
    private static IEnumerable<CodeInstruction> MoveMapAppliesToMousePatch(IEnumerable<CodeInstruction> instructions)
    {
        var originalInstructions = instructions.ToList();
        var matcher = new CodeMatcher(new List<CodeInstruction>(originalInstructions));
        
        var playerField = AccessTools.Field(typeof(DynamicMap), nameof(DynamicMap.player));
        var getAxis = AccessTools.Method(typeof(Player), nameof(Player.GetAxis), [typeof(string)]);
        var getMouseButton = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButton),
            [typeof(int)]);
        
        string horizontalAxis;
        string verticalAxis;
        
        switch (Plugin.DragAxisConfig.Value)
        {
            case DragAxisMode.MoveMapHorizontalVertical:
                horizontalAxis = "Move Map Horizontal";
                verticalAxis = "Move Map Vertical";
                break;
            case DragAxisMode.MoveLateralLongitudinal:
                horizontalAxis = "Move Lateral";
                verticalAxis = "Move Longitudinal";
                break;
            case DragAxisMode.PanViewTiltView:
            default:
                horizontalAxis = "Pan View";
                verticalAxis = "Tilt View";
                break;
        }
        
        if (Plugin.DragAxisConfig.Value == DragAxisMode.MoveMapHorizontalVertical)
        {
            matcher.MatchForward(
                true,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld, playerField),
                new CodeMatch(OpCodes.Ldstr, "Move Map Horizontal"),
                new CodeMatch(OpCodes.Callvirt, getAxis),
                new CodeMatch(OpCodes.Stloc_1)
            );
            
            if (matcher.IsInvalid)
            {
                Plugin.Logger.LogInfo("Failed to find a match for MoveMapHorizontalVertical.");
                return originalInstructions;
            }
            
            var afterLabel = matcher.InstructionAt(11).operand;
            
            matcher.Advance(6);
            matcher.Insert(new CodeInstruction(OpCodes.Br, afterLabel));
        }
        
        matcher.MatchForward(
            true,
            new CodeMatch(OpCodes.Ldc_I4_0),
            new CodeMatch(OpCodes.Call, getMouseButton),
            new CodeMatch(ci => ci.opcode == OpCodes.Brfalse || ci.opcode == OpCodes.Brfalse_S),
            new CodeMatch(OpCodes.Ldarg_0),
            new CodeMatch(OpCodes.Ldarg_0),
            new CodeMatch(OpCodes.Ldfld),
            new CodeMatch(OpCodes.Call),
            new CodeMatch(OpCodes.Add),
            new CodeMatch(OpCodes.Stfld),
            new CodeMatch(OpCodes.Ldarg_0),
            new CodeMatch(OpCodes.Ldfld),
            new CodeMatch(OpCodes.Ldstr)
        );
        
        if (matcher.IsInvalid)
        {
            Plugin.Logger.LogInfo($"Failed to find a match for {Plugin.DragAxisConfig.Value}.");
            return originalInstructions;
        }
        
        matcher.SetOperandAndAdvance(horizontalAxis);
        matcher.Advance(6);
        matcher.SetOperandAndAdvance(verticalAxis);
        
        Plugin.Logger.LogInfo($"Applied MoveMapAppliesToMousePatch in mode: {Plugin.DragAxisConfig.Value}.");
        
        return matcher.InstructionEnumeration();
    }
}