using KSP.UI.Screens;
using System;
using ToolbarControl_NS;
using UnityEngine;
using ClickThroughFix;

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

    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class CelestialBodyMover : MonoBehaviour
    {
        ToolbarControl toolbarControl = null;

        GUISkin skin;

        bool isWindowOpen = false;

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
        }

        void Start()
        {
            InitToolbar();
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
            GUI.DragWindow();
        }
    }
}
