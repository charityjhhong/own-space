using NUnit.Framework;

namespace OwnSpace.MR.Tests
{
    // Device batch 10: "no light comes from the lamps" (the Slim amber floor lamp by day had a 0.08 glow).
    public class LampGlowTests
    {
        [Test]
        public void LampStrength_ReadsByDay_AndIsStrongerInTheEveningAndAtNight()
        {
            float day = LightGlows.LampStrength(0.8f, 0.2f), evening = LightGlows.LampStrength(0.8f, 1f), night = LightGlows.LampStrength(0.8f, 1.4f);
            Assert.Greater(day, 0.3f, "visible by day (it was 0.08)");
            Assert.Greater(evening, day);
            Assert.Greater(night, evening);
            Assert.LessOrEqual(LightGlows.LampStrength(2f, 3f), 2f, "the shader's range");
            Assert.AreEqual(0f, LightGlows.LampStrength(0f, 1f), "a lamp of intensity 0 stays dark");
        }
    }
}
