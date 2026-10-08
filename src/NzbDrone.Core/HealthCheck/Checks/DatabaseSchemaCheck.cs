using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class DatabaseSchemaCheck : HealthCheckBase
    {
        private readonly IMainDatabase _database;

        public DatabaseSchemaCheck(IMainDatabase database, ILocalizationService localizationService)
            : base(localizationService)
        {
            _database = database;
        }

        // The schema only changes when migrations run, and those run only at startup.
        public override bool CheckOnSchedule => false;

        public override HealthCheck Check()
        {
            var missing = SchemaVerifier.FindMissingColumns(_database);

            if (missing.Any())
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    string.Format(_localizationService.GetLocalizedString("DatabaseSchemaHealthCheckMessage"), string.Join(", ", missing)),
                    "#database-is-missing-columns");
            }

            return new HealthCheck(GetType());
        }
    }
}
