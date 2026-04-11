using HarmonyLib;
using System;
using UnityEngine;

namespace CelestialBodyMover
{
    [HarmonyPatch(typeof(Orbit))]
    public static class OrbitPatches
    {
        [HarmonyPatch(nameof(Orbit.UpdateFromFixedVectors))]
        [HarmonyPrefix]
        public static bool Prefix_UpdateFromFixedVectors(ref Orbit __instance, Vector3d pos, Vector3d vel, CelestialBody refBody, double UT)
        {
            //Util.Log($"isActive: {CelestialBodyMover.Instance.isActive}");
            if (CelestialBodyMover.Instance.isActive && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.mainBody != null && __instance == FlightGlobals.ActiveVessel.mainBody.orbit)
            {
                //Util.Log($"Hyjacking UpdateFromFixedVectors for {FlightGlobals.ActiveVessel.mainBody.displayName}");

                // this is entirely copied from Orbit.UpdateFromFixedVectors, with the exception of stopping any changes to anything involving position

                __instance.referenceBody = refBody;
                __instance.h = Vector3d.Cross(pos, vel);
                if (__instance.h.sqrMagnitude.Equals(0.0))
                {
                    __instance.inclination = Math.Acos(pos.z / pos.magnitude) * (180.0 / Math.PI);
                    __instance.an = Vector3d.Cross(pos, Vector3d.forward);
                }
                else
                {
                    __instance.an = Vector3d.Cross(Vector3d.forward, __instance.h);
                    __instance.OrbitFrame.Z = __instance.h / __instance.h.magnitude;
                    __instance.inclination = UtilMath.AngleBetween(__instance.OrbitFrame.Z, Vector3d.forward) * (180.0 / Math.PI);
                }
                if (__instance.an.sqrMagnitude.Equals(0.0))
                {
                    __instance.an = Vector3d.right;
                }
                __instance.LAN = Math.Atan2(__instance.an.y, __instance.an.x) * (180.0 / Math.PI);
                __instance.LAN = (__instance.LAN + 360.0) % 360.0;
                __instance.eccVec = (Vector3d.Dot(vel, vel) / refBody.gravParameter - 1.0 / pos.magnitude) * pos - Vector3d.Dot(pos, vel) * vel / refBody.gravParameter;
                __instance.eccentricity = __instance.eccVec.magnitude;
                __instance.orbitalEnergy = vel.sqrMagnitude / 2.0 - refBody.gravParameter / pos.magnitude;
                __instance.semiMajorAxis = ((__instance.eccentricity < 1.0) ? ((0.0 - refBody.gravParameter) / (2.0 * __instance.orbitalEnergy)) : ((0.0 - __instance.semiLatusRectum) / (__instance.eccVec.sqrMagnitude - 1.0)));
                if (__instance.eccentricity.Equals(0.0))
                {
                    __instance.OrbitFrame.X = __instance.an.normalized;
                    __instance.argumentOfPeriapsis = 0.0;
                }
                else
                {
                    __instance.OrbitFrame.X = __instance.eccVec.normalized;
                    __instance.argumentOfPeriapsis = UtilMath.AngleBetween(__instance.an, __instance.OrbitFrame.X);
                    if (Vector3d.Dot(Vector3d.Cross(__instance.an, __instance.OrbitFrame.X), __instance.h) < 0.0)
                    {
                        __instance.argumentOfPeriapsis = Math.PI * 2.0 - __instance.argumentOfPeriapsis;
                    }
                }
                if (__instance.h.sqrMagnitude.Equals(0.0))
                {
                    __instance.OrbitFrame.Y = __instance.an.normalized;
                    __instance.OrbitFrame.Z = Vector3d.Cross(__instance.OrbitFrame.X, __instance.OrbitFrame.Y);
                }
                else
                {
                    __instance.OrbitFrame.Y = Vector3d.Cross(__instance.OrbitFrame.Z, __instance.OrbitFrame.X);
                }
                __instance.argumentOfPeriapsis *= 180.0 / Math.PI;
                __instance.meanMotion = __instance.GetMeanMotion(__instance.semiMajorAxis);

                /* prevent changing of anything relating to position
                double x = Vector3d.Dot(pos, OrbitFrame.X);
                double y = Vector3d.Dot(pos, OrbitFrame.Y);
                trueAnomaly = Math.Atan2(y, x);
                eccentricAnomaly = GetEccentricAnomaly(trueAnomaly);
                meanAnomaly = GetMeanAnomaly(eccentricAnomaly);
                meanAnomalyAtEpoch = meanAnomaly;
                ObT = meanAnomaly / meanMotion;
                ObTAtEpoch = ObT;
                */

                if (__instance.eccentricity < 1.0)
                {
                    __instance.period = Math.PI * 2.0 / __instance.meanMotion;
                    __instance.orbitPercent = __instance.meanAnomaly / (Math.PI * 2.0);
                    __instance.orbitPercent = (__instance.orbitPercent + 1.0) % 1.0;
                    __instance.timeToPe = (__instance.period - __instance.ObT) % __instance.period;
                    __instance.timeToAp = __instance.timeToPe - __instance.period / 2.0;
                    if (__instance.timeToAp < 0.0)
                    {
                        __instance.timeToAp += __instance.period;
                    }
                }
                else
                {
                    __instance.period = double.PositiveInfinity;
                    __instance.orbitPercent = 0.0;
                    __instance.timeToPe = 0.0 - __instance.ObT;
                    __instance.timeToAp = double.PositiveInfinity;
                }
                __instance.radius = pos.magnitude;
                __instance.altitude = __instance.radius - refBody.Radius;
                __instance.epoch = UT;
                __instance.pos = Planetarium.Zup.WorldToLocal(pos);
                __instance.vel = Planetarium.Zup.WorldToLocal(vel);
                __instance.h = Planetarium.Zup.WorldToLocal(__instance.h);
                __instance.debugPos = __instance.pos;
                __instance.debugVel = __instance.vel;
                __instance.debugH = __instance.h;
                __instance.debugAN = __instance.an;
                __instance.debugEccVec = __instance.eccVec;
                __instance.OrbitFrameX = __instance.OrbitFrame.X;
                __instance.OrbitFrameY = __instance.OrbitFrame.Y;
                __instance.OrbitFrameZ = __instance.OrbitFrame.Z;

                return false;
            }
            else
            {
                return true;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyPatcher : MonoBehaviour
    {
        void Start()
        {
            var harmony = new Harmony("CelestialBodyMover.HarmonyPatcher");
            harmony.PatchAll();
        }
    }
}
