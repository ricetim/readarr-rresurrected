using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Analytics
{
    [TestFixture]
    public class AnalyticsServiceFixture : CoreTest<AnalyticsService>
    {
        // The web UI and the update check both read IsEnabled. Usage data used to go to the
        // original Readarr project's services, so it must stay off whatever the setting says.
        [TestCase(true)]
        [TestCase(false)]
        public void should_never_be_enabled(bool analyticsSetting)
        {
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(s => s.AnalyticsEnabled)
                  .Returns(analyticsSetting);

            Subject.IsEnabled.Should().BeFalse();
        }
    }
}
