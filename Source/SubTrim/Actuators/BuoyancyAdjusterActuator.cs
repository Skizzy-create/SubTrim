using System;

namespace SubTrim
{
    /// <summary>
    /// (b) BuoyancyAdjuster (global KSPAddon "BuoyancyAdjuster"). When the player has switched it on for a vessel, it stores
    /// the state on the root part as hidden resources (BuoyancyAdjusterState >= 0.5, BuoyancyLevel 0..2), zeroes stock
    /// part.buoyancy and pushes every submerged part up with weight x submergedPortion x level. Fully submerged, the
    /// vessel's buoyancy/weight ratio is therefore exactly BuoyancyLevel, and 1.0 is neutral.
    ///
    /// SubTrim only uses it if the player already enabled it for the vessel (it replaces stock buoyancy, so it is exclusive
    /// with the other actuators). Drive: write BuoyancyLevel on the root part (read by BuoyancyAdjuster for non-focused
    /// vessels) and, for the vessel BuoyancyAdjuster is tracking, its private buoyancyLevel field (it reads that one instead).
    /// </summary>
    public sealed class BuoyancyAdjusterActuator : BallastActuator
    {
        public const string StateResource = "BuoyancyAdjusterState";
        public const string LevelResource = "BuoyancyLevel";

        static bool initialised;
        static Type addonType;
        static Func<object, Vessel> getTrackedVessel;
        static Func<object, float> getLevel;
        static Action<object, float> setLevel;

        static void Init()
        {
            if (initialised) return;
            initialised = true;
            addonType = FastAccess.FindType("BuoyancyAdjuster");
            if (addonType == null) return;
            getTrackedVessel = FastAccess.Getter<Vessel>(addonType, "trackedVessel");
            getLevel = FastAccess.Getter<float>(addonType, "buoyancyLevel");
            setLevel = FastAccess.Setter<float>(addonType, "buoyancyLevel");
            UnityEngine.Debug.Log("[SubTrim] BuoyancyAdjuster detected" + (setLevel == null ? " (cannot reach its focused-vessel level)" : ""));
        }

        /// <summary>True if BuoyancyAdjuster is installed and switched on for this vessel. No allocation.</summary>
        public static bool EnabledFor(Vessel v)
        {
            Init();
            if (addonType == null || v == null || v.rootPart == null) return false;
            PartResource r = v.rootPart.Resources.Get(StateResource);
            return r != null && r.amount >= 0.5;
        }

        readonly Vessel vessel;
        PartResource level;
        // Per instance, not static: BuoyancyAdjuster is a per-flight-scene addon, and a static reference would keep the
        // destroyed instance of an earlier scene (and the vessel it was tracking) alive.
        readonly UnityEngine.Object addon;

        public BuoyancyAdjusterActuator(Vessel v)
        {
            vessel = v;
            part = v.rootPart;
            addon = UnityEngine.Object.FindObjectOfType(addonType);
        }

        public override string Kind => "BA";
        public override int Priority => 1;

        bool Tracked => Alive(addon) && getTrackedVessel != null && setLevel != null && getTrackedVessel(addon) == vessel;

        public double Level
        {
            get
            {
                if (Tracked && getLevel != null) return getLevel(addon);
                return level != null ? level.amount : 1.0;
            }
        }

        public override bool Refresh(double rhoS, double vesselMass)
        {
            if (!Alive(part) || part.vessel != vessel) return false;
            if (level == null || level.part != part) level = part.Resources.Get(LevelResource);
            if (level == null) return false;
            LiftNow = Level * vesselMass;
            LiftMin = 0;
            LiftMax = 2.0 * vesselMass;
            return true;
        }

        public override void Drive(ref DriveContext ctx)
        {
            if (level == null || ctx.VesselMass <= 0) return;
            double want = Clamp(Target / ctx.VesselMass, 0, 2);
            double step = SubTrimSettings.Instance.buoyancyAdjusterRate * ctx.RateMultiplier * ctx.Dt;
            double now = Approach(Level, want, step, step);
            level.amount = now;
            if (Tracked) setLevel(addon, (float)now);
        }
    }
}
