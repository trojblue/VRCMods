﻿using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FriendsPlusHome;
using HarmonyLib;
using MelonLoader;
using UIExpansionKit.API;
using UnhollowerRuntimeLib.XrefScans;
using UnityEngine;
using VRC.Core;

[assembly: MelonInfo(typeof(FriendsPlusHomeMod), "Friends+ Home", "1.1.2", "knah, P a t c h e d   P l u s +", "https://github.com/knah/VRCMods")]
[assembly: MelonGame("VRChat", "VRChat")]
[assembly: MelonOptionalDependencies("UIExpansionKit")]

namespace FriendsPlusHome
{
    internal class FriendsPlusHomeMod : MelonMod
    {
        private const string SettingsCategory = "FriendsPlusHome";
        private const string SettingStartupName = "StartupWorldType";
        private const string SettingButtonName = "GoHomeWorldType";

        private static MelonPreferences_Entry<string> StartupName;
        private static MelonPreferences_Entry<string> ButtonName;

        private static Func<VRCUiManager> ourGetUiManager;
        private static Func<QuickMenu> ourGetQuickMenu;

        static FriendsPlusHomeMod()
        {

            ourGetUiManager = (Func<VRCUiManager>)Delegate.CreateDelegate(typeof(Func<VRCUiManager>), typeof(VRCUiManager)
                .GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .First(it => it.PropertyType == typeof(VRCUiManager)).GetMethod);
            ourGetQuickMenu = (Func<QuickMenu>)Delegate.CreateDelegate(typeof(Func<QuickMenu>), typeof(QuickMenu)
                .GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .First(it => it.PropertyType == typeof(QuickMenu)).GetMethod);

        }

        internal static VRCUiManager GetUiManager() => ourGetUiManager();
        internal static QuickMenu GetQuickMenu() => ourGetQuickMenu();

        public override void OnApplicationStart()
        {
            var category = MelonPreferences.CreateCategory(SettingsCategory, "Friends+ Home");
            StartupName = category.CreateEntry(SettingStartupName, nameof(InstanceAccessType.FriendsOfGuests), "Startup instance type");
            ButtonName = category.CreateEntry(SettingButtonName, nameof(InstanceAccessType.FriendsOfGuests), "\"Go Home\" instance type");

            if (MelonHandler.Mods.Any(it => it.Info.Name == "UI Expansion Kit" && !it.Info.Version.StartsWith("0.1.")))
                RegisterUix2Extension();

            foreach (var methodInfo in typeof(VRCFlowManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (methodInfo.ReturnType != typeof(void) || methodInfo.GetParameters().Length != 0)
                    continue;

                if (!XrefScanner.XrefScan(methodInfo).Any(it => it.Type == XrefType.Global && it.ReadAsObject()?.ToString() == "Going to Home Location: "))
                    continue;

                MelonLogger.Msg($"Patched {methodInfo.Name}");
                HarmonyInstance.Patch(methodInfo, postfix: new HarmonyMethod(AccessTools.Method(typeof(FriendsPlusHomeMod), nameof(GoHomePatch))));
            }

            DoAfterUiManagerInit(OnUiManagerInit);
        }
        private static void DoAfterUiManagerInit(Action code)
        {
            MelonCoroutines.Start(OnUiManagerInitCoro(code));
        }

        private static IEnumerator OnUiManagerInitCoro(Action code)
        {
            while (VRCUiManager.prop_VRCUiManager_0 == null)
                yield return null;
            code();
        }

        private void OnUiManagerInit()
        {
            StartEnforcingInstanceType(VRCFlowManager.field_Private_Static_VRCFlowManager_0, false); // just in case startup is slow
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterUix2Extension()
        {
            var possibleValues = new[]
            {
                (nameof(InstanceAccessType.Public), "Public"),
                (nameof(InstanceAccessType.FriendsOfGuests), "Friends+"),
                (nameof(InstanceAccessType.FriendsOnly), "Friends only"),
                (nameof(InstanceAccessType.InvitePlus), "Invite+"),
                (nameof(InstanceAccessType.InviteOnly), "Invite only"),
            };
            ExpansionKitApi.RegisterSettingAsStringEnum(SettingsCategory, SettingStartupName, possibleValues);
            ExpansionKitApi.RegisterSettingAsStringEnum(SettingsCategory, SettingButtonName, possibleValues);
        }

        private static void GoHomePatch(VRCFlowManagerVRC __instance)
        {
            StartEnforcingInstanceType(__instance, true);
        }

        private static void StartEnforcingInstanceType(VRCFlowManager flowManager, bool isButton)
        {
            var targetType = Enum.TryParse<InstanceAccessType>(isButton ? ButtonName.Value : StartupName.Value, out var type) ? type : InstanceAccessType.FriendsOfGuests;
            MelonLogger.Msg($"Enforcing home instance type: {targetType}");
            flowManager.field_Protected_InstanceAccessType_0 = targetType;

            MelonCoroutines.Start(EnforceTargetInstanceType(flowManager, targetType, isButton ? 10 : 30));
        }

        private static int ourRequestId;

        private static IEnumerator EnforceTargetInstanceType(VRCFlowManager manager, InstanceAccessType type, float time)
        {
            var endTime = Time.time + time;
            var currentRequestId = ++ourRequestId;
            while (Time.time < endTime && ourRequestId == currentRequestId)
            {
                manager.field_Protected_InstanceAccessType_0 = type;
                yield return null;
            }
        }
    }
}