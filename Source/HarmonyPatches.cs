using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CelestialBodyMover
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyPatcher : MonoBehaviour
    {
        void Start()
        {
            var harmony = new Harmony("CelestialBodyMover.HarmonyPatcher");
            harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(DeltaVPartInfo))]
    public static class DeltaVPartInfoPatches
    {
        internal static bool ValidSituation() => CelestialBodyMover.Instance.isActive && CelestialBodyMover.Instance.isFrozen && CelestialBodyMover.Instance.includeBodyMass;

        [HarmonyPatch(nameof(DeltaVPartInfo.CalculateMassValues))]
        [HarmonyPostfix]
        public static void Postfix_CalculateMassValues(ref DeltaVPartInfo __instance)
        {
            if (!ValidSituation() || !Util.IsFlight() || !Util.GetBodyOrbit(FlightGlobals.ActiveVessel?.mainBody, out _)) return;

            if (__instance.vesselDeltaV.Vessel.rootPart.persistentId != __instance.part.persistentId) return;

            __instance.dryMass += (float)__instance.vesselDeltaV.Vessel.mainBody.Mass / 1000f; // convert from kg to tons
        }
    }
}
