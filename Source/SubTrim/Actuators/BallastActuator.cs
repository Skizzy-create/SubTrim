namespace SubTrim
{
    /// <summary>Per-tick inputs for <see cref="BallastActuator.Drive"/>.</summary>
    public struct DriveContext
    {
        /// <summary>Seconds since the last tick.</summary>
        public double Dt;
        /// <summary>Rate multiplier (emergency blows run faster).</summary>
        public double RateMultiplier;
        /// <summary>Emergency blow: move toward maximum lift without needing EC (compressed air).</summary>
        public bool Emergency;
        /// <summary>The altitude SubTrim is steering to (for actuators with their own depth mode).</summary>
        public double TargetAltitude;
        /// <summary>Vessel mass in tonnes.</summary>
        public double VesselMass;
        /// <summary>Provides EC for SubTrim-driven pumps.</summary>
        public SubTrimVesselModule Host;
    }

    /// <summary>
    /// One ballast lever. All quantities are "lift" in tonnes (buoyant force / g minus the actuator's own variable mass,
    /// for the fully submerged case), so different mechanisms can share the same allocation:
    /// the controller asks for a change in net lift and the allocator hands it out in priority order.
    /// </summary>
    public abstract class BallastActuator
    {
        public Part part;
        /// <summary>Current lift contribution (t). Refreshed each tick.</summary>
        public double LiftNow;
        /// <summary>Lift with the ballast fully flooded (t).</summary>
        public double LiftMin;
        /// <summary>Lift with the ballast fully blown (t).</summary>
        public double LiftMax;
        /// <summary>Lift the allocator wants (t).</summary>
        public double Target;

        /// <summary>Short tag for the PAW.</summary>
        public abstract string Kind { get; }

        /// <summary>Lower runs first: (a) KP Naval 0, (b) BuoyancyAdjuster 1, (c) WBI 2, (d) resource 3, (e) virtual 4.</summary>
        public abstract int Priority { get; }

        /// <summary>Update LiftNow/LiftMin/LiftMax. rhoS = ocean density x PhysicsGlobals.BuoyancyScalar.
        /// Returns false if the actuator is no longer usable (part gone, module removed).</summary>
        public abstract bool Refresh(double rhoS, double vesselMass);

        /// <summary>Move toward Target, respecting the mechanism's rate and EC rules. Must not allocate.</summary>
        public abstract void Drive(ref DriveContext ctx);

        /// <summary>Called when SubTrim starts driving this actuator. Store anything Release must restore.</summary>
        public virtual void TakeControl() { }

        /// <summary>Called when SubTrim stops driving this actuator.</summary>
        public virtual void Release() { }

        /// <summary>0 = fully blown, 1 = fully flooded.</summary>
        public double FloodedFraction
        {
            get
            {
                double span = LiftMax - LiftMin;
                if (span <= 1e-9) return 0;
                double f = (LiftMax - LiftNow) / span;
                return f < 0 ? 0 : (f > 1 ? 1 : f);
            }
        }

        protected static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Step <paramref name="current"/> toward <paramref name="target"/> by at most up/down per call.</summary>
        protected static double Approach(double current, double target, double maxUp, double maxDown)
        {
            if (target > current) return current + (target - current < maxUp ? target - current : maxUp);
            if (target < current) return current - (current - target < maxDown ? current - target : maxDown);
            return current;
        }

        protected static bool Alive(UnityEngine.Object o) => o != null;
    }
}
