using System;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldControl
{
    public static class DayNightGovernor
    {
        public static float ModifyDayLengthSec(float vanillaSec)
        {
            if (WonderlandConfig.CustomDayLengthMinutes == null) return vanillaSec;
            float customMins = WonderlandConfig.CustomDayLengthMinutes.Value;
            if (customMins <= 0f) return vanillaSec;
            return customMins * 60f;
        }
    }
}
