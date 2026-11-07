using System;

namespace SubTrim
{
    /// <summary>
    /// Soft link to HullBreach 1.8+ (HullBreach.HullBreachVesselModule). Read-only; nothing breaks if HullBreach is
    /// absent or has a different API (the link then reports "not available").
    /// </summary>
    public static class HullBreachLink
    {
        static bool initialised;
        static Type moduleType;
        static Func<object, int> criticalCount;
        static Func<object, int> leakingCount;
        static Func<object, double> waterTonnes;
        static Func<object, double> inflowTonnesPerSec;
        static Func<object, bool> foundered;
        static Func<object, bool> isSubmarine;

        public static bool Available
        {
            get
            {
                Init();
                return moduleType != null;
            }
        }

        static void Init()
        {
            if (initialised) return;
            initialised = true;
            Type t = FastAccess.FindType("HullBreach.HullBreachVesselModule");
            if (t == null) return;
            criticalCount = FastAccess.Getter<int>(t, "CriticalCount");
            leakingCount = FastAccess.Getter<int>(t, "LeakingCount");
            waterTonnes = FastAccess.Getter<double>(t, "WaterTonnes");
            inflowTonnesPerSec = FastAccess.Getter<double>(t, "InflowTonnesPerSec");
            foundered = FastAccess.Getter<bool>(t, "Foundered");
            isSubmarine = FastAccess.Getter<bool>(t, "IsSubmarine");
            if (criticalCount == null || waterTonnes == null)
            {
                UnityEngine.Debug.Log("[SubTrim] HullBreach found but its vessel-module API is unknown (need 1.8+); flood emergency detection is off");
                return;
            }
            moduleType = t;
            UnityEngine.Debug.Log("[SubTrim] HullBreach 1.8+ detected: flood emergencies will surface submarines");
        }

        /// <summary>Find the HullBreach vessel module on this vessel, or null. Call on rebind only.</summary>
        public static VesselModule Find(Vessel v)
        {
            Init();
            if (moduleType == null || v == null || v.vesselModules == null) return null;
            for (int i = 0; i < v.vesselModules.Count; i++)
            {
                VesselModule m = v.vesselModules[i];
                if (m != null && m.GetType() == moduleType) return m;
            }
            return null;
        }

        public struct State
        {
            public int Critical;
            public int Leaking;
            public double Water;
            public double Inflow;
            public bool Foundered;
            /// <summary>HullBreach's own submarine detection (a submarine = true part or a BDA Submarine AI).</summary>
            public bool IsSubmarine;
        }

        public static bool Read(VesselModule module, out State s)
        {
            s = default;
            if (module == null || moduleType == null) return false;
            s.Critical = criticalCount(module);
            s.Leaking = leakingCount != null ? leakingCount(module) : 0;
            s.Water = waterTonnes(module);
            s.Inflow = inflowTonnesPerSec != null ? inflowTonnesPerSec(module) : 0;
            s.Foundered = foundered != null && foundered(module);
            s.IsSubmarine = isSubmarine != null && isSubmarine(module);
            return true;
        }
    }
}
