using UnityEngine;

namespace SubTrim
{
    /// <summary>
    /// Part Action Window front end for SubTrim, and the cfg flag that marks a vessel as a submarine.
    /// Added by MM to every command part (Patches/SubTrim_CommandParts.cfg) and, with isSubmarine = true, to known
    /// submarine hulls. All logic lives in SubTrimVesselModule; when a vessel has several of these modules the one the
    /// player last touched is the master and the others mirror it.
    /// </summary>
    [KSPModule("SubTrim")]
    public class ModuleSubTrim : PartModule
    {
        const string Group = "SubTrim";
        const string GroupTitle = "SubTrim depth & trim";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "SubTrim",
            groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        [UI_ChooseOption(options = new[] { SubTrimVesselModule.ModeAuto, SubTrimVesselModule.ModeOn, SubTrimVesselModule.ModeOff },
            display = new[] { "Auto (submarines)", "On", "Off" })]
        public string mode = SubTrimVesselModule.ModeAuto;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Depth target",
            groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        [UI_Toggle(enabledText = "Manual (below)", disabledText = "AI / hold")]
        public bool manualDepth = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Manual depth", guiUnits = " m",
            groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        [UI_FloatRange(minValue = 0f, maxValue = 400f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float targetDepth = 50f;

        [KSPField(guiActive = true, guiName = "Status", groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        public string status = "-";

        [KSPField(guiActive = true, guiName = "Trim", groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        public string trim = "-";

        [KSPField(guiActive = true, guiName = "Ballast", groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        public string ballast = "-";

        /// <summary>cfg: this part makes the vessel a submarine for Auto mode, even without a BDA Submarine AI.</summary>
        [KSPField] public bool isSubmarine = false;

        /// <summary>cfg: allow the virtual-ballast fallback on this vessel (any module set to false disables it).</summary>
        [KSPField] public bool allowVirtualBallast = true;

        SubTrimVesselModule vm;
        float uiTimer;

        SubTrimVesselModule VM
        {
            get
            {
                if (!HighLogic.LoadedSceneIsFlight || vessel == null) return null;
                if (vm == null || vm.Vessel != vessel) vm = vessel.FindVesselModuleImplementing<SubTrimVesselModule>();
                return vm;
            }
        }

        public override void OnStart(StartState state)
        {
            Hook(nameof(mode));
            Hook(nameof(manualDepth));
            Hook(nameof(targetDepth));
            UpdateEventNames();
        }

        void Hook(string field)
        {
            BaseField f = Fields[field];
            if (f == null) return;
            if (f.uiControlFlight != null) f.uiControlFlight.onFieldChanged = OnSettingChanged;
            if (f.uiControlEditor != null) f.uiControlEditor.onFieldChanged = OnSettingChanged;
        }

        void OnSettingChanged(BaseField field, object oldValue)
        {
            SubTrimVesselModule m = VM;
            if (m != null) m.PanelChanged(this);
        }

        public override void OnUpdate()
        {
            if (part.PartActionWindow == null)
            {
                // This part left the cached module's vessel (broke off, undocked): drop the reference, so a debris part
                // does not keep that vessel's module, and through it the vessel and its parts, alive after it dies.
                if ((object)vm != null && (object)vm.Vessel != vessel) vm = null;
                return;
            }
            uiTimer -= Time.deltaTime;
            if (uiTimer > 0f) return;
            uiTimer = 0.5f;
            SubTrimVesselModule m = VM;
            if (m == null) return;
            m.FillStatus(this);
            UpdateEventNames();
        }

        void UpdateEventNames()
        {
            BaseEvent e = Events[nameof(ToggleEmergencyBlow)];
            if (e == null) return;
            SubTrimVesselModule m = VM;
            e.guiName = m != null && m.emergencyBlow ? "Cancel emergency blow" : "Emergency blow";
        }

        void Notify()
        {
            SubTrimVesselModule m = VM;
            if (m != null) m.PanelChanged(this);
        }

        [KSPEvent(guiActive = true, guiName = "Emergency blow", groupName = Group, groupDisplayName = GroupTitle, groupStartCollapsed = true)]
        public void ToggleEmergencyBlow()
        {
            SubTrimVesselModule m = VM;
            if (m == null) return;
            m.ToggleEmergency();
            UpdateEventNames();
            ScreenMessages.PostScreenMessage(m.emergencyBlow ? "SubTrim: emergency blow!" : "SubTrim: emergency blow cancelled", 3f, ScreenMessageStyle.UPPER_CENTER);
        }

        [KSPAction("SubTrim: toggle on/off")]
        public void ToggleAction(KSPActionParam param)
        {
            mode = mode == SubTrimVesselModule.ModeOff ? SubTrimVesselModule.ModeOn : SubTrimVesselModule.ModeOff;
            Notify();
            ScreenMessages.PostScreenMessage("SubTrim: " + mode, 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        [KSPAction("SubTrim: emergency blow")]
        public void EmergencyAction(KSPActionParam param) => ToggleEmergencyBlow();

        [KSPAction("SubTrim: 10 m deeper")]
        public void DeeperAction(KSPActionParam param) => StepDepth(10f);

        [KSPAction("SubTrim: 10 m shallower")]
        public void ShallowerAction(KSPActionParam param) => StepDepth(-10f);

        [KSPAction("SubTrim: follow AI depth")]
        public void FollowAIAction(KSPActionParam param)
        {
            manualDepth = false;
            Notify();
            ScreenMessages.PostScreenMessage("SubTrim: following AI depth", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        void StepDepth(float delta)
        {
            manualDepth = true;
            targetDepth = Mathf.Clamp(targetDepth + delta, 0f, 400f);
            Notify();
            ScreenMessages.PostScreenMessage("SubTrim: manual depth " + targetDepth.ToString("F0") + " m", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        public override string GetInfo()
        {
            return isSubmarine
                ? "Marks the vessel as a submarine: SubTrim keeps it near neutral buoyancy and holds the AI's (or your) depth with its ballast."
                : "SubTrim depth/trim panel. Auto mode manages vessels with a BDArmory Submarine AI or a submarine hull.";
        }
    }
}
