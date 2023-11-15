﻿using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using PluginInfo = ItemQuickSwitchMod.PluginInfo;

// ReSharper disable InconsistentNaming

namespace ItemQuickSwitchMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class ItemQuickSwitchMod : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            // Creating configurable bindings:
            Debug.Log("creating Binds!");
            foreach (var action in CustomAction.AllActions)
            {
                var bind = Config.Bind(
                    "Bindings",
                    action.Id,
                    action.Shortcut,
                    action.Description
                );
                action.ConfigEntry = bind;
            }

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB))]
    public class PlayerControllerB_Patch
    {
        private static readonly Dictionary<string, MethodInfo> MethodCache = new();
        private static readonly Dictionary<string, FieldInfo> FieldCache = new();

        // parameter arrays for switchItemRpc method:
        private static readonly object[] backwardsParam = new object[1] { false };
        private static readonly object[] forwardsParam = new object[1] { true };

        private static MethodInfo GetPrivateMethod(string name)
        {
            if (MethodCache.TryGetValue(name, out var method)) return method;
            method = typeof(PlayerControllerB).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                var ex = new NullReferenceException($"Method {name} could not be found!");
                Debug.LogException(ex);
                throw ex;
            }

            MethodCache[name] = method;
            return method;
        }

        private static FieldInfo GetPrivateField(string name)
        {
            if (FieldCache.TryGetValue(name, out var field)) return field;
            field = typeof(PlayerControllerB).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                var ex = new NullReferenceException($"Field {name} could not be found!");
                Debug.LogException(ex);
                throw ex;
            }

            FieldCache[name] = field;
            return field;
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void PlayerControllerB_Update(PlayerControllerB __instance)
        {
            // early exit copy pasted from LC source. Might be autogenerated BS...
            if ((!__instance.IsOwner || !__instance.isPlayerControlled ||
                 __instance.IsServer && !__instance.isHostPlayerObject)
                && !__instance.isTestingPlayer) return;

            var keyDown = Array.Find(CustomAction.AllActions, it =>
                Keyboard.current[it.ConfigEntry.Value].wasPressedThisFrame
            );
            if (keyDown == null) return;
            if (keyDown == CustomAction.Emote1)
            {
                PerformEmote(__instance, 1);
                Debug.Log("You should be dancing now!");
                return;
            }

            if (keyDown == CustomAction.Emote2)
            {
                PerformEmote(__instance, 2);
                Debug.Log("You should be pointing now!");
                return;
            }

            stopEmotes(__instance);
            switchItemSlots(__instance, keyDown.SlotNumber);
            Debug.Log($"You should be holding item {keyDown.SlotNumber + 1} now!");
        }

        private static void PerformEmote(PlayerControllerB __instance, int emoteId)
        {
            __instance.timeSinceStartingEmote = 0.0f;
            __instance.performingEmote = true;
            __instance.playerBodyAnimator.SetInteger("emoteNumber", emoteId);
            __instance.StartPerformingEmoteServerRpc();
        }

        private static void switchItemSlots(PlayerControllerB __instance, int requestedSlot)
        {
            if (!isItemSwitchPossible(__instance) || __instance.currentItemSlot == requestedSlot)
            {
                return;
            }

            // this whole upcoming section is more complicated than it need to be but whatever
            var distance = __instance.currentItemSlot - requestedSlot;
            if (distance > 0)
            {
                if (distance == 3)
                {
                    // instead of looping we can just switch one slot forwards
                    GetPrivateMethod("SwitchItemSlotsServerRpc").Invoke(__instance, forwardsParam);
                }
                else
                {
                    do
                    {
                        // yes I'm one of the 3 people in the world who use do while loops
                        GetPrivateMethod("SwitchItemSlotsServerRpc").Invoke(__instance, backwardsParam);
                        distance--;
                    } while (distance != 0);
                }
            }
            else
            {
                if (distance == -3)
                {
                    // instead of looping we can just switch one slot backwards
                    GetPrivateMethod("SwitchItemSlotsServerRpc").Invoke(__instance, backwardsParam);
                }
                else
                {
                    do
                    {
                        GetPrivateMethod("SwitchItemSlotsServerRpc").Invoke(__instance, forwardsParam);
                        distance++;
                    } while (distance != 0);
                }
            }
            ShipBuildModeManager.Instance.CancelBuildMode();
            __instance.playerBodyAnimator.SetBool("GrabValidated", false);
            var switchItemParams = new object[] { requestedSlot, __instance.ItemSlots[requestedSlot] };
            GetPrivateMethod("SwitchToItemSlot").Invoke(__instance, switchItemParams);
            if (__instance.currentlyHeldObjectServer != null)
                __instance.currentlyHeldObjectServer.gameObject.GetComponent<AudioSource>().PlayOneShot(__instance.currentlyHeldObjectServer.itemProperties.grabSFX, 0.6f);
            GetPrivateField("timeSinceSwitchingSlots").SetValue(__instance,0.0f);
        }

        private static bool isItemSwitchPossible(PlayerControllerB __instance)
        {
            var switchSlotsTimer = (float)GetPrivateField("timeSinceSwitchingSlots").GetValue(__instance);
            var throwingObject = (bool)GetPrivateField("throwingObject").GetValue(__instance);
            // flags shamelessly stolen from base game code
            return !(switchSlotsTimer < 0.01 // this was like 0.3 or smth but that makes it sluggish
                     || __instance.inTerminalMenu || __instance.isGrabbingObjectAnimation ||
                     __instance.inSpecialInteractAnimation || throwingObject || __instance.isTypingChat ||
                     __instance.twoHanded || __instance.activatingItem || __instance.jetpackControls ||
                     __instance.disablingJetpackControls);
        }

        private static void stopEmotes(PlayerControllerB __instance)
        {
            __instance.performingEmote = false;
            __instance.StopPerformingEmoteServerRpc();
            __instance.timeSinceStartingEmote = 0.0f;
        }
    }
}