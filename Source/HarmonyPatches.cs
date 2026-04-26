//using HarmonyLib;
//using System.Collections.Generic;
//using System.Reflection;
//using UnityEngine;

//namespace CelestialBodyMover
//{
//    [KSPAddon(KSPAddon.Startup.Instantly, true)]
//    public class HarmonyPatcher : MonoBehaviour
//    {
//        void Start()
//        {
//            var harmony = new Harmony("CelestialBodyMover.HarmonyPatcher");
//            harmony.PatchAll();
//        }
//    }

//    [HarmonyPatch(typeof(VesselDeltaV))]
//    public static class VesselDeltaVPatches
//    {
//        internal static readonly FieldInfo _partInfoField = AccessTools.Field(typeof(VesselDeltaV), "_partInfo");

//        [HarmonyPatch("ResetPartInfo")]
//        //[HarmonyPrefix]
//        [HarmonyPostfix]
//        //public static bool Prefix_SimulateLastStage(ref VesselDeltaV __instance)
//        public static void Postfix_ResetPartInfo(ref VesselDeltaV __instance)
//        {
//            if (FlightGlobals.ActiveVessel == null) return;
            
//            Util.Log($"Postfix_ResetPartInfo running");
            
//            if (_partInfoField == null)
//            {
//                Util.LogWarning("_partInfoField is null");
//                //return true;
//                return;
//            }

//            List<DeltaVPartInfo> _partInfo = (List<DeltaVPartInfo>)_partInfoField.GetValue(__instance);

//            if (_partInfo == null)
//            {
//                Util.LogWarning("_partInfo returned null List<DeltaVPartInfo>!");
//                //return true;
//                return;
//            }

//            for (int i = 0; i < _partInfo.Count; i++)
//            {
//                Util.Log($"part: {_partInfo[i].part.name}, id: {_partInfo[i].part.persistentId}, rootpart id: {_partInfo[i].part.vessel.rootPart.persistentId}, rootPart name: {_partInfo[i].part.vessel.rootPart.name},  dryMass: {_partInfo[i].dryMass}, fuelMass: {_partInfo[i].fuelMass}, jettisonMass: {_partInfo[i].jettisonMass}, activationStage: {_partInfo[i].activationStage}, decoupleStage: {_partInfo[i].decoupleStage}");
//            }

//            //Util.Log($"");
//            //return true;
//            return;
//        }
//    }
//}
