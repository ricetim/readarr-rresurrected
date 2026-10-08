using Dapper;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class DatabaseSchemaCheckFixture : DbTest
    {
        private DatabaseSchemaCheck Subject => Mocker.Resolve<DatabaseSchemaCheck>();

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Database is missing columns: {0}");
        }

        [Test]
        public void should_return_ok_for_a_fully_migrated_database()
        {
            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_error_naming_the_missing_column()
        {
            using (var conn = Db.OpenConnection())
            {
                conn.Execute("ALTER TABLE \"AuthorMetadata\" RENAME COLUMN \"Kca\" TO \"KcaFromAnotherFork\"");
            }

            Subject.Check().ShouldBeError("Database is missing columns: AuthorMetadata.Kca");
        }
    }
}
