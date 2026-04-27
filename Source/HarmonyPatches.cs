using HarmonyLib;
using System;
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
        [HarmonyPatch(nameof(DeltaVPartInfo.CalculateMassValues))]
        [HarmonyPostfix]
        public static void Postfix_CalculateMassValues(ref DeltaVPartInfo __instance)
        {
            if (!Util.ValidSituation() || !Util.IsFlight() || !Util.GetBodyOrbit(FlightGlobals.ActiveVessel?.mainBody, out _, false)) return;

            if (__instance.vesselDeltaV.Vessel.rootPart.persistentId != __instance.part.persistentId) return;

            __instance.dryMass += (float)__instance.vesselDeltaV.Vessel.mainBody.Mass / 1000f; // convert from kg to tons
        }
    }

    //[HarmonyPatch]
    //public static class BackgroundThrustOrbitMathPatches
    //{
    //    static MethodBase TargetMethod()
    //    {
    //        Type type = AccessTools.TypeByName("BackgroundThrust.OrbitMath");
    //        if (type == null) return null;

    //        return AccessTools.Method(type, "IntegrateThrust", new Type[]
    //        { 
    //            AccessTools.TypeByName("BackgroundThrust.BackgroundThrustVessel"),
    //            AccessTools.TypeByName("BackgroundThrust.ThrustParameters")
    //        });
    //    }

    //    [HarmonyPrefix]
    //    public static bool Prefix_IntegrateThrust()
    //    {
    //        Util.Log($"patching backgroundthrust's integrate thrust");
            
    //        if (CelestialBodyMover.Instance == null || !CelestialBodyMover.Instance.isActive || !CelestialBodyMover.Instance.isFrozen || !Util.IsFlight() || !Util.GetBodyOrbit(FlightGlobals.ActiveVessel?.mainBody, out _, false)) return true;

    //        Util.Log($"patch triggered");

    //        return false;
    //    }
    //}
}
