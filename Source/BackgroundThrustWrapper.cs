//using System;
//using System.Reflection;

//namespace CelestialBodyMover
//{
//    internal static class BackgroundThrustWrapper
//    {
//        internal static Type backgroundThrustVessel;
//        static MethodInfo method;
//        static PropertyInfo thrustProperty;

//        internal static bool Init()
//        {
//            foreach (AssemblyLoader.LoadedAssembly assembly in AssemblyLoader.loadedAssemblies)
//            {
//                try
//                {
//                    if (assembly.name.Equals("BackgroundThrust", StringComparison.OrdinalIgnoreCase))
//                    {
//                        backgroundThrustVessel = assembly.assembly.GetType("BackgroundThrust.BackgroundThrustVessel");
//                        Util.Log($"BackgroundThrust found.");

//                        method = typeof(Vessel).GetMethod(nameof(Vessel.FindVesselModuleImplementing))?.MakeGenericMethod(backgroundThrustVessel);

//                        thrustProperty = backgroundThrustVessel.GetProperty("Thrust");

//                        return true;
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Util.LogError($"Error loading BackgroundThrust: {ex}");
//                }
//            }

//            Util.Log($"BackgroundThrust not found");

//            return false;
//        }

//        internal static Vector3d GetBackgroundThrust(Vessel vessel)
//        {
//            if (backgroundThrustVessel == null || method == null || thrustProperty == null) return Vector3d.zero;

//            object bVessel = method.Invoke(vessel, null);

//            if (bVessel == null) return Vector3d.zero;

//            object thrustVectorObj = thrustProperty.GetValue(bVessel);

//            if (thrustVectorObj is Vector3d thrustVector)
//            {
//                return thrustVector;
//            }
//            else return Vector3d.zero;
//        }
//    }
//}
