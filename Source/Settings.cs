namespace CelestialBodyMover
{
    public class CBMSettings : GameParameters.CustomParameterNode
    {
        public override string Title => "Settings";

        public override string DisplaySection => "CelestialBodyMover";

        public override string Section => "CelestialBodyMover"; // this is the key in the listDictionary

        public override int SectionOrder => 0;

        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;

        public override bool HasPresets => true;

        public override void SetDifficultyPreset(GameParameters.Preset preset) { } // throws an exception if not implemented

        // displayFormat follows https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings

        // TODO just switch to a normal settings menu

        [GameParameters.CustomFloatParameterUI("Max Surface Height", toolTip = "When the craft is frozen, this is the max height that the vessel can be from the ground where thrust to move the body is still valid", minValue = 0f, maxValue = 200f, stepCount = 20)]
        public float maxSurfaceHeight = 20f;

        [GameParameters.CustomFloatParameterUI("Line Length", toolTip = "The scaling factor for the lines displayed", minValue = 1f, maxValue = 6f)]
        public float lineLength = 5f;

        [GameParameters.CustomParameterUI("Kill Throttle on Unfreeze", toolTip = "Set throttle to 0 when unfreezing the craft, to prevent RUDs")]
        public bool killThrottleOnUnfreeze = true;

        [GameParameters.CustomParameterUI("Debug Mode", toolTip = "Enable or disable debug mode")]
        public bool debugMode = false;
    }
}
