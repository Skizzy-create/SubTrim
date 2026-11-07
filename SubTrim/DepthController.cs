using System;

namespace SubTrim
{
    /// <summary>Tuning for <see cref="DepthController"/>. Pure data, no KSP types, so the offline simulator can use it.</summary>
    public sealed class DepthControllerConfig
    {
        /// <summary>Commanded vertical speed per metre of depth error (1/s).</summary>
        public double DepthGain = 0.05;
        /// <summary>Largest commanded climb rate (m/s).</summary>
        public double MaxClimbRate = 2.0;
        /// <summary>Largest commanded dive rate (m/s, positive number).</summary>
        public double MaxDiveRate = 2.0;
        /// <summary>Depth band (m) around the target in which the trim may push either way and the integrator runs.
        /// Outside it the trim only ever pushes toward the target, so it never fights the AI's own pitch-driven transit.</summary>
        public double CaptureBand = 15.0;
        /// <summary>Net buoyancy (fraction of weight) per m/s of vertical-speed error.</summary>
        public double Kp = 0.012;
        /// <summary>Integral gain: net buoyancy fraction per (m/s x s) of vertical-speed error.</summary>
        public double Ki = 0.002;
        /// <summary>Limit on |net buoyancy| / weight while holding depth. Keeps the boat near neutral so pitch control keeps authority.</summary>
        public double MaxTrim = 0.02;
        /// <summary>Exponential bleed rate (1/s) of the integrator while outside the capture band. 0 = hold.</summary>
        public double IntegralLeak = 0.0;
    }

    /// <summary>
    /// Cascaded depth controller. Outer loop: depth error -> commanded vertical speed (proportional, rate-limited).
    /// Inner loop: vertical-speed error -> net buoyancy as a fraction of weight (PI).
    ///
    /// Anti-windup:
    ///  * the integrator only runs inside the capture band (so it learns the steady hydrodynamic/trim offset, not the transit),
    ///  * it does not integrate further in a direction in which the actuators are saturated (conditional integration),
    ///  * it is clamped to +/-MaxTrim.
    /// Output sign convention: positive = more buoyant (rise).
    /// </summary>
    public sealed class DepthController
    {
        public double Integral;
        public double CommandedSpeed;
        public double Output;
        public bool InBand;

        public void Reset()
        {
            Integral = 0;
            CommandedSpeed = 0;
            Output = 0;
            InBand = false;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <param name="altitude">Current altitude (m, negative below sea level).</param>
        /// <param name="targetAltitude">Target altitude (m, negative below sea level).</param>
        /// <param name="verticalSpeed">Current vertical speed (m/s, positive up).</param>
        /// <param name="dt">Seconds since the previous step.</param>
        /// <param name="saturatedUp">The actuators cannot add more buoyancy.</param>
        /// <param name="saturatedDown">The actuators cannot remove more buoyancy.</param>
        /// <returns>Desired net buoyancy as a fraction of weight.</returns>
        public double Step(DepthControllerConfig c, double altitude, double targetAltitude, double verticalSpeed, double dt, bool saturatedUp, bool saturatedDown)
        {
            double depthError = targetAltitude - altitude; // > 0: target is above us
            CommandedSpeed = Clamp(c.DepthGain * depthError, -c.MaxDiveRate, c.MaxClimbRate);
            double speedError = CommandedSpeed - verticalSpeed;
            InBand = Math.Abs(depthError) <= c.CaptureBand;

            if (InBand)
            {
                bool wantsUp = speedError > 0;
                if (!(wantsUp && saturatedUp) && !(!wantsUp && saturatedDown))
                    Integral += c.Ki * speedError * dt;
                Integral = Clamp(Integral, -c.MaxTrim, c.MaxTrim);
            }
            else if (c.IntegralLeak > 0)
            {
                Integral *= Math.Exp(-c.IntegralLeak * dt);
            }

            double u = c.Kp * speedError + Integral;
            if (InBand)
                u = Clamp(u, -c.MaxTrim, c.MaxTrim);
            else if (depthError > 0)
                u = Clamp(u, 0, c.MaxTrim);   // must go up: never add weight
            else
                u = Clamp(u, -c.MaxTrim, 0);  // must go down: never add lift

            Output = u;
            return u;
        }
    }
}
