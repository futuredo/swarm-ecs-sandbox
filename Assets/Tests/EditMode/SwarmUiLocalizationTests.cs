using NUnit.Framework;
using SwarmECS.Runtime;

namespace SwarmECS.Tests.EditMode
{
    public sealed class SwarmUiLocalizationTests
    {
        [Test]
        public void Select_ReturnsRequestedLanguageWithoutChangingContent()
        {
            Assert.That(
                SwarmUiLocalization.Select(SwarmUiLanguage.English, "English", "简体中文"),
                Is.EqualTo("English"));
            Assert.That(
                SwarmUiLocalization.Select(SwarmUiLanguage.SimplifiedChinese, "English", "简体中文"),
                Is.EqualTo("简体中文"));
        }

        [Test]
        public void ViewLabels_CoverEveryTechnicalLabViewInBothLanguages()
        {
            for (int index = 0; index <= (int)SwarmLabView.Network; index++)
            {
                string english = SwarmUiLocalization.GetViewLabel(SwarmUiLanguage.English, index);
                string chinese = SwarmUiLocalization.GetViewLabel(SwarmUiLanguage.SimplifiedChinese, index);

                Assert.That(english, Is.Not.Empty, $"Missing English label for view {index}.");
                Assert.That(chinese, Is.Not.Empty, $"Missing Chinese label for view {index}.");
                Assert.That(chinese, Is.Not.EqualTo(english), $"View {index} is not localized.");
            }
        }

        [Test]
        public void InvalidViewLabel_ReturnsEmptyString()
        {
            Assert.That(
                SwarmUiLocalization.GetViewLabel(SwarmUiLanguage.English, -1),
                Is.Empty);
            Assert.That(
                SwarmUiLocalization.GetViewLabel(SwarmUiLanguage.SimplifiedChinese, 6),
                Is.Empty);
        }
    }
}
