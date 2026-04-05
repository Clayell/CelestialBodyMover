//using BackgroundThrust;
using ClickThroughFix;
//using HarmonyLib;
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

    [KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public class CelestialBodyMover : ScenarioModule
    //[KSPAddon(KSPAddon.Startup.AllGameScenes, true)]
    //public class CelestialBodyMover : MonoBehaviour
    {
        const double tau = 2d * Math.PI; // Math.Tau is in .NET 5

        ToolbarControl toolbarControl = null;

        GUISkin skin;

        [KSPField(isPersistant = true)]
        bool isWindowOpen = false;
        bool isKSPGUIActive = true; // for some reason, this initially only turns to true when you turn off and on the KSP GUI
        bool isLoading = false;
        bool isBadUI = false;

        //bool? firstLoad = null;
        [KSPField(isPersistant = true)]
        bool firstLoad = true;
        [KSPField(isPersistant = true)]
        bool debugMode = false;

        [KSPField(isPersistant = true)]
        bool isActive = false;
        bool isFrozen = false; // this should be false by default, even after loading

        Vector3d forceVector = Vector3d.zero;
        Vector3d currentPos = Vector3d.zero;

        Rect mainRect = new Rect(0, 0, -1, -1);
        [KSPField(isPersistant = true)]
        Vector2 mainRectPos = new Vector2(100, 100);

        //ScenarioModule scenarioRoot = new ScenarioModule();
        //ConfigNode root = new ConfigNode();

        static string SettingsFolder;

        private void InitToolbar()
        {
            if (toolbarControl == null)
            {
                toolbarControl = gameObject.AddComponent<ToolbarControl>();
                toolbarControl.AddToAllToolbars(ToggleWindow, ToggleWindow,
                    ApplicationLauncher.AppScenes.ALWAYS,
                    "CelestialBodyMover",
                    "CelestialBodyMover_Button",
                    "CelestialBodyMover/PluginData/ToolbarIcons/CelestialBodyMover-64",
                    "CelestialBodyMover/PluginData/ToolbarIcons/CelestialBodyMover-24",
                    "CelestialBodyMover"
                );
            }
        }

        //void Awake()
        public override void OnAwake() // scenariomodule stuff needs to run before we call our Awake stuff 
        {
            Util.Log("Awake called");

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
            Util.Log("Start called");

            InitToolbar();

            SettingsFolder = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/CelestialBodyMover/PluginData/");
        }

        void OnDestroy()
        {
            Util.Log("OnDestroy called");

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
        }

        private void ToggleWindow() => isWindowOpen = !isWindowOpen;

        private void KSPShowGUI() => isKSPGUIActive = true;

        private void KSPHideGUI() => isKSPGUIActive = false;

        private void HideBadUI() => isBadUI = true;

        private void ShowBadUI() => isBadUI = false;

        private void OnSceneLoaded(GameScenes s)
        {
            if (s != GameScenes.MAINMENU)
            {
                isLoading = false;
            }
        }

        private void OnSceneChange(GameScenes s)
        {
            Util.Log($"Scene changing to {s}");

            isLoading = true;
        }

        public override void OnSave(ConfigNode root)
        {
            SaveOrbitDetails(ref root, "savedOrbits");
        }

        public override void OnLoad(ConfigNode root)
        {
            LoadOrbitDetails(root, "savedOrbits");

            mainRect = new Rect(mainRectPos.x, mainRectPos.y, mainRect.width, mainRect.height);
        }

        private void SaveOrbitDetails(string saveFile)
        {
            ConfigNode root = new ConfigNode();
            string savePath = Path.Combine(SettingsFolder, saveFile);
            File.WriteAllText(savePath, "");

            SaveOrbitDetails(ref root, Path.GetFileNameWithoutExtension(savePath));

            root.Save(savePath);
        }

        private void SaveOrbitDetails(ref ConfigNode root, string saveNode)
        {
            Util.Log($"Saving {saveNode}...");

            ConfigNode orbits = root.AddNode(saveNode);
            if (orbits == null)
            {
                Util.LogError($"Failed to create ORBITS node in {saveNode}");
                return;
            }

            if (FlightGlobals.Bodies == null)
            {
                Util.LogError($"FlightGlobals.Bodies is null");
                return;
            }

            orbits.AddValue("numBodies", FlightGlobals.Bodies.Count);
            orbits.AddValue("UT", Planetarium.GetUniversalTime());

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body.isStar || body.name == "Sun")
                {
                    Util.Log($"Body is a star (bodyName: {body.name})");
                    continue;
                }
                Orbit orbit = body.orbit;
                if (orbit == null)
                {
                    Util.LogError($"Failed to get orbit for body {body?.name}");
                    continue;
                }

                ConfigNode bodyNode = orbits?.AddNode(body?.name);
                if (bodyNode == null)
                {
                    Util.LogError($"Failed to get bodyNode for body {body?.name}");
                }

                string log = "";
                void AddValue(string name, object value)
                {
                    bodyNode.AddValue(name, value);
                    log += $"{name}: {value}, ";
                }

                FixParabolic(ref orbit);

                AddValue("inclination", orbit.inclination);
                AddValue("eccentricity", orbit.eccentricity);
                AddValue("semiMajorAxis", orbit.semiMajorAxis);
                AddValue("LAN", orbit.LAN);
                AddValue("argumentOfPeriapsis", orbit.argumentOfPeriapsis);
                AddValue("meanAnomalyAtEpoch", orbit.meanAnomalyAtEpoch);
                AddValue("epoch", orbit.epoch);
                AddValue("referenceBody", orbit.referenceBody.name);

                AddValue("rotationPeriod", body.rotationPeriod);
                AddValue("initialRotation", body.initialRotation);

                Util.Log($"Saved orbit for body {body.name}: " + log);
            }
            Util.Log($"Saved orbits for {saveNode}");

            return;
        }

        private void LoadOrbitDetails(string saveFile)
        {
            string savePath = Path.Combine(SettingsFolder, saveFile);
            Util.Log($"Loading orbits from {savePath}...");

            if (!File.Exists(savePath))
            {
                Util.LogError($"File {savePath} does not exist");
                return;
            }

            ConfigNode root = ConfigNode.Load(savePath);
            LoadOrbitDetails(root, Path.GetFileNameWithoutExtension(savePath));
        }

        private void LoadOrbitDetails(ConfigNode root, string saveNode)
        {
            Util.Log($"Loading orbits from {saveNode}...");

            ConfigNode orbits = root.GetNode(saveNode);
            if (orbits == null)
            {
                Util.LogError($"Failed to find ORBITS node in {root}");
                return;
            }
            if (!int.TryParse(orbits.GetValue("numBodies"), out int numBodies))
            {
                Util.LogError($"Failed to parse numBodies from {root}");
                return;
            }
            if (!double.TryParse(orbits.GetValue("UT"), out double UT))
            {
                Util.LogError($"Failed to parse UT from {root}");
                return;
            }

            if (FlightGlobals.Bodies == null)
            {
                Util.LogError($"FlightGlobals.Bodies is null");
                return;
            }

            if (numBodies != FlightGlobals.Bodies.Count) { Util.LogError($"numBodies ({numBodies}) does not match bodies count ({FlightGlobals.Bodies.Count})"); return; }

            for (int i = 0; i < numBodies; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body.isStar)
                {
                    Util.Log($"Body is a star (bodyName: {body.name}) in {saveNode}, skipping");
                    continue;
                }
                ConfigNode bodyNode = orbits.GetNode(body.name);
                if (bodyNode == null)
                {
                    Util.LogError($"Failed to find node for body {body.name} in {saveNode}");
                    continue;
                }

                string log = "";
                bool DoubleParse(string value, out double result)
                {                  
                    if (!double.TryParse(bodyNode.GetValue(value), out result))
                    {
                        Util.LogError($"Failed to parse {value} for body {body.name} in {saveNode}");
                        return false;
                    }
                    log += $"{value}: {result}, ";
                    return true;
                }

                if (!DoubleParse("inclination", out double inclination)) continue;
                if (!DoubleParse("eccentricity", out double eccentricity)) continue;
                if (!DoubleParse("semiMajorAxis", out double semiMajorAxis)) continue;
                if (!DoubleParse("LAN", out double LAN)) continue;
                if (!DoubleParse("argumentOfPeriapsis", out double argumentOfPeriapsis)) continue;
                if (!DoubleParse("meanAnomalyAtEpoch", out double meanAnomalyAtEpoch)) continue;
                if (!DoubleParse("epoch", out double epoch)) continue;
                CelestialBody referenceBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == bodyNode.GetValue("referenceBody"));
                if (referenceBody == null)
                {
                    Util.LogError($"Failed to parse {referenceBody} for body {body.name} in {saveNode}");
                    continue;
                }

                if (!DoubleParse("rotationPeriod", out double rotationPeriod)) continue;
                if (!DoubleParse("initialRotation", out double initialRotation)) continue;

                Orbit bodyOrbit = body.orbit;

                bodyOrbit.SetOrbit(inclination, eccentricity, semiMajorAxis, LAN, argumentOfPeriapsis, meanAnomalyAtEpoch, epoch, referenceBody); // calls Init()

                double meanMotion = Math.Sqrt(body.gravParameter / Math.Pow(Math.Abs(semiMajorAxis), 3)); // abs(SMA) to allow for hyperbolic orbits. guaranteed to be defined as we removed parabolic orbits when saving
                bodyOrbit.meanAnomaly = (bodyOrbit.meanAnomalyAtEpoch + meanMotion * (UT - epoch)) % tau; // Init() just sets meanAnomaly to meanAnomalyAtEpoch, so we need to fix it

                body.rotationPeriod = rotationPeriod;
                body.initialRotation = initialRotation;
                Util.Log($"Loading orbit for body {body.name}: " + log);

                body.CBUpdate(); // make sure this gets called before we do anything else
            }

            Util.Log($"Loaded orbits from {saveNode}");
        }

        void FixParabolic(ref Orbit orbit)
        {
            if (orbit.eccentricity == 1d)
            {
                orbit.eccentricity = 1d - 1e-9; // decrease parabolic orbits to be highly eccentric
                Util.LogWarning($"Orbit around {orbit.referenceBody} was parabolic, changing eccentricity to {orbit.eccentricity} to avoid issues with orbit calculations");
            }
            orbit.semiMajorAxis = -orbit.referenceBody.gravParameter / (2d * orbit.orbitalEnergy); // we need to use this definition because only orbitalEnergy is actually recalculated in UpdateFromFixedVectors
        }

        void OnGUI()
        {
            if (isWindowOpen && isKSPGUIActive && !isLoading && !isBadUI)
            {
                GUI.skin = skin;
                int id0 = GetHashCode();

                mainRect = ClickThruBlocker.GUILayoutWindow(id0, mainRect, MakeMainWindow, "Celestial Body Mover", GUILayout.Width(200));
                ClampToScreen(ref mainRect);

                mainRectPos.x = mainRect.xMin;
                mainRectPos.y = mainRect.yMin;
            }
        }

        void Update()
        {
            //if (firstLoad.HasValue && firstLoad.Value) // only want this to run on the 2nd frame, after the epochs are loaded
            //{
            //    SaveOrbitDetails(originalSavePath);

            //    firstLoad = false;
            //}
            //if (!firstLoad.HasValue)
            //{
            //    firstLoad = true;
            //}
            if (firstLoad)
            {
                SaveOrbitDetails("originalOrbits.cfg");

                firstLoad = false;
            }

            if (debugMode)
            {
                CheatOptions.InfinitePropellant = true;
                CheatOptions.InfiniteElectricity = true;
                CheatOptions.IgnoreMaxTemperature = true;
                CheatOptions.NoCrashDamage = true;
                CheatOptions.UnbreakableJoints = true;
                CheatOptions.IgnoreEVAConstructionMassLimit = true;
                CheatOptions.IgnoreKerbalInventoryLimits = true;
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (isActive && !FlightDriver.Pause && HighLogic.LoadedScene == GameScenes.FLIGHT && vessel != null && !vessel.HoldPhysics && !vessel.mainBody.isStar)
            {
                if (isFrozen)
                {
                    MakeVesselStationary();
                    forceVector = GetVesselThrust();
                }
                else
                {
                    forceVector = GetGravitationalForce();
                }

                if (!forceVector.IsZero()) MovePlanet(forceVector, isFrozen);
            }
        }

        private void ClampToScreen(ref Rect rect)
        {
            float left = Mathf.Clamp(rect.x, 0, Screen.width - rect.width);
            float top = Mathf.Clamp(rect.y, 0, Screen.height - rect.height);
            rect = new Rect(left, top, rect.width, rect.height);
        }

        private void MakeMainWindow(int id)
        {
            string buttonText = isActive ? "Deactivate CBM" : "Activate CBM";
            if (GUILayout.Button(buttonText))
            {
                isActive = !isActive;

                Util.Log(isActive ? "CBM Activated" : "CBM Deactivated");
            }

            if (isActive && HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                GUILayout.Space(10);
                string frozenButton = isFrozen ? "Unfreeze Craft" : "Freeze Craft";
                if (GUILayout.Button(frozenButton))
                {
                    isFrozen = !isFrozen;
                    if (FlightGlobals.ActiveVessel != null)
                    {
                        Vessel vessel = FlightGlobals.ActiveVessel;

                        currentPos = vessel.vesselTransform.position;

                        Util.Log($"currentPos: {currentPos}");
                    }

                    Util.Log(isFrozen ? "Craft Frozen" : "Craft Unfrozen");
                }
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Reset All Orbits"))
            {
                LoadOrbitDetails(Path.Combine(SettingsFolder, "originalOrbits.cfg"));
            }

            GUILayout.Space(10);

            if (debugMode)
            {
                if (GUILayout.Button("TESTORBIT"))
                {
                    double currentUT = Planetarium.GetUniversalTime();

                    CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                    Orbit orbit = testBody.orbit;
                    orbit.SetOrbit(orbit.inclination, .5, orbit.semiMajorAxis, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomalyAtEpoch, orbit.epoch, orbit.referenceBody);
                    double meanMotion = Math.Sqrt(testBody.gravParameter / Math.Pow(Math.Abs(orbit.semiMajorAxis), 3)); // abs(SMA) to allow for hyperbolic orbits.
                    orbit.meanAnomalyAtEpoch = (orbit.meanAnomaly - meanMotion * (currentUT - orbit.epoch)) % tau; // set new initial meanAnomaly to match period
                }

                if (GUILayout.Button("TESTROTATION"))
                {
                    CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                    testBody.rotationPeriod = 12345d;
                    testBody.initialRotation = (testBody.rotationAngle - 360d * (1d / testBody.rotationPeriod) * Planetarium.GetUniversalTime()) % 360d;
                }
            }

            GUI.DragWindow();
        }

        private void MakeVesselStationary()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;

            vessel.SetWorldVelocity(Vector3d.zero);
            vessel.SetPosition(currentPos, true);
        }

        private Vector3d GetVesselThrust()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;

            if (vessel.heightFromTerrain >= 10d && !vessel.LandedOrSplashed)
            {
                Util.Log($"Altitude too high (vessel.heightFromTerrain: {vessel.heightFromTerrain}, vessel.situation: {vessel.situation})");
                return Vector3d.zero;
            }

            CelestialBody body = vessel.mainBody;

            if (body.isStar)
            {
                Util.Log($"Body is a star (bodyName: {body.name})");
                return Vector3d.zero;
            }

            Vector3d thrustVector = Vector3d.zero;
            //Vector3d thrustVector2 = Vector3d.zero;
            //Vector3d thrustVector3 = Vector3d.zero;

            Vector3d vesselForward = vessel.GetTransform().up;

            //vessel.GetHeightFromTerrain();

            if (TimeWarp.CurrentRate != 1f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH)
            {
                //BackgroundThrustVessel bVessel = vessel.FindVesselModuleImplementing<BackgroundThrustVessel>();
                //thrustVector = bVessel.Thrust;
                thrustVector.Zero();
            }
            else
            {
                // vessel thrust code adapted from https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/VesselState.cs

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

            if (thrustVector.IsZero())
            {
                //Util.Log($"No thrust (thrustVector: {thrustVector})");
                return thrustVector;
            }

            thrustVector *= 1000d; // convert from kN to N

            return thrustVector;
        }

        private Vector3d GetGravitationalForce()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel.LandedOrSplashed) return Vector3d.zero;
            double vesselMass = vessel.totalMass * 1000d; // convert from tons to kg
            CelestialBody body = vessel.mainBody;

            Vector3d radiusVec = body.position - vessel.GetWorldPos3D();
            Vector3d toBody = radiusVec.normalized;
            double radius = radiusVec.magnitude;

            double forceMagnitude = body.gravParameter * vesselMass / (radius * radius);

            Vector3d forceVector = toBody * forceMagnitude;

            Util.Log($"vesselMass: {vesselMass}, radius: {radius}, body.gravParameter: {body.gravParameter}, forceMagnitude: {forceMagnitude}, forceVector: {forceVector}, toBody: {toBody}");

            return forceVector;
        }

        private void MovePlanet(Vector3d forceVector, bool isFrozen)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            double vesselMass = vessel.totalMass * 1000d; // convert from tons to kg
            CelestialBody body = vessel.mainBody;
            double currentUT = Planetarium.GetUniversalTime();
            const double epsilon = 1e-3d;

            Vector3d thrustNormal = forceVector.normalized;

            Orbit orbit = body.orbit;
            Vector3d radiusVec = body.position - vessel.GetWorldPos3D();
            double alignmentToCenter = isFrozen ? Vector3d.Dot(thrustNormal, radiusVec.normalized) : 1d; // if using gravitational force, its always aligned

            if (alignmentToCenter <= 0d)
            {
                //Util.Log($"Thrust not pointing towards planet (alignmentToCenter: {alignmentToCenter})");
                return;
            }

            // TODO: add a way to change reference body if it gets in the hill sphere, would need to use world vectors instead of local

            // TODO: need to make sure the vessel is close to the actual ground, even if underwater

            double effectiveThrust = forceVector.magnitude * alignmentToCenter;
            Vector3d forceOnPlanet = thrustNormal * effectiveThrust;
            double totalMass = body.Mass + vesselMass;
            Vector3d accel = forceOnPlanet / totalMass;
            Vector3d deltaV = accel * Time.fixedDeltaTime;
            Vector3d position = orbit.getRelativePositionAtUT(currentUT);
            Vector3d velocity = orbit.getOrbitalVelocityAtUT(currentUT);

            Vector3d newVelocity = velocity + deltaV;

            orbit.UpdateFromStateVectors(position, newVelocity, orbit.referenceBody, orbit.epoch);

            FixParabolic(ref orbit);

            double meanMotion = Math.Sqrt(body.gravParameter / Math.Pow(Math.Abs(orbit.semiMajorAxis), 3)); // abs(SMA) to allow for hyperbolic orbits.
            orbit.meanAnomalyAtEpoch = (orbit.meanAnomaly - meanMotion * (currentUT - orbit.epoch)) % tau; // set new initial meanAnomaly to match period

            if (alignmentToCenter > 1d - epsilon)
            {
                Util.Log($"Thrust aligned with planet center, no torque (alignmentToCenter: {alignmentToCenter})");

                Util.Log($"forceVector: {forceVector}, alignmentToCenter: {alignmentToCenter}, velocity: {velocity}, newVelocity: {newVelocity}, forceOnPlanet: {forceOnPlanet}, accel: {accel}");
                Util.Log($"velocity: {velocity.magnitude}, newVelocity: {newVelocity.magnitude}, forceOnPlanet: {forceOnPlanet.magnitude}, accel: {accel.magnitude}");
            }
            else
            {
                Vector3d axis = body.angularVelocity.normalized;
                double alignmentToAxis = Vector3d.Dot(thrustNormal, axis);

                if (1d - Math.Abs(alignmentToAxis) < epsilon)
                {
                    Util.Log($"Thrust aligned with planet axis, no torque (alignmentToAxis: {alignmentToAxis})");

                    Util.Log($"forceVector: {forceVector}, alignmentToCenter: {alignmentToCenter}, velocity: {velocity}, newVelocity: {newVelocity}, forceOnPlanet: {forceOnPlanet}, accel: {accel}");
                    Util.Log($"velocity: {velocity.magnitude}, newVelocity: {newVelocity.magnitude}, forceOnPlanet: {forceOnPlanet.magnitude}, accel: {accel.magnitude}");
                }
                else
                {
                    Vector3d torque = Vector3d.Cross(radiusVec, forceVector);
                    double torqueAlongAxis = Vector3d.Dot(torque, axis);

                    double I = (0.4 * body.Mass * body.Radius * body.Radius) + vesselMass * radiusVec.magnitude * radiusVec.magnitude;

                    double angularAccel = torqueAlongAxis / I;

                    Vector3d newAngularVelocity = body.angularVelocity + axis * (angularAccel * Time.fixedDeltaTime);

                    double newPeriod = tau / newAngularVelocity.magnitude;
                    double origPeriod = tau / body.angularVelocity.magnitude;
                    body.rotationPeriod = newPeriod; // angular velocity is set by rotation period in CBUpdate
                    body.initialRotation = (body.rotationAngle - 360d * (1d / newPeriod) * currentUT) % 360d; // work backwards from rotationAngle = (initialRotation + 360.0 * rotPeriodRecip * Planetarium.GetUniversalTime()) % 360.0;

                    Util.Log($"alignmentToCenter: {alignmentToCenter}, alignmentToAxis: {alignmentToAxis}, torque: {torque}, velocity: {velocity}, newVelocity: {newVelocity}, forceOnPlanet: {forceOnPlanet}, accel: {accel}, body.angularVelocity: {body.angularVelocity}, newAngularVelocity: {newAngularVelocity}, origPeriod: {origPeriod}, newPeriod: {newPeriod}, angularAccel: {angularAccel}");
                    Util.Log($"torque: {torque.magnitude}, velocity: {velocity.magnitude}, newVelocity: {newVelocity.magnitude}, forceOnPlanet: {forceOnPlanet.magnitude}, accel: {accel.magnitude}, body.angularVelocity: {body.angularVelocity.magnitude}, newAngularVelocity: {newAngularVelocity.magnitude}");
                    Util.Log($"I: {I}, torqueAlongAxis: {torqueAlongAxis}, forceOnPlanet: {forceOnPlanet.magnitude}, torque: {torque.magnitude}, axis: {axis}, body.Radius: {body.Radius}, radius.magnitude: {radiusVec.magnitude}");
                }

                body.CBUpdate(); // make sure this gets called before we do anything else
            }

            Util.Log($"semiMajorAxis: {orbit.semiMajorAxis}, ApR: {orbit.ApR}, PeR: {orbit.PeR}, eccentricity: {orbit.eccentricity}, inclination: {orbit.inclination}, LAN: {orbit.LAN}, AOP: {orbit.argumentOfPeriapsis}, orbitalEnergy: {orbit.orbitalEnergy}, orbitalSpeed: {orbit.orbitalSpeed}");
        }
    }

    //[HarmonyPatch(typeof(Vessel))]
    //public static class VesselPatches
    //{
    //    [HarmonyPatch(nameof(Vessel.UpdateAcceleration))]
    //    [HarmonyPrefix]

    //[KSPAddon(KSPAddon.Startup.Instantly, true)]
    //public class HarmonyPatcher : MonoBehaviour
    //{
    //    internal void Start()
    //    {
    //        var harmony = new Harmony("CelestialBodyMover.HarmonyPatcher");
    //        harmony.PatchAll();
    //    }
    //}
}
