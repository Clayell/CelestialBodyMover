using KSP.UI.Screens;
using System;
using ToolbarControl_NS;
using UnityEngine;

namespace CrashMyKSP
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)] // startup on main menu according to https://github.com/linuxgurugamer/ToolbarControl/wiki/Registration
    public class RegisterToolbar : MonoBehaviour
    {
        void Start()
        {
            ToolbarControl.RegisterMod("CrashMyKSP", "CrashMyKSP");
        }
    }

    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class CrashMyKSP : MonoBehaviour
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
                    "CrashMyKSP",
                    "CrashMyKSP_Button",
                    "CrashMyKSP/PluginData/ToolbarIcons/button-64",
                    "CrashMyKSP/PluginData/ToolbarIcons/button-24",
                    "CrashMyKSP"
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

                mainRect = GUILayout.Window(id0, mainRect, MakeMainWindow, "CrashMyKSP", GUILayout.Width(200));
            }
        }

        static void Log(string message, string prefix = "[CrashMyKSP]")
        {
            Debug.Log($"{prefix}: {message}");
        }

        private void MakeMainWindow(int id)
        {
            if (GUILayout.Button("Crash KSP!"))
            {
                Log($"Crashing KSP.");
                Application.Quit();
            }
            GUI.DragWindow();
        }
    }
}
