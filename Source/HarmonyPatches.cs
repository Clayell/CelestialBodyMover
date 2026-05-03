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

    // TODO: the stock jool system is unstable, laythe and vall get thrown out when using high time warp.
    // TODO: make the bodys' orbits have escape and encounter map icons
    [HarmonyPatch(typeof(OrbitDriver))]
    public static class OrbitDriverPatches
    {
        internal static readonly FieldInfo readyField = AccessTools.Field(typeof(OrbitDriver), "ready");
        internal static readonly FieldInfo fdtLastField = AccessTools.Field(typeof(OrbitDriver), "fdtLast");
        internal static readonly FieldInfo isHyperbolicField = AccessTools.Field(typeof(OrbitDriver), "isHyperbolic");

        [HarmonyPatch(nameof(OrbitDriver.UpdateOrbit))]
        [HarmonyPrefix]
        public static bool Prefix_UpdateOrbit(ref OrbitDriver __instance, bool offset = true)
        { // KSP has no idea how to handle soi transitions for non-vessels, so we'll make this patch be always active
            //Util.Log($"patching update orbit");
            if (readyField == null)
            {
                Util.LogWarning("readyField is null!");
                return true;
            }

            bool ready = (bool)readyField.GetValue(__instance);

            if (fdtLastField == null)
            {
                Util.LogWarning("fdtLastField is null!");
                return true;
            }

            double fdtLast = (double)fdtLastField.GetValue(__instance);

            if (isHyperbolicField == null)
            {
                Util.LogWarning("isHyperbolicField is null!");
                return true;
            }

            bool isHyperbolic = (bool)isHyperbolicField.GetValue(__instance);

            // now begins the actual method

            if (!ready)
            {
                return false;
            }
            __instance.lastMode = __instance.updateMode;
            //Util.Log($"UpdateOrbit: going into switch, case: {__instance.updateMode}, instance: {__instance.name}");
            switch (__instance.updateMode)
            {
                case OrbitDriver.UpdateMode.UPDATE:
                    __instance.updateFromParameters();
                    __instance.CheckDominantBody(__instance.referenceBody.position + __instance.pos);
                    break;
                case OrbitDriver.UpdateMode.TRACK_Phys:
                case OrbitDriver.UpdateMode.IDLE:
                    if (!offset)
                    {
                        fdtLastField.SetValue(__instance, -0d);
                    }
                    if (!__instance.CheckDominantBody(__instance.referenceBody.position + __instance.pos))
                    {
                        __instance.TrackRigidbody(__instance.referenceBody, -fdtLast);
                    }
                    break;
            }
            fdtLastField.SetValue(__instance, TimeWarp.fixedDeltaTime);
            if (isHyperbolic && __instance.orbit.eccentricity < 1d)
            {
                isHyperbolicField.SetValue(__instance, false);
                if (__instance.vessel != null)
                {
                    GameEvents.onVesselOrbitClosed.Fire(__instance.vessel);
                }
            }
            if (!isHyperbolic && __instance.orbit.eccentricity > 1d)
            {
                isHyperbolicField.SetValue(__instance, true);
                if (__instance.vessel != null)
                {
                    GameEvents.onVesselOrbitEscaped.Fire(__instance.vessel);
                }
            }
            if (__instance.drawOrbit)
            {
                __instance.orbit.DrawOrbit();
            }
            return false;
        }

        // TODO, make this not a harmony patch and instead just a normal method, since CheckDominantBody is only ever called in UpdateOrbit
        // however, if any other mods call CheckDominantBody, the behavior will be incorrect
        [HarmonyPatch(nameof(OrbitDriver.CheckDominantBody))]
        [HarmonyPrefix]
        public static bool Prefix_CheckDominantBody(ref OrbitDriver __instance, ref bool __result, Vector3d refPos)
        {
            Vector3d relPos = refPos - __instance.referenceBody.position;
            if (__instance.celestialBody != null)
            {
                if (__instance.celestialBody.isStar || !(bool)__instance.celestialBody.orbitDriver)
                {
                    __result = false;
                    return false;
                }

                CelestialBody mainBody = InSOI(refPos, FlightGlobals.fetch.bodies[0], __instance.celestialBody);
                //Util.Log($"CheckDominantBody: instance: {__instance.name}, referenceBody: {__instance.referenceBody}, new mainBody: {mainBody}, refPos: {refPos} (mag: {refPos.magnitude:E17}), relative pos: {relPos} (mag: {relPos.magnitude:E17}), soi of ref body: {__instance.referenceBody?.sphereOfInfluence:E17}");

                if (__instance.referenceBody != mainBody && mainBody != __instance.celestialBody && __instance.celestialBody.Mass < mainBody.Mass && !__instance.celestialBody.HasChild(mainBody) && InHillSphereAndSOI(__instance.celestialBody, mainBody))
                {
                    //Util.Log($"recalculating a body orbit, instance: {__instance.name}");
                    __instance.RecalculateOrbit(mainBody);
                    __result = true;
                    return false;
                }
            }
            else
            {
                CelestialBody mainBody = FlightGlobals.getMainBody(refPos);
                //Util.Log($"CheckDominantBody: instance: {__instance.name}, referenceBody: {__instance.referenceBody}, new mainBody: {mainBody}, refPos: {refPos} (mag: {refPos.magnitude:E17}), relative pos: {relPos} (mag: {relPos.magnitude:E17}), soi of ref body: {__instance.referenceBody?.sphereOfInfluence:E17}, hillsphere: {GetHillSphere(mainBody):E17}");

                if (__instance.referenceBody != mainBody && !FlightGlobals.overrideOrbit)
                {
                    //Util.Log($"recalculating a vessel orbit, instance: {__instance.name}");
                    __instance.RecalculateOrbit(mainBody);
                    __result = true;
                    return false;
                }
            }

            __result = false;
            return false;
        }

        private static bool InHillSphereAndSOI(CelestialBody child, CelestialBody newParent)
        {
            // the stock CelestialBody.hillSphere is incorrect in multiple ways

            CelestialBody grandParent = newParent.referenceBody;

            if (child == null || newParent == null)
            {
                return false;
            }
            if (newParent.isStar || grandParent == null || newParent.orbit == null)
            {
                return true;
            }

            double hillSphere = newParent.orbit.radius * Math.Pow(newParent.Mass / (3d * (grandParent.Mass + newParent.Mass)), 1d / 3d);
            double SOI = newParent.sphereOfInfluence;
            double distance = (child.position - newParent.position).magnitude;
            //Util.Log($"InHillSphere: hillSphere: {hillSphere:E17}, distance: {distance:E17}, SOI: {SOI:E17}, result: {distance < Math.Min(hillSphere, SOI)}");

            return distance < Math.Min(hillSphere, SOI);
        }

        //private static double GetHillSphere(CelestialBody body)
        //{
        //    CelestialBody parent = body?.referenceBody;

        //    if (body == null || parent == null || body.orbit == null)
        //    {
        //        return double.NaN;
        //    }

        //    double hillSphere = body.orbit.radius * Math.Pow(body.Mass / (3d * (parent.Mass + body.Mass)), 1d / 3d);
        //    //Util.Log($"GetHillSphere: hillSphere: {hillSphere:E17}");

        //    return hillSphere;
        //}

        private static CelestialBody InSOI(Vector3d pos, CelestialBody body, CelestialBody testBody)
        { // we could patch FlightGlobals.inSOI, but we only need to use it for this one thing
            int count = body.orbitingBodies.Count;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    CelestialBody celestialBody = body.orbitingBodies[i];
                    if (celestialBody == testBody)
                    {
                        continue;
                    }
                    if ((pos - celestialBody.position).sqrMagnitude < celestialBody.sphereOfInfluence * celestialBody.sphereOfInfluence)
                    {
                        return InSOI(pos, celestialBody, testBody);
                    }
                }
            }
            return body;
        }
    }

    // steamroller:
    // Take a look at how BurstPQS integrates with parallax continued for an example of how to patch something without requiring a dependency on it
    // https://github.com/Phantomical/BurstPQS/blob/4d3830eb495e2d3f071d8e8869a598a51af25e34/src/BurstPQS.ParallaxContinued/HarmonyPatcher.cs#L4 
    // Basically you add a second DLL that depends on it, and then it can call back with APIs into your main one

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
