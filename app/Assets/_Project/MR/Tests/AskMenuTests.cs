using System.Text;
using NUnit.Framework;

namespace OwnSpace.MR.Tests
{
    public class AskMenuTests
    {
        [Test]
        public void AppendWrapped_BreaksAtSpacesNearTheWidth()
        {
            var text = new StringBuilder();
            AskMenu.AppendWrapped(text, "A warm rug, a soft sofa and a floor lamp make the outside feel lived in.", 20);
            foreach (string line in text.ToString().Split('\n')) Assert.LessOrEqual(line.Length, 20, line);
            Assert.AreEqual("A warm rug, a soft sofa and a floor lamp make the outside feel lived in.", text.ToString().Replace('\n', ' '));
        }

        [Test]
        public void AppendWrapped_StartsANewLineAfterExistingText()
        {
            var text = new StringBuilder("First");
            AskMenu.AppendWrapped(text, "second");
            Assert.AreEqual("First\nsecond", text.ToString());
        }
    }
}
