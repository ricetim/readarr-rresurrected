using System;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Analytics
{
    public interface IAnalyticsService
    {
        bool IsEnabled { get; }
        bool InstallIsActive { get; }
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IHistoryService _historyService;

        public AnalyticsService(IHistoryService historyService, IConfigFileProvider configFileProvider)
        {
            _configFileProvider = configFileProvider;
            _historyService = historyService;
        }

        // Usage data went to the original Readarr project's services, which this fork neither runs
        // nor controls, so it is never sent. The AnalyticsEnabled setting is kept only so existing
        // config files still load.
        public bool IsEnabled => false;

        public bool InstallIsActive
        {
            get
            {
                var lastRecord = _historyService.Paged(new PagingSpec<EntityHistory>() { Page = 0, PageSize = 1, SortKey = "date", SortDirection = SortDirection.Descending });
                var monthAgo = DateTime.UtcNow.AddMonths(-1);

                return lastRecord.Records.Any(v => v.Date > monthAgo);
            }
        }
    }
}
