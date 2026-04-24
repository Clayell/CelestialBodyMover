//using BackgroundThrust;
using ClickThroughFix;
using KSP.Localization;
//using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ToolbarControl_NS;
using UnityEngine;
using Situations = Vessel.Situations;

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

        // Unity only has Clamp for floats
        internal static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal static bool MapViewEnabled() => MapView.MapIsEnabled && !HighLogic.LoadedSceneIsEditor && (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneHasPlanetarium || HighLogic.LoadedScene == GameScenes.TRACKSTATION);
    }

    [KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public class CelestialBodyMover : ScenarioModule
    {
        // note: KSPField will not work on static fields
        // event function order: https://docs.unity3d.com/560/Documentation/Manual/ExecutionOrder.html

        internal static CelestialBodyMover Instance { get; private set; } // set up a singleton static instance

        const double tau = 2d * Math.PI; // Math.Tau is in .NET 5
        const double radToDeg = 180d / Math.PI; // unity only has floats
        const double degToRad = Math.PI / 180d; // unity only has floats

        ToolbarControl toolbarControl = null;

        GUIStyle lineStyle;
        Texture2D settingsGear;

        [KSPField(isPersistant = true)]
        bool showMainWindow = false;
        [KSPField(isPersistant = true)]
        bool showSettingsWindow = false;
        bool isKSPGUIActive = true; // for some reason, this initially only turns to true when you turn off and on the KSP GUI
        bool isLoading = false;
        bool isBadUI = false;

        //bool? firstLoad = null;
        [KSPField(isPersistant = true)]
        bool firstLoad = true;

        [KSPField(isPersistant = true)]
        internal bool isActive = false;
        [KSPField(isPersistant = true)]
        bool isFrozen = false;

        Vector3d radiusVec => mainBody != null && FlightGlobals.ActiveVessel != null ? mainBody.position - FlightGlobals.ActiveVessel.GetWorldPos3D() : Vector3d.zero; // vector from vessel to body
        Vector3d bodyAccel;
        Vector3d bodyVelocity;
        double _alignmentToCenter;
        double alignmentToCenter
        {
            get => _alignmentToCenter;
            set
            {
                const double tolerance = 1e-3d;
                //Util.Log($"_alignmentToCenter: {_alignmentToCenter}, value: {value}, bool1: {(_alignmentToCenter <= 0d)}, bool2: {(value <= 0d)}");
                if (((_alignmentToCenter <= 0d) != (value <= 0d)) || (_alignmentToCenter <= 1d - tolerance) != (value <= 1d - tolerance))
                {
                    //Util.Log($"alignmentToCenter changed from {_alignmentToCenter} to {value}, resetting window");
                    needWindowChange = true;
                }
                _alignmentToCenter = value;
            }
        }
        double alignmentToAxis;
        double torqueAlongAxis;
        double bodyAngularAccel;

        bool impactDetected;
        Vector3d vesselVelocity;
        double vesselVerticalSpeed;
        Guid vesselID;
        Coroutine impactCoroutine;
        Vector2 popupAnchor = new Vector2(0.5f, 0.5f);
        [KSPField(isPersistant = true)]
        float minImpactSpeed = 100f;

        CelestialBody _mainBody;
        CelestialBody mainBody
        {
            get => _mainBody;
            set
            {
                if (_mainBody != value)
                {
                    //Util.Log($"mainBody changed from {_mainBody?.displayName?.LocalizeRemoveGender()} to {value?.displayName?.LocalizeRemoveGender()}");
                    needWindowChange = true;
                    HideAllRenderers();
                }
                _mainBody = value;
            }
        }

        Vector3d currentPos;

        Rect mainRect = new Rect(0, 0, -1, -1);
        Rect settingsRect = new Rect(0, 0, -1, -1);
        [KSPField(isPersistant = true)]
        Vector2 mainRectPos = new Vector2(100, 100);
        [KSPField(isPersistant = true)]
        Vector2 settingsRectPos = new Vector2(200, 200);
        bool needWindowChange = false;

        [KSPField(isPersistant = true)]
        bool showVesselInfo = true;
        [KSPField(isPersistant = true)]
        bool showBodyInfo = true;
        [KSPField(isPersistant = true)]
        bool showBodyOrbitInfo = true;

        [KSPField(isPersistant = true)]
        float maxSurfaceHeight = 20f;
        [KSPField(isPersistant = true)]
        internal float lineLengthExponent = 5f;
        [KSPField(isPersistant = true)]
        bool killThrottleOnUnfreeze = true;
        [KSPField(isPersistant = true)]
        bool formatTime = true;
        [KSPField(isPersistant = true)]
        bool debugMode = false; // TODO add menu to modify orbit manually if debugMode is on

        // TODO: add retrograde, radial-in, and anti-normal lines too?
        MapLineRenderer radialLineRenderer;
        Vector3d radialVector { get => GetRadialVector(); }
        MapLineRenderer normalLineRenderer;
        Vector3d normalVector { get => GetNormalVector(); }
        MapLineRenderer progradeLineRenderer;
        Vector3d progradeVector { get => GetProgradeVector(); }
        MapLineRenderer forceLineRenderer;
        Vector3d _forceVector;
        Vector3d forceVector // note: dont use .Zero() on this, since it wont trigger the setter
        {
            get => _forceVector;
            set
            {
                //Util.Log($"forceLineRenderer.PointDirection: {forceLineRenderer?.PointDirection}, value: {value.normalized}, AngleBetween: {angle}, bool: {(!value.IsZero() && forceLineRenderer != null && angle > 1d)}, bool1: {!value.IsZero()}, bool2: {forceLineRenderer != null}, bool3: {angle > 1d}");
                if (_forceVector.IsZero() != value.IsZero())
                {
                    needWindowChange = true;
                    needForceLineReset = true;
                }
                _forceVector = value;
            }
        }
        bool needForceLineReset = false;
        [KSPField(isPersistant = true)]
        bool displayLines = true;

        // TODO: use property setters and fields to detect when the window needs to be reset

        static string PluginDataFolder;

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

        public override void OnAwake() // scenariomodule stuff needs to run before we call our Awake stuff 
        {
            Instance = this; // this needs to be here and not in the normal Awake()

            Util.Log("Awake called");

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

            GameEvents.onCrash.Add(ImpactDetected);
            GameEvents.onVesselExplodeGroundCollision.Add(ImpactDetected);
            GameEvents.onCollision.Add(ImpactDetected);
            GameEvents.OnCollisionEnhancerHit.Add(ImpactDetected);
        }

        void Start()
        {
            Util.Log("Start called");

            Texture2D LoadImage(string url)
            {
                Util.Log($"Loaded {url} image");
                return GameDatabase.Instance.GetTexture("CelestialBodyMover/Icons/" + url, false);
            }

            settingsGear = LoadImage("gearGreen");

            InitToolbar();

            PluginDataFolder = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/CelestialBodyMover/PluginData/");

            Tooltip.RecreateInstance();
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

            GameEvents.onCrash.Remove(ImpactDetected);
            GameEvents.onVesselExplodeGroundCollision.Remove(ImpactDetected);
            GameEvents.onCollision.Remove(ImpactDetected);
            GameEvents.OnCollisionEnhancerHit.Remove(ImpactDetected);

            DestroyAllRenderers();

            StopImpactCoroutine();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ToggleWindow() => showMainWindow = !showMainWindow;

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

            DestroyAllRenderers();

            StopImpactCoroutine();
        }

        public override void OnSave(ConfigNode root)
        {
            SaveOrbitDetails(ref root, "savedOrbits");
        }

        public override void OnLoad(ConfigNode root)
        {
            if (!LoadOrbitDetails(root, "savedOrbits"))
            {
                Util.LogError($"Failed to load saved orbits, loading original orbits");
                LoadOrbitDetails("originalOrbits");
            }

            void MakeRect(ref Rect rect, Vector2 pos)
            {
                rect = new Rect(pos.x, pos.y, rect.width, rect.height);
            }

            MakeRect(ref mainRect, mainRectPos);
            MakeRect(ref settingsRect, settingsRectPos);
        }

        private void SaveOrbitDetails(string saveNode)
        {
            ConfigNode root = new ConfigNode();
            string savePath = Path.Combine(PluginDataFolder, saveNode + ".cfg");
            File.WriteAllText(savePath, "");

            SaveOrbitDetails(ref root, saveNode);

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
            double UT = Planetarium.GetUniversalTime();
            orbits.AddValue("UT", UT);

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body.isStar)
                {
                    Util.Log($"Body is a star (bodyName: {body.name})");
                    continue;
                }
                if (!GetBodyOrbit(body, out Orbit orbit)) continue;

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
                double meanAnomaly = orbit.meanAnomaly > 0d || orbit.eccentricity > 1d ? orbit.meanAnomaly : (orbit.meanAnomaly + tau) % tau;
                AddValue("meanAnomaly", meanAnomaly);
                AddValue("epoch", UT);
                AddValue("referenceBody", orbit.referenceBody.name);

                AddValue("rotationPeriod", body.rotationPeriod);
                AddValue("initialRotation", body.initialRotation);

                Util.Log($"Saved orbit for body {body.name}: " + log);
            }
            Util.Log($"Saved orbits for {saveNode}.");

            return;
        }

        private bool LoadOrbitDetails(string saveNode)
        {
            string savePath = Path.Combine(PluginDataFolder, saveNode + ".cfg");
            Util.Log($"Loading orbits from {savePath}...");

            if (!File.Exists(savePath))
            {
                Util.LogError($"File {savePath} does not exist");
                return false;
            }

            ConfigNode root = ConfigNode.Load(savePath);
            return LoadOrbitDetails(root, saveNode);
        }

        private bool LoadOrbitDetails(ConfigNode root, string saveNode)
        {
            Util.Log($"Loading orbits from {saveNode}...");

            ConfigNode orbits = root.GetNode(saveNode);
            if (orbits == null)
            {
                Util.LogError($"Failed to find ORBITS node in {root}");
                return false;
            }
            if (!int.TryParse(orbits.GetValue("numBodies"), out int numBodies))
            {
                Util.LogError($"Failed to parse numBodies from {root}");
                return false;
            }
            if (!double.TryParse(orbits.GetValue("UT"), out double UT))
            {
                Util.LogError($"Failed to parse UT from {root}");
                return false;
            }

            if (FlightGlobals.Bodies == null)
            {
                Util.LogError($"FlightGlobals.Bodies is null");
                return false;
            }

            if (numBodies != FlightGlobals.Bodies.Count)
            {
                Util.LogError($"numBodies ({numBodies}) does not match bodies count ({FlightGlobals.Bodies.Count})");
                return false;
            }

            for (int i = 0; i < numBodies; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body.isStar)
                {
                    Util.Log($"Body is a star (bodyName: {body.name}) in {saveNode}, skipping");
                    continue;
                }
                if (!GetBodyOrbit(body, out Orbit orbit)) continue;

                ConfigNode bodyNode = orbits.GetNode(body.name);
                if (bodyNode == null)
                {
                    Util.LogError($"Failed to find node for body {body.name} in {root}");
                    continue;
                }

                string log = "";
                bool DoubleParse(string value, out double result, bool endValue = false)
                {
                    if (!double.TryParse(bodyNode.GetValue(value), out result))
                    {
                        Util.LogError($"Failed to parse {value} ({result}) for body {body.name} in {root}");
                        return false;
                    }
                    string end = endValue ? "." : ", ";
                    log += $"{value}: {result}{end}";
                    return true;
                }

                if (!DoubleParse("inclination", out double inclination)) continue;
                if (!DoubleParse("eccentricity", out double eccentricity)) continue;
                if (!DoubleParse("semiMajorAxis", out double semiMajorAxis)) continue;
                if (!DoubleParse("LAN", out double LAN)) continue;
                if (!DoubleParse("argumentOfPeriapsis", out double argumentOfPeriapsis)) continue;
                if (!DoubleParse("meanAnomaly", out double meanAnomaly)) continue;
                meanAnomaly = meanAnomaly > 0d || eccentricity > 1d ? meanAnomaly : (meanAnomaly + tau) % tau;
                if (!DoubleParse("epoch", out double epoch)) continue;
                CelestialBody referenceBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == bodyNode.GetValue("referenceBody"));
                if (referenceBody == null)
                {
                    Util.LogError($"Failed to parse referenceBody ({referenceBody}) for body {body.name} in {root}");
                    continue;
                }
                else log += $"referenceBody: {referenceBody}, ";

                if (!DoubleParse("rotationPeriod", out double rotationPeriod)) continue;
                if (!DoubleParse("initialRotation", out double initialRotation, true)) continue;

                orbit.SetOrbit(inclination, eccentricity, semiMajorAxis, LAN, argumentOfPeriapsis, meanAnomaly, epoch, referenceBody);
                FixParabolic(ref orbit);

                // TODO handle negative angular velocity? somehow?
                body.rotationPeriod = rotationPeriod;
                body.initialRotation = initialRotation;
                body.rotationAngle = (initialRotation - 360d * (1d / rotationPeriod) * epoch) % 360d; // get the proper rotationAngle for this initialRotation
                Util.Log($"Loading orbit for body {body.name}: " + log);

                if (body.tidallyLocked) body.tidallyLocked = false;
                body.CBUpdate(); // make sure this gets called before we do anything else
            }

            Util.Log($"Loaded orbits from {saveNode}.");
            return true;
        }

        void FixParabolic(ref Orbit orbit)
        {
            if (orbit.eccentricity == 1d)
            {
                orbit.SetOrbit(orbit.inclination, 1 - 1e-9d, orbit.PeR / 1e-9d, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomalyAtEpoch, orbit.epoch, orbit.referenceBody);
            }
        }

        void Update()
        {
            if (firstLoad)
            {
                SaveOrbitDetails("originalOrbits");

                firstLoad = false;
            }

            if (debugMode)
            {
                if (!CheatOptions.InfinitePropellant) CheatOptions.InfinitePropellant = true;
                if (!CheatOptions.InfiniteElectricity) CheatOptions.InfiniteElectricity = true;
                if (!CheatOptions.IgnoreMaxTemperature) CheatOptions.IgnoreMaxTemperature = true;
                if (!CheatOptions.NoCrashDamage) CheatOptions.NoCrashDamage = true;
                if (!CheatOptions.UnbreakableJoints) CheatOptions.UnbreakableJoints = true;
                if (!CheatOptions.IgnoreEVAConstructionMassLimit) CheatOptions.IgnoreEVAConstructionMassLimit = true;
                if (!CheatOptions.IgnoreKerbalInventoryLimits) CheatOptions.IgnoreKerbalInventoryLimits = true;
            }

            // TODO: look into fixing kopernicus bug in https://github.com/Kopernicus/Kopernicus/issues/825
            Vessel vessel = FlightGlobals.ActiveVessel;
            bool isFlight = HighLogic.LoadedScene == GameScenes.FLIGHT && vessel != null && vessel.state != Vessel.State.DEAD;
            if ((!isActive || !isFlight) && !forceVector.IsZero()) forceVector = Vector3d.zero;
            if (isFlight || HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                if (isFlight)
                {
                    mainBody = vessel.mainBody;
                }
                else if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                {
                    mainBody = MapView.MapCamera.target.celestialBody ?? MapView.MapCamera.target.orbit.referenceBody; // celestialBody will be null if we targeted a vessel
                }
                else if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                {
                    mainBody = FlightGlobals.GetHomeBody();
                }

                if (GetBodyOrbit(mainBody, out Orbit orbit))
                {
                    double currentUT = Planetarium.GetUniversalTime();
                    bodyVelocity = orbit.getOrbitalVelocityAtUT(currentUT);
                    if (isFlight && isActive && !FlightDriver.Pause && !vessel.HoldPhysics)
                    {
                        if (isFrozen)
                        {
                            MakeVesselStationary();
                            forceVector = GetVesselThrust();

                            StopImpactCoroutine();
                        }
                        else
                        {
                            forceVector = GetGravitationalForce(-radiusVec); // radiusVec here needs to be from body to vessel

                            if (impactCoroutine == null)
                            {
                                //Util.Log($"Starting impact coroutine");
                                impactCoroutine = StartCoroutine(ImpactCoroutine());
                            }
                        }

                        if (!forceVector.IsZero()) MoveBodyForce(vessel, mainBody, forceVector, isFrozen, radiusVec, bodyVelocity, currentUT);
                    }
                }
            }

            bool canDisplayLines = (isFlight || HighLogic.LoadedScene == GameScenes.TRACKSTATION) && displayLines;
            bool IsRendererHidden(MapLineRenderer renderer) => renderer == null || renderer.IsHidden;
            if (canDisplayLines)
            {
                if (IsRendererHidden(radialLineRenderer))
                {
                    radialLineRenderer?.Hide(false);
                    radialLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                    radialLineRenderer.Draw(mainBody, () => radialVector, "Radial Out", Color.blue, true);
                }

                if (IsRendererHidden(normalLineRenderer))
                {
                    normalLineRenderer?.Hide(false);
                    normalLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                    normalLineRenderer.Draw(mainBody, () => normalVector, "Normal", Color.magenta, true);
                }

                if (IsRendererHidden(progradeLineRenderer))
                {
                    progradeLineRenderer?.Hide(false);
                    progradeLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                    progradeLineRenderer.Draw(mainBody, () => progradeVector, "Prograde", Color.yellow, true);
                }

                if ((needForceLineReset || IsRendererHidden(forceLineRenderer)) && !forceVector.IsZero())
                {
                    forceLineRenderer?.Hide(false);
                    forceLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                    forceLineRenderer.Draw(mainBody, () => forceVector, "Force", Color.red, true);
                }
            }

            bool RendererNotHiding(MapLineRenderer renderer) => renderer != null && !renderer.IsHiding;
            if (!canDisplayLines || forceVector.IsZero())
            {
                if (RendererNotHiding(forceLineRenderer)) forceLineRenderer.Hide(true);
                if (!canDisplayLines)
                {
                    if (RendererNotHiding(radialLineRenderer)) radialLineRenderer.Hide(true);
                    if (RendererNotHiding(normalLineRenderer)) normalLineRenderer.Hide(true);
                    if (RendererNotHiding(progradeLineRenderer)) progradeLineRenderer.Hide(true);
                }
            }

            needForceLineReset = false;
            impactDetected = false;
        }

        void OnGUI()
        {
            if (GUI.skin != HighLogic.Skin)
            {
                GUI.skin = HighLogic.Skin;
            }
            
            if (lineStyle == null)
            {
                lineStyle = new GUIStyle();
                lineStyle.normal.background = Texture2D.whiteTexture;
                lineStyle.padding = new RectOffset(0, 0, 0, 0);
                lineStyle.margin = new RectOffset(0, 0, 0, 0);
                lineStyle.border = new RectOffset(0, 0, 0, 0);
            }
            
            if (showMainWindow && isKSPGUIActive && !isLoading && !isBadUI)
            {
                int id0 = GetHashCode();
                int id1 = id0 + 1;

                mainRect = ClickThruBlocker.GUILayoutWindow(id0, mainRect, MakeMainWindow, "Celestial Body Mover", GUILayout.Width(300));
                ClampToScreen(ref mainRect);
                Tooltip.Instance?.ShowTooltip(id0);
                SetRectPos(ref mainRectPos, mainRect);

                if (showSettingsWindow)
                {
                    settingsRect = ClickThruBlocker.GUILayoutWindow(id1, settingsRect, MakeSettingsWindow, "CBM Settings", GUILayout.Width(300));
                    ClampToScreen(ref settingsRect);
                    Tooltip.Instance?.ShowTooltip(id1);
                    SetRectPos(ref settingsRectPos, settingsRect);
                }
            }
        }

        private void SetRectPos(ref Vector2 pos, Rect rect)
        {
            pos.x = rect.xMin;
            pos.y = rect.yMin;
        }

        private void ClampToScreen(ref Rect rect)
        {
            float left = Mathf.Clamp(rect.x, 0, Screen.width - rect.width);
            float top = Mathf.Clamp(rect.y, 0, Screen.height - rect.height);
            rect = new Rect(left, top, rect.width, rect.height);
        }

        private IEnumerator ResetMainWindowCoroutine()
        {
            yield return new WaitForEndOfFrame(); // wait until end of frame

            mainRect = new Rect(mainRect.xMin, mainRect.yMin, -1f, -1f);
        }

        private void ResetMainWindow(ref bool needsReset) // This should only be used at the end of the current window
        { // Doing this forces the window to be resized
            if (needsReset)
            {
                StartCoroutine(ResetMainWindowCoroutine());
                needsReset = false;
            }
        }

        private void HideAllRenderers()
        {
            radialLineRenderer?.Hide(true);
            normalLineRenderer?.Hide(true);
            progradeLineRenderer?.Hide(true);
            forceLineRenderer?.Hide(true);
        }

        private void DestroyAllRenderers()
        {
            Destroy(radialLineRenderer);
            Destroy(normalLineRenderer);
            Destroy(progradeLineRenderer);
            Destroy(forceLineRenderer);
            radialLineRenderer = null;
            normalLineRenderer = null;
            progradeLineRenderer = null;
            forceLineRenderer = null;
        }

        private void MakeMainWindow(int id)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.TRACKSTATION && HighLogic.LoadedScene != GameScenes.SPACECENTER)
            {
                return;
            }
            
            Vessel vessel = FlightGlobals.ActiveVessel;
            bool isFlight = HighLogic.LoadedScene == GameScenes.FLIGHT && vessel != null && vessel.state != Vessel.State.DEAD && vessel.mainBody == mainBody;

            GUILayout.BeginHorizontal();
            string activeText = isActive ? "Deactivate CBM" : "Activate CBM";
            string activeTooltip = "";
            GUI.enabled = isFlight;
            if (!isFlight) activeTooltip = "This button is currently disabled, as you are not in flight";
            ResetWindowButton(ref isActive, activeText, activeTooltip, GUILayout.Width(300f - 30f));
            GUI.enabled = true;
            ShowSettingsButton();
            GUILayout.EndHorizontal();

            double currentUT = Planetarium.GetUniversalTime();
            if (GetBodyOrbit(mainBody, out Orbit orbit))
            {
                if (isFlight && isActive)
                {
                    GUILayout.Space(10);
                    string frozenButton = isFrozen ? "Unfreeze Craft" : "Freeze Craft";
                    if (ResetWindowButton(ref isFrozen, frozenButton))
                    {
                        if (!isFrozen && killThrottleOnUnfreeze)
                        {
                            FlightInputHandler.state.mainThrottle = 0f;
                            MakeVesselStationary(); // just for a frame
                        }
                        if (vessel.vesselTransform == null)
                        {
                            Util.LogError($"vessel.vesselTransform for {vessel.vesselName} is null");
                        }
                        else
                        {
                            currentPos = vessel.vesselTransform.position; // gives the same thing as vessel.transform.position, but NOT CoMD, CoM, CurrentCoM, or localCoM

                            //Util.Log($"currentPos: {currentPos}");
                        }

                        //Util.Log(isFrozen ? "Craft Frozen" : "Craft Unfrozen");
                    }

                    const double tolerance = 1e-3d;

                    string forceText = isFrozen ? $"Thrust:" : $"Gravitational Force:";
                    LabelValueDouble(forceText, forceVector.magnitude, "N");

                    if (isFrozen)
                    {
                        LabelValueDouble("Vessel Terrain Altitude", vessel.heightFromTerrain, "m", $"Threshold is {maxSurfaceHeight:G}m");

                        bool canUseForce = HeightValid(vessel) && !InRailsWarp() && alignmentToCenter > 0d;
                        LabelValue("Thrust Is Applied?", $"{canUseForce}", $"Must be a valid height ({HeightValid(vessel)}), not in rails warp ({!InRailsWarp()}), and thrust must be pointing towards the center ({alignmentToCenter > 0d})");
                    }
                    else
                    {
                        bool canUseForce = !vessel.LandedOrSplashed;
                        LabelValue("Gravitational Force Is Applied?", $"{canUseForce}", "Vessel must not be landed or splashed");
                    }

                    if (!forceVector.IsZero())
                    {
                        GUILayout.Space(10);
                        if (alignmentToCenter <= 0d)
                        {
                            GUILayout.Label(new GUIContent("Force not aligned", "Force not pointing towards body"));
                        }
                        else
                        {
                            GetForceDirections(forceVector, mainBody, out double forceRadial, out double forceNormal, out double forceTransverse);

                            string radialText = forceRadial >= 0d ? "Radial-Out Force:" : "Radial-In Force:";
                            LabelValueDouble(radialText, Math.Abs(forceRadial), "N");
                            string normalText = forceNormal >= 0d ? "Normal Force:" : "Anti-Normal Force:";
                            LabelValueDouble(normalText, Math.Abs(forceNormal), "N");
                            string transverseText = forceTransverse >= 0d ? "Prograde Force:" : "Retrograde Force:";
                            LabelValueDouble(transverseText, Math.Abs(forceTransverse), "N");

                            LabelValueDouble("Acceleration:", bodyAccel.magnitude, "m/s\u00B2");

                            GUILayout.Space(10);
                            if (!isFrozen || alignmentToCenter > 1d - tolerance)
                            {
                                GUILayout.Label(new GUIContent("Force fully aligned", "Force fully aligned with center of body"));
                            }
                            else
                            {
                                double angleToCenter = Math.Acos(alignmentToCenter) * radToDeg;
                                double effectiveThrust = forceVector.magnitude * alignmentToCenter;

                                LabelValueDouble("Force Alignment Offset:", angleToCenter, "\u00B0", "The angle that the force is offset from the center by, where 0\u00B0 indicates maximum force", includeUnitSpace: false);
                                LabelValueDouble("Effective Thrust:", effectiveThrust, "N", "The component of force pointing towards the center");

                                GUILayout.Space(10);
                                if (Math.Abs(alignmentToAxis) > 1d - tolerance)
                                {
                                    GUILayout.Label(new GUIContent("Torque not aligned", "Force fully aligned with axis of body, no torque"));
                                }
                                else
                                {
                                    double angleToAxis = Math.Acos(alignmentToAxis) * radToDeg;

                                    LabelValueDouble("Torque Alignment Offset:", angleToAxis, "\u00B0", "The angle that the torque is offset from the axis by, where 90\u00B0 indicates maximum torque", includeUnitSpace: false);
                                    LabelValueDouble("Torque:", torqueAlongAxis, "Nm", "The component of torque perpendicular to the axis");

                                    LabelValueDouble($"Angular Acceleration:", bodyAngularAccel * radToDeg, "\u00B0/s\u00B2");
                                }
                            }
                        }
                    }

                    if (showVesselInfo)
                    {
                        DrawLine();

                        GUILayout.Label("Vessel Details:");
                        double radius = radiusVec.magnitude;
                        double gravitationalAcceleration = mainBody.gravParameter / (radius * radius);
                        LabelValueDouble("Gravitational Acceleration:", -gravitationalAcceleration, "m/s\u00B2", includeSpace: false);
                        double centrifugalAcceleration = tau * tau * radius / (mainBody.rotationPeriod * mainBody.rotationPeriod);
                        LabelValueDouble("Centrifugal Acceleration:", centrifugalAcceleration, "m/s\u00B2");
                        LabelValueDouble("Mass:", GetVesselMass(vessel), "kg");
                    }
                }

                string displayName = mainBody.displayName.LocalizeRemoveGender();
                string refBodyDisplayName = orbit.referenceBody.displayName.LocalizeRemoveGender();
                if (showBodyInfo)
                {
                    DrawLine();

                    GUILayout.Label("Body Details:");
                    LabelValue("Body:", displayName, includeSpace: false);
                    LabelValue("Rotates:", $"{mainBody.rotates}", labelToolTip: $"Whether or not {displayName} rotates");
                    bool progradeDirection = mainBody.rotationPeriod > 0d;
                    LabelValue("Prograde Rotation:", $"{progradeDirection}", labelToolTip: $"Whether or not the direction of rotation of {displayName} around its axis is prograde relative to its orbital direction");
                    LabelValueTime("Sidereal Rotation Period:", Math.Abs(mainBody.rotationPeriod));
                    LabelValueTime("Solar Rotation Period:", Math.Abs(mainBody.solarDayLength)); // solarDayLength is set in CBUpdate
                    double angularSpeed = radToDeg * (progradeDirection ? mainBody.angularVelocity.magnitude : -mainBody.angularVelocity.magnitude);
                    LabelValueDouble("Angular Speed:", angularSpeed, "\u00B0/s");
                    LabelValueDouble("Rotation Angle:", mainBody.rotationAngle, "\u00B0", includeUnitSpace: false);
                    LabelValueDouble("Mass:", mainBody.Mass, "kg");
                }

                if (showBodyOrbitInfo)
                {
                    DrawLine();

                    double velocity = bodyVelocity.magnitude;
                    GUILayout.Label("Body Orbit Details:");
                    LabelValueDouble("Current Altitude:", orbit.radius, "m", $"Includes the radius of {refBodyDisplayName}", false);
                    LabelValueDouble("Velocity:", velocity, "m/s");
                    LabelValueDouble("Apoapsis:", orbit.ApR, "m");
                    LabelValueDouble("Periapsis:", orbit.PeR, "m");
                    LabelValueDouble("Eccentricity:", orbit.eccentricity, "");
                    LabelValueTime("Period:", orbit.period);
                    LabelValueDouble("Inclination:", orbit.inclination, "\u00B0", includeUnitSpace: false);
                    LabelValueDouble("LAN:", orbit.LAN, "\u00B0", includeUnitSpace: false);
                    LabelValueDouble("AoP:", orbit.argumentOfPeriapsis, "\u00B0", includeUnitSpace: false);
                    LabelValueDouble("Mean Anomaly:", orbit.meanAnomaly * radToDeg, "\u00B0", includeUnitSpace: false);
                    //Util.Log($"meanAnomaly: {orbit.meanAnomaly * radToDeg}");
                    LabelValue("Reference Body", $"{refBodyDisplayName}");
                }

                GUILayout.Space(10);

                if (GUILayout.Button("Reset All Orbits")) // TODO: add "are you sure" window and button
                {
                    LoadOrbitDetails("originalOrbits");
                }

                string displayLineText = displayLines ? "Hide Lines" : "Display Lines";
                string displayLinetooltip = "";
                if (!Util.MapViewEnabled()) displayLinetooltip = "This button is currently disabled, as you are not in the map view";
                GUI.enabled = Util.MapViewEnabled();
                BoolButton(ref displayLines, displayLineText, displayLinetooltip);
                GUI.enabled = true;
            }
            else if (mainBody != null && mainBody.isStar)
            {
                GUILayout.Label($"Current body ({mainBody.displayName.LocalizeRemoveGender()}) is a star, cannot use CBM");
            }

            if (debugMode)
            {
                // TODO test these again
                if (GUILayout.Button("TESTORBIT"))
                {
                    CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                    if (testBody != null)
                    {
                        Orbit testOrbit = testBody.orbit;
                        double ecc = .99;
                        testOrbit.SetOrbit(testOrbit.inclination, ecc, testOrbit.PeR / (1 - ecc), testOrbit.LAN, testOrbit.argumentOfPeriapsis, 0d, testOrbit.epoch, testOrbit.referenceBody);
                    }
                }

                if (GUILayout.Button("TESTORBIT2"))
                {
                    CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                    if (testBody != null)
                    {
                        Orbit testOrbit = testBody.orbit;
                        double ecc = 1.5;
                        testOrbit.SetOrbit(testOrbit.inclination, ecc, testOrbit.PeR / (1 - ecc), testOrbit.LAN, testOrbit.argumentOfPeriapsis, 0d, testOrbit.epoch, testOrbit.referenceBody);
                    }
                }

                if (GUILayout.Button("TESTROTATION"))
                {
                    CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                    if (testBody != null)
                    {
                        testBody.rotationPeriod = 12345d;
                        testBody.initialRotation = (testBody.rotationAngle - 360d * (1d / testBody.rotationPeriod) * Planetarium.GetUniversalTime()) % 360d;
                    }
                }

                void SetLatLong(Vector3d vector)
                {
                    if (vessel != null)
                    {
                        Vector3d dir = vector + mainBody.orbit.getPositionAtUT(currentUT);

                        double latitude = Math.Asin(vector.y) * radToDeg;
                        double longitude = Math.Atan2(vector.x, vector.z) * radToDeg; // TODO: this is right for normal/antinormal, but not for prograde/retrograde and radial-in/out
                        double altitude = Math.Round(Mathf.Max(vessel.vesselSize.x, vessel.vesselSize.y, vessel.vesselSize.z), 2) * 0.5 + 15d; // SetPosition.GetSugestedAltitude (typo)
                        //Util.Log($"latitude: {latitude}, longitude: {longitude}, altitude: {altitude}, vector: {vector}, vector.magnitude: {vector.magnitude}, dir: {dir}, mainBody.transform.position: {mainBody.transform.position}, mainBody.position: {mainBody.position}, mainBody.getPositionAtUT(currentUT): {mainBody.getPositionAtUT(currentUT)}, mainBody.orbit.getPositionAtUT(currentUT): {mainBody.orbit.getPositionAtUT(currentUT)}, mainBody.initialRotation: {mainBody.initialRotation}, rotationAngle: {mainBody.rotationAngle}");
                        FlightGlobals.fetch.SetVesselPosition(mainBody.flightGlobalsIndex, latitude, longitude, altitude, -90d, 90d, true, true);
                    }
                }
                 
                // prograde/retrograde and radial-in/out dont work. TODO fix
                if (GUILayout.Button("PROGRADE VECTOR"))
                {
                    SetLatLong(progradeVector);
                }

                if (GUILayout.Button("RETROGRADE VECTOR"))
                {
                    SetLatLong(-progradeVector);
                }

                if (GUILayout.Button("NORMAL VECTOR"))
                {
                    SetLatLong(normalVector);
                }

                if (GUILayout.Button("ANTINORMAL VECTOR"))
                {
                    SetLatLong(-normalVector);
                }

                if (GUILayout.Button("RADIAL OUT VECTOR"))
                {
                    SetLatLong(radialVector);
                }

                if (GUILayout.Button("RADIAL IN VECTOR"))
                {
                    SetLatLong(-radialVector);
                }
            }

            Tooltip.Instance?.RecordTooltip(id);
            GUI.DragWindow();
            ResetMainWindow(ref needWindowChange);
        }

        private void MakeSettingsWindow(int id)
        {
            LabelValueDouble("Max Surface Height:", maxSurfaceHeight , "m", $"Vessel must be below this height when frozen in order for its thrust to be considered valid");
            maxSurfaceHeight = Mathf.Round(GUILayout.HorizontalSlider(maxSurfaceHeight, 0f, 200f));

            LabelValueDouble("Line Length Exponent:", lineLengthExponent, "", "The exponent that determines how long the displayed lines will be");
            lineLengthExponent = Mathf.Round(GUILayout.HorizontalSlider(lineLengthExponent, 0f, 10f));

            LabelValueDouble("Minimum Impact Speed:", minImpactSpeed, "", "If KSP does not recognize an impact, this is the minimum speed at which an impact will always be counted");
            minImpactSpeed = Mathf.Round(GUILayout.HorizontalSlider(minImpactSpeed, 0f, 10f));

            GUILayout.Space(10);

            string killThrottleText = killThrottleOnUnfreeze ? "Disable Kill Throttle" : "Enable Kill Throttle";
            BoolButton(ref killThrottleOnUnfreeze, killThrottleText, "Set throttle to 0 when unfreezing the craft, to prevent RUDs");

            string homeBody = FlightGlobals.GetHomeBody().displayName.LocalizeRemoveGender();
            string formatTimeText = formatTime ? "Disable Time Formatting" : "Enable Time Formatting";
            BoolButton(ref formatTime, formatTimeText, $"Toggle between displaying time in only seconds or in {homeBody} years, {homeBody} solar days, hours, minutes, and seconds");

            bool isFlight = HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel?.mainBody == mainBody;
            string showVesselText = showVesselInfo ? "Hide Vessel Info" : "Show Vessel Info";
            string showVesselTooltip = isFlight ? "" : "\nThis button is currently disabled, as you are not in flight";
            GUI.enabled = isFlight;
            ResetWindowButton(ref showVesselInfo, showVesselText, "Toggle the display of vessel information" + showVesselTooltip);
            GUI.enabled = true;

            string showBodyText = showBodyInfo ? "Hide Body Info" : "Show Body Info";
            ResetWindowButton(ref showBodyInfo, showBodyText, "Toggle the display of body information");

            string showBodyOrbitText = showBodyOrbitInfo ? "Hide Body Orbit Info" : "Show Body Orbit Info";
            ResetWindowButton(ref showBodyOrbitInfo, showBodyOrbitText, "Toggle the display of body orbit information");

            string debugButtonText = debugMode ? "Disable Debug Mode" : "Enable Debug Mode"; // TODO take this out of the settings window before release
            ResetWindowButton(ref debugMode, debugButtonText);

            Tooltip.Instance?.RecordTooltip(id);
            GUI.DragWindow();
        }

        private void ShowSettingsButton()
        {
            if (settingsGear != null)
            {
                BoolButton(ref showSettingsWindow, new GUIContent(settingsGear, "Show Settings"), GUI.skin.button);
            }
            else
            {
                Util.LogError($"Settings gear icon not found, cannot show settings button");
            }
        }

        private bool ResetWindowButton(ref bool value, string label, string tooltip = "", params GUILayoutOption[] options)
        {
            if (BoolButton(ref value, label, tooltip, options))
            {
                needWindowChange = true;
                return true;
            }
            else return false;
        }

        private bool BoolButton(ref bool value, string label, string tooltip = "", params GUILayoutOption[] options)
        {
            return BoolButton(ref value, new GUIContent(label, tooltip), HighLogic.Skin.button, options);
        }

        private bool BoolButton(ref bool value, GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            if (GUILayout.Button(content, style, options))
            {
                value = !value;
                return true;
            }
            else return false;
        }

        private void Box(string value, string boxTooltip = "")
        {
            GUILayout.BeginVertical();
            GUILayout.Space(7); // Box is weirdly offset, need to shift it down
            GUILayout.Box(new GUIContent(value, boxTooltip), GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
        }

        private void LabelValueTime(string label, double value, string labelTooltip = "", bool includeSpace = true)
        {
            if (formatTime)
            {
                LabelValue(label, FormatTime(value), $"{value:G17}s", labelTooltip, includeSpace);
            }
            else
            {
                LabelValueDouble(label, value, "s", labelTooltip, includeSpace, false);
            }
        }

        private void LabelValue(string label, string value, string boxTooltip = "", string labelToolTip = "", bool includeSpace = true)
        {
            if (includeSpace) GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, labelToolTip), GUILayout.ExpandWidth(true));
            Box(value, boxTooltip);
            GUILayout.EndHorizontal();
        }

        private void LabelValueDouble(string label, double value, string unit, string labelTooltip = "", bool includeSpace = true, bool includeUnitSpace = true)
        {
            string space = includeUnitSpace ? " " : "";

            // TODO: make decimals configurable
            LabelValue(label, $"{value:G5}{space}{unit}", $"{value:G17}{space}{unit}", labelTooltip, includeSpace);
        }

        private void DrawLine()
        {
            GUILayout.Space(10);
            GUILayout.Box("", lineStyle, GUILayout.Height(2), GUILayout.ExpandWidth(true));
            //GUILayout.Space(10);
        }

        private string FormatTime(double t, bool formatTime)
        {
            if (formatTime)
            {
                return FormatTime(t);
            }
            else
            {
                return $"{t:G5}s"; // TODO: make decimals configurable
            }
        }

        private string FormatTime(double t)
        {
            CelestialBody homeBody = FlightGlobals.GetHomeBody();

            int tSign = Math.Sign(t);
            t = Math.Abs(t);

            double yearLength = homeBody.orbit.period;
            double dayLength = homeBody.solarDayLength;

            int years = (int)(t / yearLength);
            t %= yearLength;
            int days = (int)(t / dayLength);
            t %= dayLength;
            int hours = (int)(t / 3600d);
            t %= 3600d;
            int minutes = (int)(t / 60d);
            double seconds = t % 60d;

            string timeString = "";
            if (years > 0d) timeString += $"{years}y ";
            if (days > 0d) timeString += $"{days}d ";
            if (hours > 0d) timeString += $"{hours}h ";
            if (minutes > 0d) timeString += $"{minutes}m ";
            if (string.IsNullOrEmpty(timeString) || seconds != 0d) timeString += $"{seconds:G5}s"; // check if not == 0 to avoid 1y 0s

            return tSign != -1 ? timeString : "-" + timeString;
        }

        private bool GetBodyOrbit(CelestialBody body, out Orbit orbit)
        {
            if (body == null)
            {
                Util.LogError($"body is null in GetBodyOrbit (body: {body})");
                orbit = null;
                return false;
            }
            else if (body.isStar)
            {
                //Util.Log($"body is a star in GetBodyOrbit (body: {body.name}, body.isStar: {body.isStar})");
                orbit = null;
                return false;
            }
            else if (body.orbit == null)
            {
                Util.LogError($"body.orbit is null in GetBodyOrbit (body: {body.name}, body.orbit: {body.orbit})");
                orbit = null;
                return false;
            }
            else
            {
                orbit = body.orbit;
                return true;
            }
        }

        private Vector3d GetRadialVector()
        {
            double currentUT = Planetarium.GetUniversalTime();

            if (!GetBodyOrbit(mainBody, out Orbit orbit))
            {
                return Vector3d.zero;
            }

            return orbit.Radial(currentUT);
        }

        private Vector3d GetNormalVector()
        {
            double currentUT = Planetarium.GetUniversalTime();

            if (!GetBodyOrbit(mainBody, out Orbit orbit))
            {
                return Vector3d.zero;
            }

            Orbit fakeOrbit = new Orbit();
            const double epsilon = 1e-9d;
            if (orbit.inclination > epsilon && Math.Abs(orbit.inclination - 180d) > epsilon)
            {
                fakeOrbit = orbit;
            }
            else
            {
                // TODO test this
                double newInclination;
                if (orbit.inclination <= epsilon) newInclination = epsilon;
                else newInclination = 180d - epsilon;

                fakeOrbit.SetOrbit(newInclination, orbit.eccentricity, orbit.semiMajorAxis, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomalyAtEpoch, orbit.epoch, orbit.referenceBody);
            }

            return -fakeOrbit.Normal(currentUT); // use negative bc KSP gives left handed stuff
        }

        private Vector3d GetProgradeVector()
        {
            double currentUT = Planetarium.GetUniversalTime();

            if (!GetBodyOrbit(mainBody, out Orbit orbit))
            {
                return Vector3d.zero;
            }

            return orbit.Prograde(currentUT);
        }

        private void GetForceDirections(Vector3d forceVector, CelestialBody body, out double forceRadial, out double forceNormal, out double forceTransverse)
        {
            Vector3d radial = GetRadialVector();
            Vector3d normal = GetNormalVector();
            Vector3d prograde = GetProgradeVector();

            forceRadial = Vector3d.Dot(forceVector, radial);
            forceNormal = Vector3d.Dot(forceVector, normal);
            forceTransverse = Vector3d.Dot(forceVector, prograde);
        }

        private void MakeVesselStationary()
        { // note: if the spin of the planet is increased measurably, the orbit velocity will increase even if the surface velocity stays at 0, so the vessel can get flung off. this is expected
            Vessel vessel = FlightGlobals.ActiveVessel;

            vessel.SetWorldVelocity(Vector3d.zero);
            vessel.SetPosition(currentPos, true);
        }

        private bool HeightValid(Vessel vessel) => vessel.heightFromTerrain < maxSurfaceHeight || vessel.LandedOrSplashed;
        // TODO: need to make sure the vessel is close to the actual ground, even if underwater. use abs(terrainAltitude)? 

        private bool InRailsWarp() => TimeWarp.CurrentRate != 1f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH; // mode.high is non-physics time warp

        private double GetVesselMass(Vessel vessel) => vessel.totalMass * 1000d; // convert from tons to kg

        private Vector3d GetVesselThrust()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;

            if (!HeightValid(vessel))
            {
                //Util.Log($"Altitude too high (vessel.heightFromTerrain: {vessel.heightFromTerrain}, vessel.LandedOrSplashed: {vessel.LandedOrSplashed})");
                return Vector3d.zero;
            }

            Vector3d thrustVector = Vector3d.zero;
            //Vector3d thrustVector2 = Vector3d.zero;
            //Vector3d thrustVector3 = Vector3d.zero;

            Vector3d vesselForward = vessel.GetTransform().up;

            if (InRailsWarp()) // non-physics time warp
            {
                //BackgroundThrustVessel bVessel = vessel.FindVesselModuleImplementing<BackgroundThrustVessel>();
                //thrustVector = bVessel.Thrust;
                thrustVector = Vector3d.zero;
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

                                //Util.Log($"transformThrust: {transformThrust}, thrustVector: {thrustVector}, thrustDirectionVector: {thrustDirectionVector}, cosineLosses: {cosineLosses}, tCurrentThrust: {tCurrentThrust}");
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

            //Util.Log($"thrustVector: {thrustVector}, magnitude: {thrustVector.magnitude}");

            if (thrustVector.IsZero())
            {
                //Util.Log($"No thrust (thrustVector: {thrustVector})");
                return thrustVector;
            }

            thrustVector *= 1000d; // convert from kN to N

            return thrustVector;
        }

        private Vector3d GetGravitationalForce(Vector3d radiusVec)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel.LandedOrSplashed) return Vector3d.zero;
            double vesselMass = GetVesselMass(vessel);
            CelestialBody body = vessel.mainBody;

            Vector3d toBody = radiusVec.normalized;
            double radius = radiusVec.magnitude;

            double forceMagnitude = body.gravParameter * vesselMass / (radius * radius);

            Vector3d forceVector = toBody * forceMagnitude;

            //Util.Log($"vesselMass: {vesselMass}, radius: {radius}, body.gravParameter: {body.gravParameter}, forceMagnitude: {forceMagnitude}, forceVector: {forceVector}, toBody: {toBody}");

            return forceVector;
        }

        private void SetPositionVelocity(Vector3d position, Vector3d velocity, ref Orbit orbit, double currentUT)
        {
            const double epsilon = 1e-9d;

            double AOP = orbit.argumentOfPeriapsis;
            double meanAnomaly = orbit.meanAnomaly;
            double eccentricity = orbit.eccentricity;

            double LAN = orbit.LAN;
            double inclination = orbit.inclination;

            orbit.UpdateFromStateVectors(position, velocity, orbit.referenceBody, currentUT); // use currentUT to set the epoch to now
            //Util.Log($"orbit.eccentricity: {orbit.eccentricity}, orbit.semiMajorAxis: {orbit.semiMajorAxis}");

            if (orbit.eccentricity < epsilon && eccentricity < epsilon)
            { // AOP and meanAnomaly are not stable in circular orbits
                //Util.Log("circular orbit detected");
                orbit.SetOrbit(orbit.inclination, orbit.eccentricity, orbit.semiMajorAxis, orbit.LAN, AOP, meanAnomaly, orbit.epoch, orbit.referenceBody);
            }
            if ((orbit.inclination < epsilon && inclination < epsilon) || (Math.Abs(orbit.inclination - 180d) < epsilon && Math.Abs(eccentricity - 180d) < epsilon))
            { // AOP and LAN are not stable in flat orbits
                //Util.Log("flat orbit detected");
                orbit.SetOrbit(orbit.inclination, orbit.eccentricity, orbit.semiMajorAxis, LAN, AOP, orbit.meanAnomaly, orbit.epoch, orbit.referenceBody);
            }

            FixParabolic(ref orbit);

            if (orbit.meanAnomaly < 0d && orbit.eccentricity < 1d) // fix negative mean anomaly with elliptical orbits
            {
                orbit.meanAnomaly = (orbit.meanAnomaly + tau) % tau;
                orbit.SetOrbit(orbit.inclination, orbit.eccentricity, orbit.semiMajorAxis, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomaly, orbit.epoch, orbit.referenceBody);
            }
        }

        private void SetAngularVelocity(Vector3d originalAngularVelocity, Vector3d newAngularVelocity, ref CelestialBody body, double currentUT)
        {
            if (body.tidallyLocked) body.tidallyLocked = false; //
            
            double initialPeriod = body.rotationPeriod;
            double newPeriod = tau / newAngularVelocity.magnitude;
            //Util.Log($"rotationalInertia: {rotationalInertia}, bodyAngularAccel: {bodyAngularAccel}, torqueAlongAxis: {torqueAlongAxis}, Time.fixedDeltaTime: {Time.fixedDeltaTime}, body.angularVelocity: {body.angularVelocity} (mag: {body.angularVelocity.magnitude}), deltaAngularV: {deltaAngularV}, newAngularVelocity: {newAngularVelocity} (mag: {newAngularVelocity.magnitude}), body.rotationPeriod: {body.rotationPeriod}, newPeriod: {newPeriod}");
            body.rotationPeriod = newPeriod; // angular velocity is set by rotation period in CBUpdate
            if (Vector3d.Dot(originalAngularVelocity, newAngularVelocity) < 0d)
            {
                body.rotationPeriod = -body.rotationPeriod;
                Util.Log($"Reversed the rotation direction of {body.displayName.LocalizeRemoveGender()}! Original Angular Velocity: {originalAngularVelocity}, New Angular Velocity: {newAngularVelocity}, Original Rotation Period: {initialPeriod}, New Rotation Period: {body.rotationPeriod}");
            }
            body.initialRotation = (body.rotationAngle - 360d * (1d / newPeriod) * currentUT) % 360d; // work backwards from rotationAngle = (initialRotation + 360.0 * rotPeriodRecip * Planetarium.GetUniversalTime()) % 360.0;
            // rotationAngle is left unchanged

            body.CBUpdate(); // make sure this gets called before we do anything else
        }

        private void MoveBodyForce(Vessel vessel, CelestialBody body, Vector3d forceVector, bool isFrozen, Vector3d radiusVec, Vector3d bodyVelocity, double currentUT)
        {
            Vector3d thrustNormal = forceVector.normalized;

            alignmentToCenter = isFrozen ? Vector3d.Dot(thrustNormal, radiusVec.normalized) : 1d; // if using gravitational force, its always aligned, although in the opposite direction to thrust

            if (alignmentToCenter <= 0d)
            {
                //Util.Log($"Thrust not pointing towards planet (alignmentToCenter: {alignmentToCenter})");
                return;
            }

            double vesselMass = GetVesselMass(vessel);
            Orbit orbit = body.orbit;
            double totalMass = body.Mass + vesselMass;
            const double tolerance = 1e-3d;

            // TODO: add a way to change reference body if it gets in the hill sphere, would need to use world vectors instead of local

            double effectiveThrust = forceVector.magnitude * alignmentToCenter;
            Vector3d forceOnPlanet = thrustNormal * effectiveThrust;
            bodyAccel = forceOnPlanet / totalMass;
            Vector3d deltaV = bodyAccel * Time.fixedDeltaTime;
            Vector3d position = orbit.getRelativePositionAtUT(currentUT); // same as (orbit.getTruePositionAtUT(currentUT) - orbit.referenceBody.getTruePositionAtUT(currentUT)).xzy;

            Vector3d newVelocity = bodyVelocity + deltaV;

            //string Vector3dLog(string name, Vector3d vec)
            //{
            //    return $"{name}: {vec}, (magnitude: {vec.magnitude}) ";
            //}
            //Util.Log($"alignmentToCenter: {alignmentToCenter}, effectiveThrust: {effectiveThrust}, thrustNormal: {thrustNormal}" + Vector3dLog("radiusVec", radiusVec) + Vector3dLog("forceVector", forceVector) + Vector3dLog("forceOnPlanet", forceOnPlanet) + Vector3dLog("bodyAccel", bodyAccel) + $"deltaV: {deltaV} (magnitude: {newVelocity.magnitude - velocity.magnitude})\n" + Vector3dLog("position", position) + Vector3dLog("velocity", velocity) + Vector3dLog("newVelocity", newVelocity));

            //Util.Log($"altitude: {orbit.altitude + body.orbit.referenceBody.Radius}, position: {position}, position.magnitude: {position.magnitude}, velocity: {velocity}, newVelocity: {newVelocity}, newVelocity.magnitude: {newVelocity.magnitude}, deltaV: {deltaV}");

            SetPositionVelocity(position, newVelocity, ref orbit, currentUT);

            //Util.Log($"NEW position: {orbit.getRelativePositionAtUT(currentUT)}, NEW velocity: {orbit.getOrbitalVelocityAtUT(currentUT)}, meanAnomaly: {orbit.meanAnomaly}, argumentOfPeriapsis: {orbit.argumentOfPeriapsis}");

            if (mainBody.rotates) return; // what even uses this? maybe for like stars or something

            if (alignmentToCenter <= 1d - tolerance) // if not pointing towards center
            {
                Vector3d axis = body.angularVelocity.normalized;
                alignmentToAxis = Vector3d.Dot(thrustNormal, axis);

                if (1d - Math.Abs(alignmentToAxis) >= tolerance) // if not pointing at axis
                {
                    Vector3d torque = Vector3d.Cross(radiusVec, forceVector);
                    torqueAlongAxis = Vector3d.Dot(torque, axis);

                    double perpendicularRadius = Vector3d.Cross(axis, radiusVec).magnitude;
                    double rotationalInertia = (0.4 * body.Mass * body.Radius * body.Radius) + vesselMass * perpendicularRadius * perpendicularRadius;

                    bodyAngularAccel = torqueAlongAxis / rotationalInertia;

                    Vector3d deltaAngularV = axis * (bodyAngularAccel * Time.fixedDeltaTime);

                    Vector3d newAngularVelocity = body.angularVelocity + deltaAngularV;

                    SetAngularVelocity(body.angularVelocity, newAngularVelocity, ref body, currentUT);
                }
            }
        }

        private void MoveBodyImpact(Vessel vessel, CelestialBody body, Vector3d vesselVelocity, Vector3d radiusVec, Vector3d bodyVelocity, double currentUT)
        {
            Vector3d normal = -radiusVec.normalized;

            double vesselMass = GetVesselMass(vessel);
            double sumInverseMass = (1d / vesselMass) + (1d / body.Mass);
            Orbit orbit = body.orbit;

            Vector3d position = orbit.getRelativePositionAtUT(currentUT); // same as (orbit.getTruePositionAtUT(currentUT) - orbit.referenceBody.getTruePositionAtUT(currentUT)).xzy;
            Vector3d axis = body.angularVelocity.normalized;

            Vector3d rotationVelocity = Vector3d.Cross(radiusVec, body.angularVelocity);
            Vector3d relativeVelocity = vesselVelocity - rotationVelocity;

            double speedNormal = Vector3d.Dot(relativeVelocity, normal);
            Vector3d velocityNormal = speedNormal * normal;
            Vector3d velocityTangent = relativeVelocity - velocityNormal;

            double impulseNormal = speedNormal / sumInverseMass;
            Vector3d impulseNormalVec = impulseNormal * normal;
            Vector3d deltaV = impulseNormalVec / body.Mass;

            Vector3d newBodyVelocity = bodyVelocity + deltaV;

            SetPositionVelocity(position, newBodyVelocity, ref orbit, currentUT);
            string displayName = body.displayName.LocalizeRemoveGender();

            if (mainBody.rotates)
            {
                double perpendicularRadius = Vector3d.Cross(radiusVec, axis).magnitude;
                double vesselRotInertia = vesselMass * perpendicularRadius * perpendicularRadius;
                double bodyRotInertia = (0.4 * body.Mass * body.Radius * body.Radius);
                double rotationalInertia = bodyRotInertia + vesselRotInertia;

                //Vector3d angularDeltaV = axis * Vector3d.Dot(Vector3d.Cross(-radiusVec, relativeVelocity), axis) * vesselMass / rotationalInertia;

                double impulseTangent = velocityTangent.magnitude / sumInverseMass;
                impulseTangent = Util.Clamp(impulseTangent, -Math.Abs(impulseNormal), Math.Abs(impulseNormal));
                Vector3d impulseTangentVec = velocityTangent.normalized * impulseTangent;
                Vector3d angularDeltaV = axis * Vector3d.Dot(Vector3d.Cross(impulseTangentVec, radiusVec), axis) / rotationalInertia;

                Vector3d newAngularVelocity = body.angularVelocity + angularDeltaV;

                //Util.Log($"bodyVelocity: {bodyVelocity} (mag: {bodyVelocity.magnitude}), newBodyVelocity: {newBodyVelocity} (mag: {newBodyVelocity.magnitude})\nangularVelocity: {body.angularVelocity}, newAngularVelocity: {newAngularVelocity} (mag: {newAngularVelocity.magnitude})\n deltaV: {deltaV}, angularDeltaV: {angularDeltaV}\n rotationalInertia: {rotationalInertia}, vesselRotInertia: {vesselRotInertia}, bodyRotInertia: {bodyRotInertia}, GetVesselMass(vessel): {GetVesselMass(vessel)}, body.Mass: {body.Mass}\n vesselVelocity: {vesselVelocity} (mag: {vesselVelocity.magnitude}), relativeVelocity: {relativeVelocity} (mag: {relativeVelocity.magnitude}), rotationVelocity: {rotationVelocity} (mag: {rotationVelocity.magnitude})\n radiusVec: {radiusVec}, axis: {axis}, Vector3d.Dot(relativeVelocity, normal): {Vector3d.Dot(relativeVelocity, normal)}");
                //Util.Log($"speedNormal: {speedNormal}, velocityNormal: {velocityNormal}, velocityTangent: {velocityTangent}, impulseNormal: {impulseNormal}, impulseNormalVec: {impulseNormalVec}, velocityTangent.magnitude / sumInverseMass: {velocityTangent.magnitude / sumInverseMass}, impulseTangent: {impulseTangent}, impulseTangentVec: {impulseTangentVec}");

                double initialPeriod = body.rotationPeriod;
                int initialDirection = Math.Sign(initialPeriod);
                double initialAngVelocity = body.angularVelocity.magnitude * initialDirection * radToDeg;
                SetAngularVelocity(body.angularVelocity, newAngularVelocity, ref body, currentUT);

                int newDirection = Math.Sign(body.rotationPeriod);
                double newAngVelocity = body.angularVelocity.magnitude * newDirection * radToDeg;
                bool changedDirection = initialDirection != newDirection;
                string msg = $"A vessel impacted with {displayName} at a velocity of {vesselVelocity.magnitude:G5}m/s, changing the velocity of {displayName} from {bodyVelocity.magnitude:G5}m/s to {newBodyVelocity.magnitude:G5}m/s (change of {(newBodyVelocity.magnitude - bodyVelocity.magnitude)}m/s), " +
                    $"and changing its angular velocity from {initialAngVelocity:G5}\u00B0/s to {newAngVelocity:G5}\u00B0/s (change of {newAngVelocity - initialAngVelocity}\u00B0/s, or {body.rotationPeriod - initialPeriod}s)" + (changedDirection ? ", reversing the direction of its rotation." : ".");
                Util.Log(msg);
                PopupDialog.SpawnPopupDialog(popupAnchor, popupAnchor, "CBMImpactDetected", "Impact Detected!", msg, Localizer.Format("#autoLOC_190905"), false, HighLogic.UISkin, false);
            }
            else // what even uses this? maybe for like stars or something
            {
                string msg = $"A vessel impacted with {displayName} at a velocity of {vesselVelocity.magnitude:G5}m/s, changing the velocity of {displayName} from {bodyVelocity.magnitude:G5}m/s to {newBodyVelocity.magnitude:G5}m/s (change of {(newBodyVelocity.magnitude - bodyVelocity.magnitude):G5}m/s).";
                Util.Log(msg);
                PopupDialog.SpawnPopupDialog(popupAnchor, popupAnchor, "CBMImpactDetected", "Impact Detected!", msg, Localizer.Format("#autoLOC_190905"), false, HighLogic.UISkin, false);
            }
        }

        private void ImpactDetected(EventReport evt) => impactDetected = true;
        private void ImpactDetected(Vessel vessel) => impactDetected = true;
        private void ImpactDetected(Part p, RaycastHit r) => impactDetected = true;

        private void StopImpactCoroutine()
        {
            if (impactCoroutine != null)
            {
                //Util.Log($"Stopping impact coroutine");
                StopCoroutine(impactCoroutine);
                impactCoroutine = null;
            }
        }

        private bool StopImpactCoroutine(Vessel vesselParam)
        {
            if (!HighLogic.LoadedSceneIsFlight || vesselParam == null || !GetBodyOrbit(vesselParam.mainBody, out _) || !isActive || isFrozen)
            {
                //Util.Log($"StopImpactCoroutine(Vessel vesselParam) triggered. !HighLogic.LoadedSceneIsFlight: {!HighLogic.LoadedSceneIsFlight}, vesselParam == null: {vesselParam == null}, !isActive: {!isActive}, isFrozen: {isFrozen}");
                impactCoroutine = null;
                return true;
            }
            else return false;
        }

        private IEnumerator ImpactCoroutine()
        {
            try
            {
                //Util.Log($"Running ImpactCoroutine 1, impactDetected: {impactDetected}");

                Vessel vessel = FlightGlobals.ActiveVessel;

                if (StopImpactCoroutine(vessel)) yield break;

                //Util.Log($"Running ImpactCoroutine 2");

                bool VectorNullOrZero(Vector3d vector) => vector == null || vector.IsZero();

                while (vessel != null && vessel == FlightGlobals.ActiveVessel && vessel.state != Vessel.State.DEAD && !vessel.LandedOrSplashed && !impactDetected)
                {
                    //Util.Log($"Running loop in impact coroutine, impactDetected: {impactDetected}");
                    Situations situation = vessel.situation;
                    if (situation == Situations.FLYING || situation == Situations.SUB_ORBITAL || situation == Situations.ORBITING || situation == Situations.ESCAPING)
                    {
                        //Util.Log($"Valid situation in ImpactCoroutine");
                        vesselVelocity = vessel.obt_velocity;
                        vesselVerticalSpeed = vessel.verticalSpeed;
                        vesselID = vessel.id;
                    }
                    else
                    {
                        vesselVelocity = Vector3.zero;
                        vesselVerticalSpeed = 0f;
                    }

                    yield return new WaitForFixedUpdate(); // run on fixed update for physics stuff
                }

                //Util.Log($"Triggered impact coroutine 1");

                if (StopImpactCoroutine(vessel)) yield break;

                //Util.Log($"Triggered impact coroutine 2");

                if (vessel.id == vesselID && !VectorNullOrZero(vesselVelocity) && GetBodyOrbit(mainBody, out _) && !VectorNullOrZero(bodyVelocity))
                {
                    // some of this is copied from Vessel.CheckKill()
                    if (vesselVerticalSpeed > minImpactSpeed || (!vessel.LandedOrSplashed && !vessel.HoldPhysics && vessel.altitude < ((vessel.terrainAltitude != -1d) ? vessel.terrainAltitude : -250d)))
                    {
                        //Util.Log($"Triggered impact coroutine 3");
                        double currentUT = Planetarium.GetUniversalTime();
                        MoveBodyImpact(vessel, mainBody, vesselVelocity, radiusVec, bodyVelocity, currentUT);
                        vessel.MurderCrew(); // probably not needed
                        vessel.Die(); // probably not needed
                        vessel.rootPart.explode((float)(vessel.terrainAltitude - vessel.altitude));
                    }
                    else
                    {
                        //Util.Log($"Not high enough velocity");
                    }
                }
                else
                {
                    //Util.Log($"Not triggered. vessel.id: {vessel.id},vesselID: {vesselID}, !VectorNullOrZero(vesselVelocity): {!VectorNullOrZero(vesselVelocity)}, mainBody: {mainBody}, !VectorNullOrZero(bodyVelocity): {!VectorNullOrZero(bodyVelocity)}");
                }
            }
            finally
            {
                //Util.Log($"Setting impact coroutine to null");
                impactCoroutine = null;
            }
        }
    }
}