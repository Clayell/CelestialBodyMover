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

        // displayFormat follows https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings

        [GameParameters.CustomFloatParameterUI("Max Surface Height", toolTip = "When the craft is frozen, this is the max height that the vessel can be from the ground where thrust to move the body is still valid", minValue = 0f, maxValue = 1000f, stepCount = 10, displayFormat = "F0")]
        public float maxSurfaceHeight = 10f;
    }
}
