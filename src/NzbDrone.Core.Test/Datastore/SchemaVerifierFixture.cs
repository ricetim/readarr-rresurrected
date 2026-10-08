using System;
using System.Collections.Generic;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class SchemaVerifierFixture : DbTest
    {
        // This is what keeps the startup check honest: if a model property is ever added
        // without a migration, or the check misreads a real schema, this fails in CI rather
        // than warning every user about a problem their database doesn't have.
        [Test]
        public void fully_migrated_database_should_have_every_mapped_column()
        {
            SchemaVerifier.FindMissingColumns(Mocker.Resolve<IMainDatabase>()).Should().BeEmpty();
        }

        [Test]
        public void should_report_a_column_missing_from_the_live_schema()
        {
            using (var conn = Db.OpenConnection())
            {
                conn.Execute("ALTER TABLE \"AuthorMetadata\" RENAME COLUMN \"Kca\" TO \"KcaFromAnotherFork\"");
            }

            var missing = SchemaVerifier.FindMissingColumns(Mocker.Resolve<IMainDatabase>());

            missing.Should().ContainSingle();
            missing[0].Table.Should().Be("AuthorMetadata");
            missing[0].Column.Should().Be("Kca");
        }

        [Test]
        public void should_only_check_tables_present_in_the_database()
        {
            var actual = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "AuthorMetadata", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Id" } }
            };

            var missing = SchemaVerifier.Compare(actual);

            missing.Should().NotBeEmpty();
            missing.Should().OnlyContain(m => m.Table == "AuthorMetadata");
            missing.Should().NotContain(m => m.Column == "Id");
        }
    }

    [TestFixture]
    public class SchemaVerifierLogDatabaseFixture : DbTest
    {
        protected override MigrationType MigrationType => MigrationType.Log;

        [Test]
        public void fully_migrated_log_database_should_have_every_mapped_column()
        {
            SchemaVerifier.FindMissingColumns(Mocker.Resolve<ILogDatabase>()).Should().BeEmpty();
        }
    }
}
