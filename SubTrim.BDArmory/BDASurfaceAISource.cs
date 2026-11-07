using System;
using System.Collections.Generic;
using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.Weapons;
using UnityEngine;

namespace SubTrim.BDA
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SubTrimBDARegistrar : MonoBehaviour
    {
        void Awake()
        {
            SubTrimRegistry.Register(new BDASurfaceAISourceFactory());
            Destroy(this);
        }
    }

    public sealed class BDASurfaceAISourceFactory : IDepthCommandSourceFactory
    {
        public IDepthCommandSource Create() => new BDASurfaceAISource();
    }

    /// <summary>
    /// Reads BDModuleSurfaceAI (BDArmory Plus 1.13.x). The AI keeps its depth target in a local variable inside
    /// PilotControl (there is no field for it), so <see cref="CommandedAltitude"/> repeats the AI's selection logic from
    /// BDModuleSurfaceAI.PilotControl, 1.13.1:
    ///   default CombatAltitude; Missile selected with a target: -10 (periscope depth);
    ///   Gun: with bullet water drag, splashed target (or flying/landed with a turret) -> -10 until in engage range, then +1 (surface);
    ///        without water drag, landed target with a turret -> -10/+1; fixed gun vs a splashed target -> the target's altitude
    ///        unless the AI is extending (private doExtend, read via an emitted accessor);
    ///   Rocket/DefenseLaser: landed or flying-with-turret targets -> -10/+1; splashed target -> target altitude unless extending.
    /// It only reads AI state; the AI itself still flies the boat (pitch, yaw, speed).
    /// </summary>
    public sealed class BDASurfaceAISource : IDepthCommandSource
    {
        const string SubmarineType = "Submarine";
        const float PeriscopeDepth = -10f;
        const float Surface = 1f;

        static bool initialised;
        static Func<object, bool> getDoExtend;

        readonly List<BDModuleSurfaceAI> ais = new List<BDModuleSurfaceAI>(1);
        Vessel vessel;

        public string Name => "BDA";

        public bool Bind(Vessel v)
        {
            if (!initialised)
            {
                initialised = true;
                getDoExtend = FastAccess.Getter<bool>(typeof(BDModuleSurfaceAI), "doExtend");
                if (getDoExtend == null) Debug.LogWarning("[SubTrim] BDModuleSurfaceAI.doExtend not found; assuming the AI never extends");
            }
            vessel = v;
            ais.Clear();
            if (v == null || v.parts == null) return false;
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                if (p == null) continue;
                for (int j = 0; j < p.Modules.Count; j++)
                    if (p.Modules[j] is BDModuleSurfaceAI ai) ais.Add(ai);
            }
            return ais.Count > 0;
        }

        /// <summary>The AI flying the boat: the first enabled one, else the first one.</summary>
        BDModuleSurfaceAI Current
        {
            get
            {
                BDModuleSurfaceAI first = null;
                for (int i = 0; i < ais.Count; i++)
                {
                    BDModuleSurfaceAI ai = ais[i];
                    if (ai == null || ai.vessel != vessel) continue;
                    if (ai.pilotEnabled) return ai;
                    if (first == null) first = ai;
                }
                return first;
            }
        }

        public bool Present => Current != null;

        public bool IsSubmarine
        {
            get
            {
                BDModuleSurfaceAI ai = Current;
                // String compare: SurfaceType does Enum.Parse (allocates) on every call.
                return ai != null && ai.SurfaceTypeName == SubmarineType;
            }
        }

        public bool Active
        {
            get
            {
                BDModuleSurfaceAI ai = Current;
                return ai != null && ai.pilotEnabled && ai.SurfaceTypeName == SubmarineType;
            }
        }

        public float CruiseAltitude
        {
            get
            {
                BDModuleSurfaceAI ai = Current;
                return ai != null ? ai.CombatAltitude : 0f;
            }
        }

        static bool FurtherThan(Vector3 a, Vector3 b, float distance) => (a - b).sqrMagnitude > distance * distance;

        public float CommandedAltitude()
        {
            BDModuleSurfaceAI ai = Current;
            if (ai == null) return 0f;
            float alt = ai.CombatAltitude;
            MissileFire wm = ai.WeaponManager;
            if (wm == null || wm.currentTarget == null) return alt;
            IBDWeapon weapon = wm.selectedWeapon;
            if (weapon == null) return alt;
            TargetInfo target = wm.currentTarget;
            Vessel tv = target.Vessel;
            if (tv == null) return alt;
            Vector3 com = vessel.CoM;

            switch (weapon.GetWeaponClass())
            {
                case WeaponClasses.Missile:
                    return PeriscopeDepth;

                case WeaponClasses.Gun:
                    {
                        ModuleWeapon gun = wm.currentGun;
                        bool turret = gun != null && gun.turret != null;
                        if (BDArmorySettings.BULLET_WATER_DRAG)
                        {
                            if (target.isSplashed || ((target.isFlying || tv.situation == Vessel.Situations.LANDED) && turret))
                                alt = FurtherThan(com, tv.CoM, weapon.GetEngageRange()) ? PeriscopeDepth : Surface;
                            return alt;
                        }
                        if (tv.situation == Vessel.Situations.LANDED && turret)
                            alt = FurtherThan(com, tv.CoM, weapon.GetEngageRange()) ? PeriscopeDepth : Surface;
                        if (turret || !target.isSplashed) return alt;
                        return Pursue(ai, alt, tv, com);
                    }

                case WeaponClasses.Rocket:
                case WeaponClasses.DefenseLaser:
                    {
                        ModuleWeapon gun = wm.currentGun;
                        bool turret = gun != null && gun.turret != null;
                        if (tv.situation == Vessel.Situations.LANDED || (target.isFlying && turret))
                            alt = FurtherThan(com, tv.CoM, weapon.GetEngageRange()) ? PeriscopeDepth : Surface;
                        if (!target.isSplashed) return alt;
                        return Pursue(ai, alt, tv, com);
                    }

                default: // SLW (torpedoes), bombs, ...: stay at combat depth
                    return alt;
            }
        }

        /// <summary>Fixed weapons against a ship or sub: dive/climb to the target's altitude unless the AI is extending away.</summary>
        static float Pursue(BDModuleSurfaceAI ai, float alt, Vessel tv, Vector3 com)
        {
            bool extending = getDoExtend != null && getDoExtend(ai);
            if (!extending && tv.altitude < ai.CombatAltitude / 4f && FurtherThan(com, tv.CoM, 200f))
                return (float)tv.altitude;
            return alt;
        }
    }
}
