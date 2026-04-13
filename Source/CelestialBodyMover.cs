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

        internal static bool MapViewEnabled() => MapView.MapIsEnabled && !HighLogic.LoadedSceneIsEditor && HighLogic.LoadedSceneIsFlight;
    }

    [KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public class CelestialBodyMover : ScenarioModule
    {
        // note: KSPField will not work on static fields

        internal static CelestialBodyMover Instance { get; private set; } // set up a singleton static instance

        const double tau = 2d * Math.PI; // Math.Tau is in .NET 5
        const double radToDeg = 180d / Math.PI; // unity only has floats
        const double degToRad = Math.PI / 180d; // unity only has floats

        ToolbarControl toolbarControl = null;

        GUISkin skin;
        GUIStyle lineStyle;

        [KSPField(isPersistant = true)]
        bool isWindowOpen = false;
        bool isKSPGUIActive = true; // for some reason, this initially only turns to true when you turn off and on the KSP GUI
        bool isLoading = false;
        bool isBadUI = false;

        //bool? firstLoad = null;
        [KSPField(isPersistant = true)]
        bool firstLoad = true;

        [KSPField(isPersistant = true)]
        internal bool isActive = false;
        //bool _isFrozen = false; // this should be false by default, even after loading
        //bool isFrozen
        //{
        //    get => _isFrozen;
        //    set
        //    {
        //        if (_isFrozen != value)
        //        {
        //            needWindowChange = true;
        //            _isFrozen = value;
        //        }
        //    }
        //} // TODO test this needWindowChange
        bool isFrozen = false; // this should be false by default, even after loading

        Vector3d _forceVector;
        Vector3d forceVector // note: dont use .Zero() on this, since it wont trigger the setter
        {
            get => _forceVector;
            set 
            {
                double angle = double.NaN;
                if (!value.IsZero() && forceLineRenderer != null)
                {
                    angle = UtilMath.AngleBetween(forceLineRenderer.PointDirection, value) * radToDeg;
                }
                
                //Util.Log($"forceLineRenderer.PointDirection: {forceLineRenderer?.PointDirection}, value: {value.normalized}, AngleBetween: {angle}, bool: {(!value.IsZero() && forceLineRenderer != null && angle > 1d)}, bool1: {!value.IsZero()}, bool2: {forceLineRenderer != null}, bool3: {angle > 1d}");
                if (_forceVector.IsZero() != value.IsZero())
                {
                    needWindowChange = true;
                    needForceLineReset = true;
                }
                else if (angle > 1d)
                {
                    //Util.Log("resetting line");
                    needForceLineReset = false;
                }
                _forceVector = value;
            }
        }
        Vector3d radiusVec;
        Vector3d bodyAccel;
        double _alignmentToCenter;
        double alignmentToCenter
        {
            get => _alignmentToCenter;
            set
            {
                //Util.Log($"_alignmentToCenter: {_alignmentToCenter}, value: {value}, bool1: {(_alignmentToCenter <= 0d)}, bool2: {(value <= 0d)}");
                if ((_alignmentToCenter <= 0d) != (value <= 0d))
                {
                    //Util.Log($"alignmentToCenter changed from {_alignmentToCenter} to {value}, resetting window");
                    needWindowChange = true;
                }
                _alignmentToCenter = value;
            }
        }
        double _alignmentToAxis;
        double alignmentToAxis
        {
            get => _alignmentToAxis;
            set
            {
                const double tolerance = 1e-3d;
                //Util.Log($"_alignmentToAxis: {_alignmentToAxis}, value: {value}, bool1: {(Math.Abs(_alignmentToAxis) > 1d - tolerance)}, bool2: {(Math.Abs(value) > 1d - tolerance)}");
                if ((Math.Abs(_alignmentToAxis) > 1d - tolerance) != (Math.Abs(value) > 1d - tolerance))
                {
                    //Util.Log($"alignmentToAxis changed from {_alignmentToAxis} to {value}, resetting window");
                    needWindowChange = true;
                }
                _alignmentToAxis = value;
            }
        }
        double torqueAlongAxis;
        double bodyAngularAccel;

        CelestialBody _mainBody;
        CelestialBody mainBody
        {
            get => _mainBody;
            set
            {
                if (_mainBody != value)
                {
                    //Util.Log($"mainBody changed from {_mainBody?.name} to {value?.name}, resetting window");
                    needWindowChange = true;
                    HideAllRenderers();
                }
                _mainBody = value;
            }
        }

        Vector3d currentPos;

        Rect mainRect = new Rect(0, 0, -1, -1);
        [KSPField(isPersistant = true)]
        Vector2 mainRectPos = new Vector2(100, 100);
        bool needWindowChange = false;

        internal CBMSettings settings;

        MapLineRenderer radialLineRenderer;
        MapLineRenderer normalLineRenderer;
        MapLineRenderer progradeLineRenderer;
        MapLineRenderer forceLineRenderer;
        bool? needForceLineReset = null; // visibilityChange true or false
        [KSPField(isPersistant = true)]
        bool displayLines = false;

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

            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);

            lineStyle = new GUIStyle();
            lineStyle.normal.background = Texture2D.whiteTexture;
            lineStyle.padding = new RectOffset(0, 0, 0, 0);
            lineStyle.margin = new RectOffset(0, 0, 0, 0);
            lineStyle.border = new RectOffset(0, 0, 0, 0);

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

            PluginDataFolder = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/CelestialBodyMover/PluginData/");

            settings = HighLogic.CurrentGame.Parameters.CustomParams<CBMSettings>();

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

            Destroy(radialLineRenderer);
            Destroy(normalLineRenderer);
            Destroy(progradeLineRenderer);
            Destroy(forceLineRenderer);
            radialLineRenderer = null;
            normalLineRenderer = null;
            progradeLineRenderer = null;
            forceLineRenderer = null;

            if (Instance == this)
            {
                Instance = null;
            }
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

            Destroy(radialLineRenderer);
            Destroy(normalLineRenderer);
            Destroy(progradeLineRenderer);
            Destroy(forceLineRenderer);
            radialLineRenderer = null;
            normalLineRenderer = null;
            progradeLineRenderer = null;
            forceLineRenderer = null;
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
                LoadOrbitDetails(Path.Combine(PluginDataFolder, "originalOrbits.cfg"));
            }

            mainRect = new Rect(mainRectPos.x, mainRectPos.y, mainRect.width, mainRect.height);
        }

        private void SaveOrbitDetails(string saveFile)
        {
            ConfigNode root = new ConfigNode();
            string savePath = Path.Combine(PluginDataFolder, saveFile);
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
            double UT = Planetarium.GetUniversalTime();
            orbits.AddValue("numBodies", FlightGlobals.Bodies.Count);
            orbits.AddValue("UT", UT);

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

                AddValue("inclination", orbit.inclination);
                AddValue("eccentricity", orbit.eccentricity);
                AddValue("semiMajorAxis", orbit.semiMajorAxis);
                AddValue("LAN", orbit.LAN);
                AddValue("argumentOfPeriapsis", orbit.argumentOfPeriapsis);
                AddValue("meanAnomaly", orbit.meanAnomaly);
                AddValue("epoch", UT);
                AddValue("referenceBody", orbit.referenceBody.name);

                AddValue("rotationPeriod", body.rotationPeriod);
                AddValue("initialRotation", body.initialRotation);

                Util.Log($"Saved orbit for body {body.name}: " + log);
            }
            Util.Log($"Saved orbits for {saveNode}");

            return;
        }

        private bool LoadOrbitDetails(string saveFile)
        {
            string savePath = Path.Combine(PluginDataFolder, saveFile);
            Util.Log($"Loading orbits from {savePath}...");

            if (!File.Exists(savePath))
            {
                Util.LogError($"File {savePath} does not exist");
                return false;
            }

            ConfigNode root = ConfigNode.Load(savePath);
            return LoadOrbitDetails(root, Path.GetFileNameWithoutExtension(savePath));
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
                ConfigNode bodyNode = orbits.GetNode(body.name);
                if (bodyNode == null)
                {
                    Util.LogError($"Failed to find node for body {body.name} in {root}");
                    continue;
                }

                string log = "";
                bool DoubleParse(string value, out double result)
                {
                    if (!double.TryParse(bodyNode.GetValue(value), out result))
                    {
                        Util.LogError($"Failed to parse {value} for body {body.name} in {root}");
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
                if (!DoubleParse("meanAnomaly", out double meanAnomaly)) continue;
                if (!DoubleParse("epoch", out double epoch)) continue;
                CelestialBody referenceBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == bodyNode.GetValue("referenceBody"));
                if (referenceBody == null)
                {
                    Util.LogError($"Failed to parse {referenceBody} for body {body.name} in {root}");
                    continue;
                }

                if (!DoubleParse("rotationPeriod", out double rotationPeriod)) continue;
                if (!DoubleParse("initialRotation", out double initialRotation)) continue;

                Orbit bodyOrbit = body.orbit;

                bodyOrbit.SetOrbit(inclination, eccentricity, semiMajorAxis, LAN, argumentOfPeriapsis, meanAnomaly, epoch, referenceBody);

                body.rotationPeriod = rotationPeriod;
                body.initialRotation = initialRotation;
                body.rotationAngle = (initialRotation - 360d * (1d / rotationPeriod) * epoch) % 360d; // get the proper rotationAngle for this initialRotation
                Util.Log($"Loading orbit for body {body.name}: " + log);

                body.CBUpdate(); // make sure this gets called before we do anything else
            }

            Util.Log($"Loaded orbits from {saveNode}");
            return true;
        }

        // event function order: https://docs.unity3d.com/560/Documentation/Manual/ExecutionOrder.html

        void Update()
        {
            if (firstLoad)
            {
                SaveOrbitDetails("originalOrbits.cfg");

                firstLoad = false;
            }

            if (settings.debugMode)
            {
                if (!CheatOptions.InfinitePropellant) CheatOptions.InfinitePropellant = true;
                if (!CheatOptions.InfiniteElectricity) CheatOptions.InfiniteElectricity = true;
                if (!CheatOptions.IgnoreMaxTemperature) CheatOptions.IgnoreMaxTemperature = true;
                if (!CheatOptions.NoCrashDamage) CheatOptions.NoCrashDamage = true;
                if (!CheatOptions.UnbreakableJoints) CheatOptions.UnbreakableJoints = true;
                if (!CheatOptions.IgnoreEVAConstructionMassLimit) CheatOptions.IgnoreEVAConstructionMassLimit = true;
                if (!CheatOptions.IgnoreKerbalInventoryLimits) CheatOptions.IgnoreKerbalInventoryLimits = true;
            }

            if (!isActive && !forceVector.IsZero()) forceVector = Vector3d.zero;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && vessel != null)
            {
                mainBody = vessel.mainBody;
                Orbit vOrbit = vessel.orbit;
                Util.Log($"inc: {vOrbit.inclination}, ecc: {vOrbit.eccentricity}, sma: {vOrbit.semiMajorAxis}, LAN: {vOrbit.LAN}, arg: {vOrbit.argumentOfPeriapsis}, M: {vOrbit.meanAnomaly}, epoch: {vOrbit.epoch}");
                if (mainBody != null && !mainBody.isStar)
                {
                    if (isActive && !FlightDriver.Pause && !vessel.HoldPhysics)
                    {
                        radiusVec = mainBody.position - vessel.GetWorldPos3D(); // vector from vessel to body
                        if (isFrozen) // note: if the spin of the planet is increased measureably, the orbit velocity will increase even if the surface velocity stays at 0, so the vessel can get flung off
                        {
                            MakeVesselStationary();
                            forceVector = GetVesselThrust();
                        }
                        else
                        {
                            forceVector = GetGravitationalForce(-radiusVec); // radiusVec here needs to be from body to vessel
                        }

                        if (!forceVector.IsZero()) MovePlanet(forceVector, isFrozen, radiusVec);
                    }

                    bool CheckRendererAvailable(MapLineRenderer renderer) => renderer == null || renderer.IsHidden;
                    if (displayLines && (CheckRendererAvailable(radialLineRenderer) && CheckRendererAvailable(normalLineRenderer) && CheckRendererAvailable(progradeLineRenderer)))
                    {
                        //Util.Log($"DRAWING LINES");

                        radialLineRenderer?.Hide(false);
                        normalLineRenderer?.Hide(false);
                        progradeLineRenderer?.Hide(false);

                        radialLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                        normalLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                        progradeLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();

                        GetForceDirections(mainBody, out Vector3d radial, out Vector3d normal, out Vector3d transverse);
                        radialLineRenderer.Draw(mainBody, radial, "Radial", Color.blue, true);
                        normalLineRenderer.Draw(mainBody, normal, "Normal", Color.magenta, true);
                        progradeLineRenderer.Draw(mainBody, transverse, "Prograde", Color.yellow, true);
                    }

                    if (displayLines && !forceVector.IsZero() && (needForceLineReset.HasValue || CheckRendererAvailable(forceLineRenderer)))
                    {
                        bool visibilityChanged = true;
                        if (needForceLineReset.HasValue)
                        {
                            visibilityChanged = needForceLineReset.Value;
                        }
                        forceLineRenderer?.Hide(visibilityChanged);
                        forceLineRenderer = MapView.MapCamera.gameObject.AddComponent<MapLineRenderer>();
                        forceLineRenderer.Draw(mainBody, forceVector, "Force", Color.red, visibilityChanged);
                    }
                }
            }

            bool CheckRendererHiding(MapLineRenderer renderer) => renderer != null && !renderer.IsHiding;
            if ((CheckRendererHiding(radialLineRenderer) && CheckRendererHiding(normalLineRenderer) && CheckRendererHiding(progradeLineRenderer)) && (HighLogic.LoadedScene != GameScenes.FLIGHT || !displayLines))
            {
                //Util.Log($"CLEARING LINES");

                radialLineRenderer?.Hide(true);
                normalLineRenderer?.Hide(true);
                progradeLineRenderer?.Hide(true);
            }
            else if (CheckRendererHiding(forceLineRenderer) && (HighLogic.LoadedScene != GameScenes.FLIGHT || !displayLines || forceVector.IsZero()))
            {
                bool visibilityChanged = true;
                if (needForceLineReset.HasValue)
                {
                    visibilityChanged = needForceLineReset.Value;
                }
                forceLineRenderer?.Hide(visibilityChanged);
            }

            needForceLineReset = null;
        }

        void OnGUI()
        {
            if (isWindowOpen && isKSPGUIActive && !isLoading && !isBadUI)
            {
                GUI.skin = skin;
                int id0 = GetHashCode();

                mainRect = ClickThruBlocker.GUILayoutWindow(id0, mainRect, MakeMainWindow, "Celestial Body Mover", GUILayout.Width(300));
                ClampToScreen(ref mainRect);
                Tooltip.Instance?.ShowTooltip(id0);

                mainRectPos.x = mainRect.xMin;
                mainRectPos.y = mainRect.yMin;
            }
        }

        private void ClampToScreen(ref Rect rect)
        {
            float left = Mathf.Clamp(rect.x, 0, Screen.width - rect.width);
            float top = Mathf.Clamp(rect.y, 0, Screen.height - rect.height);
            rect = new Rect(left, top, rect.width, rect.height);
        }

        private void ResetWindow(ref bool needsReset, ref Rect rect) // This should only be used at the end of the current window
        { // Doing this forces the window to be resized
            if (needsReset)
            {
                rect = new Rect(rect.xMin, rect.yMin, -1f, -1f);
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

        private void MakeMainWindow(int id)
        {
            string activeText = isActive ? "Deactivate CBM" : "Activate CBM";
            if (GUILayout.Button(activeText))
            {
                isActive = !isActive;
                needWindowChange = true;

                //Util.Log(isActive ? "CBM Activated" : "CBM Deactivated");
            }

            double currentUT = Planetarium.GetUniversalTime();
            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel?.mainBody; // mainBody property is already set in Update()
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && vessel != null && body != null && !body.isStar)
            {
                Orbit orbit = body.orbit;

                void Box(string value, string boxTooltip = "")
                {
                    GUILayout.BeginVertical();
                    GUILayout.Space(5); // Box is weirdly offset, need to shift it down
                    GUILayout.Box(new GUIContent(value, boxTooltip), GUILayout.ExpandWidth(true));
                    GUILayout.EndVertical();
                }

                void LabelValue(string label, string value, string boxTooltip = "", string labelToolTip = "", bool includeSpace = true)
                {
                    if (includeSpace) GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(new GUIContent(label, labelToolTip), GUILayout.ExpandWidth(true));
                    Box(value, boxTooltip);
                    GUILayout.EndHorizontal();
                }

                void LabelValueDouble(string label, double value, string unit, string labelTooltip = "", bool includeSpace = true)
                {
                    // TODO: make decimals configurable
                    LabelValue(label, $"{value:G5}{unit}", $"{value:G17}{unit}", labelTooltip, includeSpace);
                }

                void DrawLine()
                {
                    GUILayout.Space(10);
                    GUILayout.Box("", lineStyle, GUILayout.Height(2), GUILayout.ExpandWidth(true));
                    //GUILayout.Space(10);
                }

                if (isActive)
                {
                    GUILayout.Space(10);
                    string frozenButton = isFrozen ? "Unfreeze Craft" : "Freeze Craft";
                    if (GUILayout.Button(frozenButton))
                    {
                        isFrozen = !isFrozen;
                        if (!isFrozen && settings.killThrottleOnUnfreeze)
                        {
                            FlightInputHandler.state.mainThrottle = 0f;
                            MakeVesselStationary(); // just for a frame
                        }
                        needWindowChange = true;
                        if (vessel.vesselTransform == null)
                        {
                            Util.LogError($"vessel.vesselTransform for {vessel.vesselName} is null");
                        }
                        else
                        {
                            currentPos = vessel.vesselTransform.position;

                            //Util.Log($"currentPos: {currentPos}");
                        }

                        //Util.Log(isFrozen ? "Craft Frozen" : "Craft Unfrozen");
                    }

                    const double tolerance = 1e-3d;

                    string forceText = isFrozen ? $"Thrust:" : $"Gravitational Force:";
                    LabelValue(forceText, $"{forceVector.magnitude:N2}N");

                    if (isFrozen)
                    {
                        LabelValueDouble("Vessel Terrain Altitude", vessel.heightFromTerrain, "m", $"Threshold is {settings.maxSurfaceHeight:G}m");

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
                            GetForceDirections(forceVector, body, out double forceRadial, out double forceNormal, out double forceTransverse);

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

                                LabelValueDouble("Force Alignment Offset:", angleToCenter, "\u00B0", "The angle that the force is offset from the center by");
                                LabelValueDouble("Effective Thrust:", effectiveThrust, "N", "The component of force pointing towards the center");

                                GUILayout.Space(10);
                                if (Math.Abs(alignmentToAxis) > 1d - tolerance)
                                {
                                    GUILayout.Label(new GUIContent("Torque not aligned", "Force fully aligned with axis of body, no torque"));
                                }
                                else
                                {
                                    double angleToAxis = Math.Acos(alignmentToAxis) * radToDeg;

                                    LabelValueDouble("Torque Alignment Offset:", angleToAxis, "\u00B0", "The angle that the torque is offset from the axis by");
                                    LabelValueDouble("Torque:", torqueAlongAxis, "Nm", "The component of torque perpendicular to the axis");

                                    LabelValueDouble($"Angular Acceleration:", bodyAngularAccel * radToDeg, "\u00B0/s\u00B2");
                                }
                            }
                        }
                    }
                }

                DrawLine();

                GUILayout.Label("Body Details:");
                LabelValue("Body:", body.displayName.LocalizeRemoveGender(), includeSpace: false);
                LabelValueDouble("Rotation Period:", body.rotationPeriod, "s");
                LabelValueDouble("Rotation Angle:", body.rotationAngle, "\u00B0");
                LabelValueDouble("Mass:", body.Mass, "kg");

                DrawLine();

                GUILayout.Space(10);

                string refBody = orbit.referenceBody.displayName.LocalizeRemoveGender();
                double velocity = orbit.getOrbitalVelocityAtUT(currentUT).magnitude;
                GUILayout.Label("Body Orbit Details:");
                LabelValueDouble("Current Altitude:", orbit.radius, "m", $"Includes the radius of {refBody}", false);
                LabelValueDouble("Velocity:", velocity, "m/s");
                LabelValueDouble("Apoapsis:", orbit.ApR, "m");
                LabelValueDouble("Periapsis:", orbit.PeR, "m");
                LabelValueDouble("Eccentricity:", orbit.eccentricity, "");
                LabelValueDouble("Period:", orbit.period, "s"); // TODO: add format year but make it toggleable
                LabelValueDouble("Inclination:", orbit.inclination, "\u00B0");
                LabelValueDouble("LAN:", orbit.LAN, "\u00B0");
                LabelValueDouble("AoP:", orbit.argumentOfPeriapsis, "\u00B0");
                LabelValueDouble("Mean Anomaly:", orbit.meanAnomaly * radToDeg, "\u00B0");
                //Util.Log($"meanAnomaly: {orbit.meanAnomaly * radToDeg}");
                LabelValue("Reference Body", $"{refBody}");

                GUILayout.Space(10);

                if (GUILayout.Button("Reset All Orbits"))
                {
                    LoadOrbitDetails(Path.Combine(PluginDataFolder, "originalOrbits.cfg"));
                }

                GUILayout.Space(10);

                string displayLineText = displayLines ? "Hide Lines" : "Display Lines";
                if (GUILayout.Button(displayLineText))
                {
                    displayLines = !displayLines;
                }

                GUILayout.Space(10);
            }
            else if (body != null && body.isStar)
            {
                GUILayout.Label($"Current body ({body.displayName.LocalizeRemoveGender()}) is a star, cannot use CBM");
            }

            if (settings.debugMode)
            {
                // TODO test these again
                if (GUILayout.Button("TESTORBIT"))
                {
                    CelestialBody testBody = FlightGlobals.Bodies.FirstOrDefault(b => b.name == "Scylla");
                    if (testBody != null)
                    {
                        Orbit testOrbit = testBody.orbit;
                        testOrbit.SetOrbit(testOrbit.inclination, .5, testOrbit.semiMajorAxis, testOrbit.LAN, testOrbit.argumentOfPeriapsis, testOrbit.meanAnomalyAtEpoch, testOrbit.epoch, testOrbit.referenceBody);
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
            }

            Tooltip.Instance?.RecordTooltip(id);
            GUI.DragWindow();
            ResetWindow(ref needWindowChange, ref mainRect);
        }

        //private string FormatTime(double t)
        //{
        //    // TODO, add years? would have to be similar to useHomeSolarDay (try KSPUtil.dateTimeFormatter.Year?)
        //    int days = (int)Math.Floor(t / Math.Round(solarDayLength)); // round to avoid stuff like 3d 24h 0m 0s, TODO this isnt working
        //    t -= days * Math.Round(solarDayLength);
        //    int hours = (int)Math.Floor(t / (60d * 60d));
        //    t -= hours * 60d * 60d;
        //    int minutes = (int)Math.Floor(t / 60d);
        //    t -= minutes * 60d;
        //    if (days > 0d)
        //        return $"{days}d {hours}h {minutes}m {FormatDecimals(t)}s";
        //    else if (hours > 0d)
        //        return $"{hours}h {minutes}m {FormatDecimals(t)}s";
        //    else if (minutes > 0d)
        //        return $"{minutes}m {FormatDecimals(t)}s";
        //    return $"{FormatDecimals(t)}s";
        //}

        private void GetForceDirections(CelestialBody body, out Vector3d radial, out Vector3d normal, out Vector3d transverse)
        {
            double currentUT = Planetarium.GetUniversalTime();

            if (body == null || body.orbit == null)
            {
                Util.LogError($"body or body.orbit is null in GetForceDirections (body: {body}, body.orbit: {body?.orbit})");
                radial = Vector3d.zero;
                normal = Vector3d.zero;
                transverse = Vector3d.zero;
                return;
            }

            Orbit orbit = body.orbit;
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

            radial = fakeOrbit.Radial(currentUT);
            normal = -fakeOrbit.Normal(currentUT); // use negative bc KSP gives left handed stuff
            transverse = fakeOrbit.Prograde(currentUT);

            //Util.Log($"Force directions: Radial: {radial}, radial2: {radial2}, Normal: {normal}, normal2: {normal2}, Transverse: {transverse}, transverse2: {transverse2}");
            //Util.Log($"forceVector: {forceVector}, body.angularVelocity.normalized: {body.angularVelocity.normalized}");
        }

        private void GetForceDirections(Vector3d forceVector, CelestialBody body, out double forceRadial, out double forceNormal, out double forceTransverse)
        {
            GetForceDirections(body, out Vector3d radial, out Vector3d normal, out Vector3d transverse);

            forceRadial = Vector3d.Dot(forceVector, radial);
            forceNormal = Vector3d.Dot(forceVector, normal);
            forceTransverse = Vector3d.Dot(forceVector, transverse);
        }

        private void MakeVesselStationary()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;

            vessel.SetWorldVelocity(Vector3d.zero);
            vessel.SetPosition(currentPos, true);
        }

        private bool HeightValid(Vessel vessel) => vessel.heightFromTerrain < settings.maxSurfaceHeight || vessel.LandedOrSplashed;

        private bool InRailsWarp() => TimeWarp.CurrentRate != 1f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH; // mode.high is non-physics time warp

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
            double vesselMass = vessel.totalMass * 1000d; // convert from tons to kg
            CelestialBody body = vessel.mainBody;

            Vector3d toBody = radiusVec.normalized;
            double radius = radiusVec.magnitude;

            double forceMagnitude = body.gravParameter * vesselMass / (radius * radius);

            Vector3d forceVector = toBody * forceMagnitude;

            //Util.Log($"vesselMass: {vesselMass}, radius: {radius}, body.gravParameter: {body.gravParameter}, forceMagnitude: {forceMagnitude}, forceVector: {forceVector}, toBody: {toBody}");

            return forceVector;
        }

        private void MovePlanet(Vector3d forceVector, bool isFrozen, Vector3d radiusVec)
        {
            Vector3d thrustNormal = forceVector.normalized;

            alignmentToCenter = isFrozen ? Vector3d.Dot(thrustNormal, radiusVec.normalized) : 1d; // if using gravitational force, its always aligned, although in the opposite direction to thrust

            if (alignmentToCenter <= 0d)
            {
                //Util.Log($"Thrust not pointing towards planet (alignmentToCenter: {alignmentToCenter})");
                return;
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            double vesselMass = vessel.totalMass * 1000d; // convert from tons to kg
            CelestialBody body = vessel.mainBody;
            Orbit orbit = body.orbit;
            double totalMass = body.Mass + vesselMass;
            double currentUT = Planetarium.GetUniversalTime();
            const double tolerance = 1e-3d;
            const double epsilon = 1e-9d;

            // TODO: add a way to change reference body if it gets in the hill sphere, would need to use world vectors instead of local

            // TODO: need to make sure the vessel is close to the actual ground, even if underwater

            double effectiveThrust = forceVector.magnitude * alignmentToCenter;
            Vector3d forceOnPlanet = thrustNormal * effectiveThrust;
            bodyAccel = forceOnPlanet / totalMass;
            Vector3d deltaV = bodyAccel * Time.fixedDeltaTime;
            Vector3d position = orbit.getRelativePositionAtUT(currentUT); // same as (orbit.getTruePositionAtUT(currentUT) - orbit.referenceBody.getTruePositionAtUT(currentUT)).xzy;
            Vector3d velocity = orbit.getOrbitalVelocityAtUT(currentUT); // same as orbit.getOrbitalVelocityAtUT(currentUT) + orbit.referenceBody.GetFrameVelAtUT(currentUT) - orbit.referenceBody.GetFrameVelAtUT(currentUT);

            Vector3d newVelocity = velocity + deltaV;

            //Util.Log($"altitude: {orbit.altitude + body.orbit.referenceBody.Radius}, position: {position}, position.magnitude: {position.magnitude}, velocity: {velocity}, newVelocity: {newVelocity}, newVelocity.magnitude: {newVelocity.magnitude}, deltaV: {deltaV}");

            double AOP = orbit.argumentOfPeriapsis;
            double meanAnomaly = orbit.meanAnomaly;
            double eccentricity = orbit.eccentricity;

            orbit.UpdateFromStateVectors(position, newVelocity, orbit.referenceBody, currentUT); // use currentUT to set the epoch to now

            if (eccentricity < epsilon && Math.Abs(orbit.eccentricity - eccentricity) < epsilon) // AOP and meanAnomaly are not stable in circular orbits
            {
                orbit.SetOrbit(orbit.inclination, orbit.eccentricity, orbit.semiMajorAxis, orbit.LAN, AOP, meanAnomaly, orbit.epoch, orbit.referenceBody);
            }

            //Util.Log($"NEW position: {orbit.getRelativePositionAtUT(currentUT)}, NEW velocity: {orbit.getOrbitalVelocityAtUT(currentUT)}, meanAnomaly: {orbit.meanAnomaly}, argumentOfPeriapsis: {orbit.argumentOfPeriapsis}");

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

                    double newPeriod = tau / newAngularVelocity.magnitude;
                    //Util.Log($"rotationalInertia: {rotationalInertia}, bodyAngularAccel: {bodyAngularAccel}, torqueAlongAxis: {torqueAlongAxis}, Time.fixedDeltaTime: {Time.fixedDeltaTime}, body.angularVelocity: {body.angularVelocity} (mag: {body.angularVelocity.magnitude}), deltaAngularV: {deltaAngularV}, newAngularVelocity: {newAngularVelocity} (mag: {newAngularVelocity.magnitude}), body.rotationPeriod: {body.rotationPeriod}, newPeriod: {newPeriod}");
                    body.rotationPeriod = newPeriod; // angular velocity is set by rotation period in CBUpdate
                    body.initialRotation = (body.rotationAngle - 360d * (1d / newPeriod) * currentUT) % 360d; // work backwards from rotationAngle = (initialRotation + 360.0 * rotPeriodRecip * Planetarium.GetUniversalTime()) % 360.0;
                    // rotationAngle is left unchanged
                }

                body.CBUpdate(); // make sure this gets called before we do anything else
            }
        }
    }
}