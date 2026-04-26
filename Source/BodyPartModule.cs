using System;
using UnityEngine;

namespace CelestialBodyMover
{
    public class BodyPartModule : PartModule, IPartMassModifier
    {
        [KSPField(isPersistant = true)]
        public float mass;
        [KSPField(isPersistant = true)]
        public CelestialBody body;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            return mass;
        }

        public ModifierChangeWhen GetModuleMassChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }

        internal static bool ModuleExists(Vessel vessel) => vessel.rootPart.Modules.Contains<BodyPartModule>();

        internal static void AddModule(Vessel vessel, CelestialBody body) // mass in kilograms
        {
            if (vessel == null) return;
            
            if (!ModuleExists(vessel))
            {
                //Util.Log($"Adding module");
                BodyPartModule m = (BodyPartModule)vessel.rootPart.AddModule(typeof(BodyPartModule).Name);
                m.mass = (float)body.Mass / 1000f; // convert from kg to tons
                m.body = body;
                vessel.rootPart.UpdateMass();
                vessel.VesselDeltaV.SetCalcsDirty(true, true);
            }
            else
            {
                //Util.Log($"Updating module mass");
                BodyPartModule m = vessel.rootPart.Modules.GetModule<BodyPartModule>();

                if (m.body != body)
                {
                    m.mass = (float)body.Mass / 1000f; // convert from kg to tons
                    m.body = body;
                    vessel.rootPart.UpdateMass();
                    vessel.VesselDeltaV.SetCalcsDirty(true, true);
                }
            }
        }

        internal static void RemoveModule(Vessel vessel)
        {
            if (vessel == null) return;

            if (ModuleExists(vessel))
            {
                //Util.Log($"Removing module");
                BodyPartModule m = vessel.rootPart.Modules.GetModule<BodyPartModule>();
                vessel.rootPart.RemoveModule(m);
                vessel.rootPart.UpdateMass();
                vessel.VesselDeltaV.SetCalcsDirty(true, true);
            }
        }
    }
}
