//using BackgroundThrust;
using ClickThroughFix;
using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolbarControl_NS;
using UnityEngine;

namespace CelestialBodyMover
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)] // startup on main menu according to https://github.com/linuxgurugamer/ToolbarControl/wiki/Registration
    public class RegisterToolbar : MonoBehaviour
    {
        void Start()
        {
            ToolbarControl.RegisterMod("CelestialBodyMover", "CelestialBodyMover");
        }
    }

    internal static class Util
    {
        internal static void Log(string message, string prefix = "[CelestialBodyMover]")
        {
            UnityEngine.Debug.Log($"{prefix}: {message}"); // KSPLog.print does the same thing
        }

        internal static void LogWarning(string message, string prefix = "[CelestialBodyMover]")
        {
            UnityEngine.Debug.LogWarning($"{prefix}: {message}");
        }

        internal static void LogError(string message, string prefix = "[CelestialBodyMover]")
        {
            UnityEngine.Debug.LogError($"{prefix}: {message}");
        }
    }

    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class CelestialBodyMover : MonoBehaviour
    {
        ToolbarControl toolbarControl = null;

        GUISkin skin;

        bool isWindowOpen = true;
        bool isKSPGUIActive = true; // for some reason, this initially only turns to true when you turn off and on the KSP GUI

        internal static bool isActive = false;

        Vector3d currentPos = Vector3d.zero;

        Rect mainRect = new Rect(100, 100, -1, -1);

        private void InitToolbar()
        {
            if (toolbarControl == null)
            {
                toolbarControl = gameObject.AddComponent<ToolbarControl>();
                toolbarControl.AddToAllToolbars(ToggleWindow, ToggleWindow,
                    ApplicationLauncher.AppScenes.ALWAYS,
                    "CelestialBodyMover",
                    "CelestialBodyMover_Button",
                    "CelestialBodyMover/PluginData/ToolbarIcons/button-64",
                    "CelestialBodyMover/PluginData/ToolbarIcons/button-24",
                    "CelestialBodyMover"
                );
            }
        }

        void Awake()
        {
            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);

            GameEvents.onShowUI.Add(KSPShowGUI);
            GameEvents.onHideUI.Add(KSPHideGUI);
        }

        void Start()
        {
            InitToolbar();
        }

        void OnDestroy()
        {
            Destroy(toolbarControl);
            toolbarControl = null;

            GameEvents.onShowUI.Remove(KSPShowGUI);
            GameEvents.onHideUI.Remove(KSPHideGUI);
        }

        private void ToggleWindow() => isWindowOpen = !isWindowOpen;

        private void KSPShowGUI() => isKSPGUIActive = true;

        private void KSPHideGUI() => isKSPGUIActive = false;

        void OnGUI()
        {
            if (isWindowOpen && isKSPGUIActive)
            {
                GUI.skin = skin;
                int id0 = GetHashCode();

                mainRect = ClickThruBlocker.GUILayoutWindow(id0, mainRect, MakeMainWindow, "CelestialBodyMover", GUILayout.Width(200));
                ClampToScreen(ref mainRect);
            }
        }

        void Update()
        {
            GetThrust();
            MakeVesselStationary();

            CheatOptions.InfinitePropellant = true;
            CheatOptions.InfiniteElectricity = true;
            CheatOptions.IgnoreMaxTemperature = true;
            CheatOptions.NoCrashDamage = true;
            CheatOptions.UnbreakableJoints = true;
            CheatOptions.IgnoreEVAConstructionMassLimit = true;
            CheatOptions.IgnoreKerbalInventoryLimits = true;
        }

        private void ClampToScreen(ref Rect rect)
        {
            float left = Mathf.Clamp(rect.x, 0, Screen.width - rect.width);
            float top = Mathf.Clamp(rect.y, 0, Screen.height - rect.height);
            rect = new Rect(left, top, rect.width, rect.height);
        }

        private void MakeMainWindow(int id)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                string buttonText = isActive ? "Unfreeze Craft" : "Freeze Craft";
                if (GUILayout.Button(buttonText))
                {
                    isActive = !isActive;
                    if (FlightGlobals.ActiveVessel != null)
                    {
                        Vessel vessel = FlightGlobals.ActiveVessel;

                        currentPos = vessel.vesselTransform.position;

                        Util.Log($"currentPos: {currentPos}");
                    }

                    Util.Log(isActive ? "Craft Frozen" : "Craft Unfrozen");
                }
            }
            else
            {
                GUILayout.Label("Not flight scene!");
            }
            GUI.DragWindow();
        }

        private void MakeVesselStationary()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;

            if (vessel == null)
                return;

            if (isActive)
            {
                vessel.SetWorldVelocity(Vector3d.zero);
                vessel.SetPosition(currentPos, true);
            }
        }

        private void GetThrust()
        {
            if (!isActive || HighLogic.LoadedScene != GameScenes.FLIGHT) return;

            Vessel vessel = FlightGlobals.ActiveVessel;

            if (vessel.heightFromTerrain >= 10d && vessel.situation != Vessel.Situations.LANDED)
            {
                Util.Log($"Altitude too high (vessel.heightFromTerrain: {vessel.heightFromTerrain}, vessel.situation: {vessel.situation})");
                return;
            }

            Vector3d thrustVector = Vector3d.zero;
            //Vector3d thrustVector2 = Vector3d.zero;
            //Vector3d thrustVector3 = Vector3d.zero;

            Vector3d vesselForward = vessel.GetTransform().up;

            //vessel.GetHeightFromTerrain();

            if (TimeWarp.CurrentRate > 1d)
            {
                //BackgroundThrustVessel bVessel = vessel.FindVesselModuleImplementing<BackgroundThrustVessel>();
                //thrustVector = bVessel.Thrust;
                thrustVector = Vector3d.zero;
            }
            else
            {
                // thrust code taken from https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/VesselState.cs

                List<Part> parts = vessel.parts.Where(p => p.State == PartStates.ACTIVE).ToList();

                for (int i1 = 0; i1 < parts.Count; i1++)
                {
                    Part part = parts[i1];

                    //thrustVector3 += part.force;

                    for (int i2 = 0; i2 < part.Modules.Count; i2++)
                    {
                        PartModule pm = part.Modules[i2];
                        if (!pm.isEnabled)
                        {
                            continue;
                        }

                        if (pm is ModuleEngines e)
                        {
                            for (int i3 = 0; i3 < e.thrustTransforms.Count; i3++)
                            {
                                Transform transform = e.thrustTransforms[i3];
                                // The rotation makes a +z vector point in the direction that molecules are ejected
                                // from the engine.  The resulting thrust force is in the opposite direction.
                                Vector3d thrustDirectionVector = -transform.forward;

                                double cosineLosses = Vector3d.Dot(thrustDirectionVector, vesselForward);
                                float thrustTransformMultiplier = e.thrustTransformMultipliers[i3];
                                double tCurrentThrust = e.finalThrust * thrustTransformMultiplier;
                                Vector3d transformThrust = tCurrentThrust * cosineLosses * thrustDirectionVector;
                                //Vector3d transformThrust2 = tCurrentThrust * cosineLosses * vesselForward;

                                thrustVector += transformThrust;
                                //thrustVector2 += transformThrust2;

                                //Util.Log($"transformThrust: {transformThrust}, transformThrust2: {transformThrust2}, thrustVector: {thrustVector}, thrustVector2: {thrustVector2}, thrustDirectionVector: {thrustDirectionVector}, cosineLosses: {cosineLosses}, tCurrentThrust: {tCurrentThrust}");
                            }
                        }
                    }

                    //for (int i2 = 0; i2 < part.forces.Count; i2++)
                    //{
                    //    thrustVector3 += part.forces[i2].force;
                    //}
                }
            }

            if (thrustVector == Vector3d.zero)
            {
                //Util.Log($"No thrust (thrustVector: {thrustVector})");
                return;
            }

            double currentUT = Planetarium.GetUniversalTime();

            const double epsilon = 1e-3d;

            thrustVector *= 1000d; // convert from kN to N

            //double thrustMagnitude = thrustVector.magnitude;
            //double thrustMagnitude2 = thrustVector2.magnitude;
            //double thrustMagnitude3 = thrustVector3.magnitude;

            Vector3d thrustNormal = thrustVector.normalized;

            CelestialBody body = vessel.mainBody;

            Orbit bodyOrbit = body.orbit;
            Vector3d vesselPos = vessel.GetWorldPos3D();
            Vector3d planetPos = body.position;
            Vector3d toPlanet = (planetPos - vesselPos).normalized;
            double alignmentToCenter = Vector3d.Dot(thrustNormal, toPlanet);

            if (alignmentToCenter <= 0d)
            {
                //Util.Log($"Thrust not pointing towards planet (alignmentToCenter: {alignmentToCenter})");
                return;
            }

            double vesselMass = vessel.totalMass * 1000d; // convert from tons to kg

            double effectiveThrust = thrustVector.magnitude * alignmentToCenter;
            Vector3d forceOnPlanet = thrustNormal * effectiveThrust;
            double totalMass = body.Mass + vesselMass;
            Vector3d accel = forceOnPlanet / totalMass;
            Vector3d deltaV = accel * Time.fixedDeltaTime;
            Vector3d position = bodyOrbit.getRelativePositionAtUT(currentUT);
            Vector3d velocity = bodyOrbit.getOrbitalVelocityAtUT(currentUT);

            Vector3d newVelocity = velocity + deltaV;

            bodyOrbit.UpdateFromStateVectors(position, newVelocity, bodyOrbit.referenceBody, currentUT);

            if (alignmentToCenter > 1d - epsilon)
            {
                Util.Log($"Thrust aligned with planet center, no torque (alignmentToCenter: {alignmentToCenter})");

                Util.Log($"thrustVector: {thrustVector}, alignmentToCenter: {alignmentToCenter}, velocity: {velocity}, newVelocity: {newVelocity}, forceOnPlanet: {forceOnPlanet}, accel: {accel}");
                Util.Log($"velocity: {velocity.magnitude}, newVelocity: {newVelocity.magnitude}, forceOnPlanet: {forceOnPlanet.magnitude}, accel: {accel.magnitude}");
            }
            else
            {
                Vector3d axis = body.angularVelocity.normalized;
                double alignmentToAxis = Vector3d.Dot(thrustNormal, axis);

                if (1d - Math.Abs(alignmentToAxis) < epsilon)
                {
                    Util.Log($"Thrust aligned with planet axis, no torque (alignmentToAxis: {alignmentToAxis})");

                    Util.Log($"thrustVector: {thrustVector}, alignmentToCenter: {alignmentToCenter}, velocity: {velocity}, newVelocity: {newVelocity}, forceOnPlanet: {forceOnPlanet}, accel: {accel}");
                    Util.Log($"velocity: {velocity.magnitude}, newVelocity: {newVelocity.magnitude}, forceOnPlanet: {forceOnPlanet.magnitude}, accel: {accel.magnitude}");
                }
                else
                {
                    Vector3d radius = vesselPos - planetPos;
                    Vector3d torque = Vector3d.Cross(radius, thrustVector);
                    double torqueAlongAxis = Vector3d.Dot(torque, axis);

                    double I = (0.4 * body.Mass * body.Radius * body.Radius) + vesselMass * radius.magnitude * radius.magnitude;

                    double angularAccel = torqueAlongAxis / I;

                    Vector3d newAngularVelocity = body.angularVelocity + axis * (angularAccel * Time.fixedDeltaTime);

                    body.angularVelocity = newAngularVelocity;

                    double newPeriod = (2d * Math.PI) / newAngularVelocity.magnitude;
                    double origPeriod = (2d * Math.PI) / body.angularVelocity.magnitude;

                    Util.Log($"alignmentToCenter: {alignmentToCenter}, alignmentToAxis: {alignmentToAxis}, torque: {torque}, velocity: {velocity}, newVelocity: {newVelocity}, forceOnPlanet: {forceOnPlanet}, accel: {accel}, body.angularVelocity: {body.angularVelocity}, newAngularVelocity: {newAngularVelocity}, origPeriod: {origPeriod}, newPeriod: {newPeriod}, angularAccel: {angularAccel}");
                    Util.Log($"torque: {torque.magnitude}, velocity: {velocity.magnitude}, newVelocity: {newVelocity.magnitude}, forceOnPlanet: {forceOnPlanet.magnitude}, accel: {accel.magnitude}, body.angularVelocity: {body.angularVelocity.magnitude}, newAngularVelocity: {newAngularVelocity.magnitude}");
                    Util.Log($"I: {I}, torqueAlongAxis: {torqueAlongAxis}, forceOnPlanet: {forceOnPlanet.magnitude}, torque: {torque.magnitude}, axis: {axis}, body.Radius: {body.Radius}, radius.magnitude: {radius.magnitude}");
                }
            }

            Util.Log($"semiMajorAxis: {bodyOrbit.semiMajorAxis}, ApR: {bodyOrbit.ApR}, PeR: {bodyOrbit.PeR}, eccentricity: {bodyOrbit.eccentricity}, inclination: {bodyOrbit.inclination}, LAN: {bodyOrbit.LAN}, AOP: {bodyOrbit.argumentOfPeriapsis}, orbitalEnergy: {bodyOrbit.orbitalEnergy}, orbitalSpeed: {bodyOrbit.orbitalSpeed}");

            // need to make sure the vessel is touching the ground (not just splashed)

            //Util.Log($"TimeWarp.CurrentRate: {TimeWarp.CurrentRate}, thrustVector: {thrustVector}, thrustVector2: {thrustVector2}, thrustVector3: {thrustVector3}, thrustMagnitude: {thrustMagnitude}, thrustMagnitude2: {thrustMagnitude2}, thrustMagnitude3: {thrustMagnitude3}, bodyPos: {body.position}, bodyAngVel: {body.angularVelocity}, body.zUpAngularVelocity: {body.zUpAngularVelocity}, rotationPeriod: {body.rotationPeriod}, solarDayLength: {body.solarDayLength}, rotation: {body.rotation}, rotationAngle: {body.rotationAngle}, referenceBody: {bodyOrbit.referenceBody.bodyName}, GetRelativeVel: {bodyOrbit.GetRelativeVel()}, orbitalSpeed: {bodyOrbit.orbitalSpeed}, GetFrameVel().magnitude: {bodyOrbit.GetFrameVel().magnitude}, transformRight: {body.transformRight}, transformUp: {body.transformUp}");
            //Util.Log($"thrustVector.normalized: {thrustVector.normalized} vesselForward: {vesselForward} dot: {Vector3d.Dot(thrustVector.normalized, vesselForward)}, vesselPos: {vesselPos}, planetPos: {planetPos}, toPlanet: {toPlanet}, alignmentToCenter: {alignmentToCenter}");
        }
    }

    //[HarmonyPatch(typeof(Vessel))]
    //public static class VesselPatches
    //{
    //    [HarmonyPatch(nameof(Vessel.UpdateAcceleration))]
    //    [HarmonyPrefix]
    //    internal static bool Prefix_UpdateAcceleration(Vessel __instance, double fdtRecip, bool fromUpdate)
    //    {
    //        if (!CelestialBodyMover.isActive)
    //        {
    //            return true;
    //        }

    //        //Util.Log($"Prefix_UpdateAcceleration called");

    //        bool flag = __instance.mainBody.rotates && __instance.mainBody.inverseRotation;

    //        if (__instance.loaded && !__instance.packed)
    //        {
    //            __instance.acceleration.Zero();
    //            __instance.acceleration_immediate.Zero();
    //            __instance.perturbation.Zero();
    //            __instance.perturbation_immediate.Zero();
    //            __instance.geeForce = 0d;
    //            __instance.specificAcceleration = 0d;
    //            __instance.geeForce_immediate = 0d;

    //            __instance.lastVel = __instance.obt_velocity;

    //            __instance.frameWasRotating = flag;
    //            __instance.krakensbaneAcc.Zero();
    //            __instance.lastBody = __instance.orbit.referenceBody;

    //            return false;
    //        }
    //        else
    //        {
    //            return true;
    //        }
    //    }

    //    [HarmonyPatch(nameof(Vessel.UpdatePosVel))]
    //    [HarmonyPostfix]
    //    internal static void Postfix_UpdatePosVel(Vessel __instance)
    //    {
    //        if (!CelestialBodyMover.isActive)
    //        {
    //            return;
    //        }

    //        //Util.Log($"Postfix_UpdatePosVel called");

    //        __instance.obt_velocity.Zero();
    //        __instance.obt_speed = 0d;
    //        __instance.srf_velocity.Zero();
    //        __instance.verticalSpeed = 0d;
    //        __instance.srfSpeed = 0.0;
    //        __instance.horizontalSrfSpeed = 0.0;
    //        __instance.srf_vel_direction.Zero();
    //    }
    //}

    //[HarmonyPatch(typeof(OrbitDriver))]
    //public static class OrbitDriverPatches
    //{
    //    [HarmonyPatch(nameof(OrbitDriver.UpdateOrbit))]
    //    [HarmonyPrefix]
    //    internal static bool Prefix_UpdateOrbit(OrbitDriver __instance)
    //    {
    //        if (!CelestialBodyMover.isActive || __instance != FlightGlobals.ActiveVessel?.orbitDriver)
    //        {
    //            return true;
    //        }

    //        //Util.Log($"Prefix_UpdateOrbit called");

    //        __instance.lastMode = OrbitDriver.UpdateMode.IDLE;
    //        return true;
    //    }
    //}

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyPatcher : MonoBehaviour
    {
        internal void Start()
        {
            var harmony = new Harmony("CelestialBodyMover.HarmonyPatcher");
            harmony.PatchAll();
        }
    }
}
