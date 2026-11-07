using System;

namespace SubTrim
{
    /// <summary>
    /// (a) KerbalPowers Naval, Cavitation.ModuleShipBallast (Cavitation.dll). The module varies part.buoyancy between
    /// minBuoyancy and maxBuoyancy, pumping at variantPumpRate % of maxBuoyancy per second and drawing ECRequirement EC/s
    /// while draining.
    ///
    /// Default drive: switch the module to its "target flooding" mode (useTargetDepth = false, pumpActive = true) and set
    /// targetFill to the fill that gives the buoyancy SubTrim wants. KP then pumps at its own rate and charges its own EC.
    /// With kpNativeDepthHold = true SubTrim instead hands KP the target depth (useTargetDepth = true, targetDepth) and lets
    /// KP's own depth logic run.
    /// Emergency blow without EC: part.buoyancy and the module's private partBuoyancy are raised directly (if that field exists).
    /// The player's original pump settings are restored on release.
    /// </summary>
    public sealed class KPBallastActuator : BallastActuator
    {
        static bool initialised;
        public static Type ModuleType { get; private set; }
        static Func<object, float> getMinB, getMaxB, getPumpRate, getMaxDepth, getTargetFill, getTargetDepth;
        static Func<object, bool> getUseTargetDepth, getPumpActive;
        static Action<object, bool> setUseTargetDepth, setPumpActive;
        static Action<object, float> setTargetFill, setTargetDepth, setPartBuoyancy;

        public static bool Init()
        {
            if (initialised) return ModuleType != null;
            initialised = true;
            Type t = FastAccess.FindType("Cavitation.ModuleShipBallast");
            if (t == null) return false;
            getMinB = FastAccess.Getter<float>(t, "minBuoyancy");
            getMaxB = FastAccess.Getter<float>(t, "maxBuoyancy");
            getPumpRate = FastAccess.Getter<float>(t, "variantPumpRate");
            getMaxDepth = FastAccess.Getter<float>(t, "maxDepth");
            getTargetFill = FastAccess.Getter<float>(t, "targetFill");
            getTargetDepth = FastAccess.Getter<float>(t, "targetDepth");
            getUseTargetDepth = FastAccess.Getter<bool>(t, "useTargetDepth");
            getPumpActive = FastAccess.Getter<bool>(t, "pumpActive");
            setUseTargetDepth = FastAccess.Setter<bool>(t, "useTargetDepth");
            setPumpActive = FastAccess.Setter<bool>(t, "pumpActive");
            setTargetFill = FastAccess.Setter<float>(t, "targetFill");
            setTargetDepth = FastAccess.Setter<float>(t, "targetDepth");
            setPartBuoyancy = FastAccess.Setter<float>(t, "partBuoyancy"); // private; optional (emergency blow only)
            if (getMinB == null || getMaxB == null || setUseTargetDepth == null || setPumpActive == null || setTargetFill == null)
            {
                UnityEngine.Debug.LogWarning("[SubTrim] KerbalPowers ModuleShipBallast found but its fields differ from 1.x; not using it");
                return false;
            }
            ModuleType = t;
            UnityEngine.Debug.Log("[SubTrim] KerbalPowers Naval ModuleShipBallast detected" + (setPartBuoyancy == null ? " (no emergency override)" : ""));
            return true;
        }

        readonly PartModule module;
        readonly bool native;
        double k;
        float minB, maxB;
        bool taken;
        bool origUseTargetDepth, origPumpActive;
        float origTargetFill, origTargetDepth;

        public KPBallastActuator(PartModule module, bool nativeDepthHold)
        {
            this.module = module;
            part = module.part;
            native = nativeDepthHold;
        }

        public override string Kind => "KP";
        public override int Priority => 0;

        public override bool Refresh(double rhoS, double vesselMass)
        {
            if (!Alive(module) || !Alive(part) || part.partBuoyancy == null) return false;
            k = part.partBuoyancy.displacement * rhoS;
            minB = getMinB(module);
            maxB = getMaxB(module);
            if (maxB < minB) { float t = maxB; maxB = minB; minB = t; }
            LiftNow = k * part.buoyancy;
            LiftMin = k * minB;
            LiftMax = k * maxB;
            return true;
        }

        public override void TakeControl()
        {
            if (taken || !Alive(module)) return;
            taken = true;
            origUseTargetDepth = getUseTargetDepth != null && getUseTargetDepth(module);
            origPumpActive = getPumpActive != null && getPumpActive(module);
            origTargetFill = getTargetFill != null ? getTargetFill(module) : 0f;
            origTargetDepth = getTargetDepth != null ? getTargetDepth(module) : 0f;
        }

        public override void Release()
        {
            if (!taken) return;
            taken = false;
            if (!Alive(module)) return;
            setUseTargetDepth(module, origUseTargetDepth);
            setPumpActive(module, origPumpActive);
            setTargetFill(module, origTargetFill);
            if (setTargetDepth != null) setTargetDepth(module, origTargetDepth);
        }

        public override void Drive(ref DriveContext ctx)
        {
            if (native && setTargetDepth != null && !ctx.Emergency)
            {
                float maxDepth = getMaxDepth != null ? getMaxDepth(module) : 1000f;
                float depth = (float)Clamp(-ctx.TargetAltitude, 0, maxDepth > 0 ? maxDepth : 1000);
                setUseTargetDepth(module, true);
                setTargetDepth(module, depth);
                setPumpActive(module, true);
                return;
            }

            double b = k > 1e-9 ? Target / k : maxB;
            b = Clamp(b, minB, maxB);
            float fill = maxB > 1e-6f ? (float)((maxB - b) / maxB * 100.0) : 0f;
            setUseTargetDepth(module, false);
            setPumpActive(module, true);
            setTargetFill(module, fill < 0f ? 0f : (fill > 100f ? 100f : fill));

            // Emergency: blow with "compressed air" (no EC), on top of KP's own (EC-limited) pumping.
            if (ctx.Emergency && setPartBuoyancy != null && part.buoyancy < maxB)
            {
                float rate = getPumpRate != null ? getPumpRate(module) : 1f;
                if (rate <= 0f) rate = 1f;
                double step = rate / 100.0 * maxB * ctx.RateMultiplier * ctx.Dt;
                float nb = (float)Math.Min(maxB, part.buoyancy + step);
                part.buoyancy = nb;
                setPartBuoyancy(module, nb);
            }
        }
    }
}
