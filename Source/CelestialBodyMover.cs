using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using ToolbarControl_NS;
using UnityEngine;
using ClickThroughFix;
using System.Linq;

namespace CelestialBodyMover
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)] // startup on main menu according to https://github.com/linuxgurugamer/ToolbarControl/wiki/Registration
    public class RegisterToolbar : MonoBehaviour
    {
        void Start()
        {
            ToolbarControl.RegisterMod("CBM", "CelestialBodyMover");
        }
    }

    [KSPAddon(KSPAddon.Startup.FlightAndKSC, false)]
    public class CelestialBodyMover : MonoBehaviour
    {
        ToolbarControl toolbarControl = null;

        GUISkin skin;

        bool isWindowOpen = false;
        bool isActive = true;

        Rect mainRect = new Rect(100, 100, -1, -1);

        private void InitToolbar()
        {
            if (toolbarControl == null)
            {
                toolbarControl = gameObject.AddComponent<ToolbarControl>();
                toolbarControl.AddToAllToolbars(ToggleWindow, ToggleWindow,
                    ApplicationLauncher.AppScenes.FLIGHT & ApplicationLauncher.AppScenes.SPACECENTER,
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
        }

        void Start()
        {
            InitToolbar();
        }

        void OnDestroy()
        {
            Destroy(toolbarControl);
            toolbarControl = null;
        }

        private void ToggleWindow() => isWindowOpen = !isWindowOpen;

        void OnGUI()
        {
            if (isWindowOpen)
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
        }

        private void ClampToScreen(ref Rect rect)
        {
            float left = Mathf.Clamp(rect.x, 0, Screen.width - rect.width);
            float top = Mathf.Clamp(rect.y, 0, Screen.height - rect.height);
            rect = new Rect(left, top, rect.width, rect.height);
        }

        static void Log(string message, string prefix = "[CelestialBodyMover]")
        {
            Debug.Log($"{prefix}: {message}");
        }

        private void MakeMainWindow(int id)
        {
            if (GUILayout.Button(isActive ? "Deactivate" : "Activate"))
            {
                isActive = !isActive;
                Log(isActive ? "Activated" : "Deactivated");
            }
            GUI.DragWindow();
        }

        private void GetThrust()
        {
            if (!isActive) return;

            Vessel vessel = FlightGlobals.ActiveVessel;
            List<Part> parts = vessel.parts.Where(p => p.State == PartStates.ACTIVE).ToList();

            // thrust code taken from https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/VesselState.cs
            Vector3d thrustCurrent = Vector3d.zero;
            Vector3d vesselForward = vessel.GetTransform().up;

            for (int i1 = 0; i1 < parts.Count; i1++)
            {
                Part part = parts[i1];
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

                            thrustCurrent += tCurrentThrust * cosineLosses * thrustDirectionVector;
                        }
                    }
                }
            }

            double thrustCurrentMag = Vector3d.Dot(thrustCurrent, vesselForward);

            Log($"thrustCurrentMag: {thrustCurrentMag}, thrustCurrent: {thrustCurrent}, vesselForward: {vesselForward}");
        }
    }
}
