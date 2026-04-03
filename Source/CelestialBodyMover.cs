//using BackgroundThrust;
using ClickThroughFix;
using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
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

    [KSPAddon(KSPAddon.Startup.AllGameScenes, true)]
    //[KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    //public class CelestialBodyMover : ScenarioModule
    public class CelestialBodyMover : MonoBehaviour
    {
        ToolbarControl toolbarControl = null;

        GUISkin skin;

        //[KSPField(isPersistant = true)]
        bool isWindowOpen = true;
        bool isKSPGUIActive = true; // for some reason, this initially only turns to true when you turn off and on the KSP GUI
        bool isLoading = false;
        bool isBadUI = false;

        internal static bool isActive = false;

        Vector3d currentPos = Vector3d.zero;

        Rect mainRect = new Rect(100, 100, -1, -1);

        //ScenarioModule scenarioRoot = new ScenarioModule();
        //ConfigNode root = new ConfigNode();

        static string SettingsFolder;

        private void InitToolbar()
        {
            if (toolbarControl == null)
            {
                toolbarControl = gameObject.AddComponent<ToolbarControl>();
                toolbarControl.AddToAllToolbars(ToggleWindow, ToggleWindow,
                    ApplicationLauncher.AppScenes.FLIGHT & ApplicationLauncher.AppScenes.MAPVIEW & ApplicationLauncher.AppScenes.SPACECENTER & ApplicationLauncher.AppScenes.TRACKSTATION,
                    "CelestialBodyMover",
                    "CelestialBodyMover_Button",
                    "CelestialBodyMover/PluginData/ToolbarIcons/CelestialBodyMover-64",
                    "CelestialBodyMover/PluginData/ToolbarIcons/CelestialBodyMover-24",
                    "CelestialBodyMover"
                );
            }
        }

        void Awake()
        {
            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);

            GameEvents.onShowUI.Add(KSPShowGUI);
            GameEvents.onHideUI.Add(KSPHideGUI);

            GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneLoaded);

            GameEvents.onGUIAstronautComplexSpawn.Add(HideBadUI);
            GameEvents.onGUIRnDComplexSpawn.Add(HideBadUI);
            GameEvents.onGUIAdministrationFacilitySpawn.Add(HideBadUI);
            GameEvents.onGUIAstronautComplexDespawn.Add(ShowBadUI);
            GameEvents.onGUIRnDComplexDespawn.Add(ShowBadUI);
            GameEvents.onGUIAdministrationFacilityDespawn.Add(ShowBadUI);
        }

        void Start()
        {
            DontDestroyOnLoad(this); // we only want to load the saved orbits once

            InitToolbar();

            SettingsFolder = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/CelestialBodyMover/PluginData/");

            string originalSavePath = Path.Combine(SettingsFolder, "originalOrbits.cfg");
            if (!File.Exists(originalSavePath))
            {
                SaveOrbitDetails(originalSavePath);
            }

            string savePath = Path.Combine(SettingsFolder, "savedOrbits.cfg");
            if (File.Exists(savePath))
            {
                LoadOrbitDetails(savePath);
            }

            LoadSettings();
        }

        void OnDestroy()
        {
            Destroy(toolbarControl);
            toolbarControl = null;

            GameEvents.onShowUI.Remove(KSPShowGUI);
            GameEvents.onHideUI.Remove(KSPHideGUI);

            GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneLoaded);

            GameEvents.onGUIAstronautComplexSpawn.Remove(HideBadUI);
            GameEvents.onGUIRnDComplexSpawn.Remove(HideBadUI);
            GameEvents.onGUIAdministrationFacilitySpawn.Remove(HideBadUI);
            GameEvents.onGUIAstronautComplexDespawn.Remove(ShowBadUI);
            GameEvents.onGUIRnDComplexDespawn.Remove(ShowBadUI);
            GameEvents.onGUIAdministrationFacilityDespawn.Remove(ShowBadUI);

            SaveSettings();
            SaveOrbitDetails(Path.Combine(SettingsFolder, "savedOrbits.cfg"));
        }

        private void ToggleWindow() => isWindowOpen = !isWindowOpen;

        private void KSPShowGUI() => isKSPGUIActive = true;

        private void KSPHideGUI() => isKSPGUIActive = false;

        private void HideBadUI() => isBadUI = true;

        private void ShowBadUI() => isBadUI = false;

        private void OnSceneLoaded(GameScenes s) => isLoading = false;

        private void OnSceneChange(GameScenes s) => isLoading = true;

        void OnGUI()
        {
            if (isWindowOpen && isKSPGUIActive && !isLoading && !isBadUI)
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

            GUILayout.Space(10);

            if (GUILayout.Button("Reset All Orbits"))
            {
                LoadOrbitDetails(Path.Combine(SettingsFolder, "originalOrbits.cfg"));
            }

            GUILayout.Space(10);

            if (GUILayout.Button("TEST"))
            {
                CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                Orbit orbit = testBody.orbit;
                orbit.SetOrbit(orbit.inclination, .5, orbit.semiMajorAxis, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomalyAtEpoch, orbit.epoch, testBody.orbit.referenceBody);
                SaveOrbitDetails(Path.Combine(SettingsFolder, "savedOrbits.cfg"));
            }

            GUI.DragWindow();
        }

        private void SaveOrbitDetails(string savePath)
        {
            Util.Log($"Clearing {savePath}...");
            File.WriteAllText(savePath, "");

            ConfigNode root = new ConfigNode();
            ConfigNode orbits = root.AddNode("ORBITS");
            orbits?.AddValue("numBodies", FlightGlobals.Bodies?.Count);
            orbits?.AddValue("UT", Planetarium.GetUniversalTime());
            Util.Log($"UT: {Planetarium.GetUniversalTime()}");

            for (int i = 0; i < FlightGlobals.Bodies?.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                Orbit orbit = body?.orbit;

                if (orbit == null)
                {
                    continue;
                }

                ConfigNode bodyNode = orbits?.AddNode(body?.name);
                
                bodyNode?.AddValue("pos", orbit.pos);
                bodyNode?.AddValue("vel", orbit.vel);
                Util.Log($"orbit.epoch {orbit.epoch} for {body.name}");
            }

            root?.Save(savePath);
            Util.Log($"Saved orbits to {savePath}");

            //scenarioRoot.Save(root);
        }

        private void LoadOrbitDetails(string savePath)
        {
            //root = ConfigNode.Load(savePath);
            ConfigNode root = ConfigNode.Load(savePath);
            if (root == null)
            {
                Util.LogError($"Failed to load ROOT node from {savePath}");
                return;
            }
            //scenarioRoot.Load(root);
            ConfigNode orbits = root.GetNode("ORBITS");
            if (orbits == null)
            {
                Util.LogError($"Failed to find ORBITS node in {savePath}");
                return;
            }
            if (!int.TryParse(orbits.GetValue("numBodies"), out int numBodies))
            {
                Util.LogError($"Failed to parse numBodies from {savePath}"); 
                return;
            }
            if (!double.TryParse(orbits.GetValue("UT"), out double UT))
            {
                Util.LogError($"Failed to parse UT from {savePath}");
                return;
            }

            if (numBodies != FlightGlobals.Bodies.Count) { Util.LogError($"numBodies ({numBodies}) does not match bodies count ({FlightGlobals.Bodies.Count})"); return; }

            for (int i = 0; i < numBodies; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                ConfigNode bodyNode = orbits.GetNode(body.name);
                if (bodyNode == null)
                {
                    Util.LogError($"Failed to find node for body {body.name} in {savePath}");
                    continue;
                }
                Vector3d pos = ConfigNode.ParseVector3D(bodyNode.GetValue("pos"));
                Vector3d vel = ConfigNode.ParseVector3D(bodyNode.GetValue("vel"));
                if (pos == Vector3d.zero || vel == Vector3d.zero) { Util.LogError($"Failed to parse pos ({pos}) or vel ({vel}) from {savePath} for body {body.name}"); continue; }
                body.orbit.UpdateFromStateVectors(pos, vel, body.orbit.referenceBody, UT);
            }

            Util.Log($"Loaded orbits from {savePath}");
        }

        private void SaveSettings()
        {
            string savePath = Path.Combine(SettingsFolder, "settings.cfg");
            Util.Log($"Clearing {savePath}...");
            File.WriteAllText(savePath, "");

            ConfigNode root = new ConfigNode();
            if (root == null)
            {
                Util.LogError($"Failed to create ROOT node in {savePath}");
                return;
            }
            ConfigNode settings = root.AddNode("SETTINGS");
            if (settings == null)
            {
                Util.LogError($"Failed to create SETTINGS node in {savePath}");
                return;
            }

            settings.AddValue("isWindowOpen", isWindowOpen);

            root.Save(savePath);
            Util.Log($"Saved settings to {savePath}");
        }

        private void LoadSettings()
        {
            string savePath = Path.Combine(SettingsFolder, "settings.cfg");

            ConfigNode root = ConfigNode.Load(savePath);
            if (root == null)
            {
                Util.LogError($"Failed to load ROOT node from {savePath}");
                return;
            }
            ConfigNode settings = root.GetNode("SETTINGS");
            if (settings == null)
            {
                Util.LogError($"Failed to find SETTINGS node in {savePath}");
                return;
            }

            settings.TryGetValue("isWindowOpen", ref isWindowOpen);

            Util.Log($"Loaded settings from {savePath}");
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

            CelestialBody body = vessel.mainBody;

            if (body.isStar || body.name == "Sun")
            {
                Util.Log($"Body is a star (bodyName: {body.name})");
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
