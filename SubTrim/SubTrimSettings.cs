using System;
using System.Collections.Generic;
using UnityEngine;

namespace SubTrim
{
    /// <summary>Global settings, read once from the SUBTRIM_SETTINGS node (GameData/SubTrim/SubTrim.cfg).</summary>
    public sealed class SubTrimSettings
    {
        static SubTrimSettings instance;

        public static SubTrimSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new SubTrimSettings();
                    instance.Load();
                }
                return instance;
            }
        }

        public bool enabled = true;
        public float tickInterval = 0.1f;
        public bool debug = false;

        public readonly DepthControllerConfig controller = new DepthControllerConfig();

        /// <summary>AI target altitudes above this (m) mean "surface" (BDA uses +1 to surface for deck guns).</summary>
        public float surfaceAltitude = -3f;
        /// <summary>Reserve buoyancy (fraction of weight) to hold while surfaced.</summary>
        public float surfaceReserve = 0.15f;
        /// <summary>Rate multiplier for emergency blows (no EC needed).</summary>
        public float emergencyRateMultiplier = 5f;

        /// <summary>Never hold deeper than this fraction of the shallowest part crush depth (from part.maxPressure).</summary>
        public float crushSafetyFactor = 0.85f;
        /// <summary>Minimum clearance above the sea floor (m).</summary>
        public float keelClearance = 15f;

        /// <summary>Surface when vessel EC falls below this fraction, resume above resumeEcFraction.</summary>
        public float lowEcFraction = 0.02f;
        public float resumeEcFraction = 0.10f;
        /// <summary>EC per tonne of lift added by SubTrim-driven pumps (resource and virtual ballast).</summary>
        public float ecPerTonne = 0.5f;

        /// <summary>Resource ballast pump rate, fraction of tank capacity per second.</summary>
        public float resourceRateFraction = 0.01f;
        public readonly List<string> ballastResources = new List<string>();

        public bool virtualBallast = true;
        public float virtualMinScale = 0.2f;
        public float virtualMaxScale = 1.5f;
        /// <summary>Virtual ballast rate, fraction of the parts' stock buoyancy per second.</summary>
        public float virtualScaleRate = 0.01f;

        /// <summary>KerbalPowers Naval: hand the target depth to ModuleShipBallast's own depth mode instead of driving its fill.</summary>
        public bool kpNativeDepthHold = false;

        /// <summary>BuoyancyAdjuster level change rate (per second, 1.0 = 100 %).</summary>
        public float buoyancyAdjusterRate = 0.01f;

        /// <summary>HullBreach: surface when any compartment is critical, or when flood water exceeds this fraction of vessel mass.</summary>
        public bool hullBreachSurfaceOnCritical = true;
        public float hullBreachWaterFraction = 0.05f;

        void Load()
        {
            ballastResources.Add("BallastWater");
            ConfigNode node = null;
            try
            {
                if (GameDatabase.Instance != null)
                {
                    ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("SUBTRIM_SETTINGS");
                    if (nodes != null && nodes.Length > 0) node = nodes[nodes.Length - 1];
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SubTrim] Could not read SUBTRIM_SETTINGS: " + e.Message);
            }
            if (node == null)
            {
                Debug.Log("[SubTrim] No SUBTRIM_SETTINGS node found, using defaults");
                return;
            }

            node.TryGetValue("enabled", ref enabled);
            node.TryGetValue("tickInterval", ref tickInterval);
            node.TryGetValue("debug", ref debug);
            Read(node, "depthGain", ref controller.DepthGain);
            Read(node, "maxClimbRate", ref controller.MaxClimbRate);
            Read(node, "maxDiveRate", ref controller.MaxDiveRate);
            Read(node, "captureBand", ref controller.CaptureBand);
            Read(node, "kp", ref controller.Kp);
            Read(node, "ki", ref controller.Ki);
            Read(node, "maxTrim", ref controller.MaxTrim);
            Read(node, "integralLeak", ref controller.IntegralLeak);
            node.TryGetValue("surfaceAltitude", ref surfaceAltitude);
            node.TryGetValue("surfaceReserve", ref surfaceReserve);
            node.TryGetValue("emergencyRateMultiplier", ref emergencyRateMultiplier);
            node.TryGetValue("crushSafetyFactor", ref crushSafetyFactor);
            node.TryGetValue("keelClearance", ref keelClearance);
            node.TryGetValue("lowEcFraction", ref lowEcFraction);
            node.TryGetValue("resumeEcFraction", ref resumeEcFraction);
            node.TryGetValue("ecPerTonne", ref ecPerTonne);
            node.TryGetValue("resourceRateFraction", ref resourceRateFraction);
            node.TryGetValue("virtualBallast", ref virtualBallast);
            node.TryGetValue("virtualMinScale", ref virtualMinScale);
            node.TryGetValue("virtualMaxScale", ref virtualMaxScale);
            node.TryGetValue("virtualScaleRate", ref virtualScaleRate);
            node.TryGetValue("kpNativeDepthHold", ref kpNativeDepthHold);
            node.TryGetValue("buoyancyAdjusterRate", ref buoyancyAdjusterRate);
            node.TryGetValue("hullBreachSurfaceOnCritical", ref hullBreachSurfaceOnCritical);
            node.TryGetValue("hullBreachWaterFraction", ref hullBreachWaterFraction);

            ConfigNode[] res = node.GetNodes("BALLAST_RESOURCE");
            if (res.Length > 0)
            {
                ballastResources.Clear();
                for (int i = 0; i < res.Length; i++)
                {
                    string n = res[i].GetValue("name");
                    if (!string.IsNullOrEmpty(n) && !ballastResources.Contains(n)) ballastResources.Add(n);
                }
            }

            // Sanity limits.
            tickInterval = Mathf.Clamp(tickInterval, 0.02f, 1f);
            controller.MaxTrim = Math.Max(0.001, controller.MaxTrim);
            controller.CaptureBand = Math.Max(1.0, controller.CaptureBand);
            virtualMinScale = Mathf.Clamp(virtualMinScale, 0f, 1f);
            virtualMaxScale = Mathf.Max(1f, virtualMaxScale);
            Debug.Log("[SubTrim] Settings loaded: tick " + tickInterval + " s, maxTrim " + controller.MaxTrim + ", ballast resources " + string.Join(",", ballastResources.ToArray()));
        }

        static void Read(ConfigNode node, string key, ref double value)
        {
            string s = node.GetValue(key);
            if (s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) value = v;
        }
    }
}
