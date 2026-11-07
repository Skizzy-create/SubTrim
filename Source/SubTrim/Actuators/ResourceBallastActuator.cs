namespace SubTrim
{
    /// <summary>
    /// (d) Plain ballast-water tanks: a part resource listed in BALLAST_RESOURCE (default BallastWater, 1 t/unit, as on MNWS
    /// SSBN002 and 075A). Lift = -density x amount. Flooding (only while the part is in the water) is free; pumping out
    /// costs ecPerTonne EC per tonne, except in an emergency blow. Rate: resourceRateFraction of capacity per second.
    /// Only the amount is changed; the pack's own pump toggles (e.g. MNWS FastDive/FastSurface) still work if the player
    /// uses them, but SubTrim will correct what they do while it is active.
    /// </summary>
    public sealed class ResourceBallastActuator : BallastActuator
    {
        readonly PartResource res;
        double density;

        public ResourceBallastActuator(Part p, PartResource r)
        {
            part = p;
            res = r;
        }

        public override string Kind => "Res";
        public override int Priority => 3;
        public string ResourceName => res.resourceName;

        public override bool Refresh(double rhoS, double vesselMass)
        {
            if (!Alive(part) || res == null || res.part != part || res.maxAmount <= 0) return false;
            density = res.info != null ? res.info.density : 0;
            if (density <= 0) return false;
            LiftNow = -density * res.amount;
            LiftMin = -density * res.maxAmount;
            LiftMax = 0;
            return true;
        }

        public override void Drive(ref DriveContext ctx)
        {
            double want = Clamp(-Target / density, 0, res.maxAmount);
            double rate = SubTrimSettings.Instance.resourceRateFraction * res.maxAmount * ctx.Dt;
            double amount = res.amount;
            if (want > amount)
            {
                if (ctx.Emergency || part.submergedPortion <= 0) return;
                res.amount = Approach(amount, want, rate, 0);
                return;
            }
            double remove = amount - want;
            double maxRemove = rate * ctx.RateMultiplier;
            if (remove > maxRemove) remove = maxRemove;
            if (remove <= 0) return;
            if (!ctx.Emergency) remove = ctx.Host.PayForLift(remove * density) / density;
            res.amount = amount - remove;
        }
    }
}
