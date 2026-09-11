using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public interface IUpgradableSpecification
    {
        bool IsUpgradable(QualityProfile profile, QualityModel currentQuality, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats);
        bool QualityCutoffNotMet(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality = null);
        bool CutoffNotMet(QualityProfile profile, List<QualityModel> currentQualities, List<CustomFormat> currentFormats, QualityModel newQuality = null);
        bool IsRevisionUpgrade(QualityModel currentQuality, QualityModel newQuality);
        bool IsUpgradeAllowed(QualityProfile qualityProfile, QualityModel currentQuality, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats);
    }

    public class UpgradableSpecification : IUpgradableSpecification
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpgradableSpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        /// <summary>
        /// Ebooks and audiobooks are independent. Neither upgrades nor downgrades the other, so
        /// a file of the other type must never block a grab or count towards its cutoff.
        /// </summary>
        private static bool IsComparable(QualityModel current, QualityModel candidate)
        {
            return current?.Quality == null
                || candidate?.Quality == null
                || current.Quality.MediaType == candidate.Quality.MediaType;
        }

        private ProfileComparisonResult IsQualityUpgradable(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality = null)
        {
            if (newQuality != null)
            {
                var totalCompare = 0;

                var compare = new QualityModelComparer(profile).Compare(newQuality, currentQuality);

                totalCompare += compare;

                if (compare < 0)
                {
                    // Not upgradable if new quality is a downgrade for any current quality
                    return ProfileComparisonResult.Downgrade;
                }

                // Not upgradable if new quality is equal to all current qualities
                if (totalCompare == 0)
                {
                    return ProfileComparisonResult.Equal;
                }

                // Accept unless the user doesn't want to prefer propers, optionally they can
                // use preferred words to prefer propers/repacks over non-propers/repacks.
                if (_configService.DownloadPropersAndRepacks == ProperDownloadTypes.DoNotPrefer &&
                    newQuality?.Revision.CompareTo(currentQuality.Revision) > 0)
                    {
                        return ProfileComparisonResult.Equal;
                    }
            }

            return ProfileComparisonResult.Upgrade;
        }

        public bool IsUpgradable(QualityProfile qualityProfile, QualityModel currentQualities, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            if (!IsComparable(currentQualities, newQuality))
            {
                _logger.Debug("Existing item is a different media type, not comparable");
                return true;
            }

            var qualityUpgrade = IsQualityUpgradable(qualityProfile, currentQualities, newQuality);

            if (qualityUpgrade == ProfileComparisonResult.Upgrade)
            {
                _logger.Debug("New item has a better quality");
                return true;
            }

            if (qualityUpgrade == ProfileComparisonResult.Downgrade)
            {
                _logger.Debug("Existing item has better quality, skipping");
                return false;
            }

            var currentFormatScore = qualityProfile.CalculateCustomFormatScore(currentCustomFormats);
            var newFormatScore = qualityProfile.CalculateCustomFormatScore(newCustomFormats);

            if (newFormatScore <= currentFormatScore)
            {
                _logger.Debug("New item's custom formats [{0}] do not improve on [{1}], skipping",
                              newCustomFormats.ConcatToString(),
                              currentCustomFormats.ConcatToString());

                return false;
            }

            _logger.Debug("New item has a better custom format score");
            return true;
        }

        public bool QualityCutoffNotMet(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality = null)
        {
            var cutoff = profile.EffectiveCutoff(currentQuality.Quality.MediaType);

            if (cutoff == null)
            {
                // The profile allows nothing of this media type, so there is no bar to clear.
                return false;
            }

            var cutoffCompare = new QualityModelComparer(profile).Compare(currentQuality.Quality.Id, cutoff.Value);

            if (cutoffCompare < 0)
            {
                return true;
            }

            if (newQuality != null && IsRevisionUpgrade(currentQuality, newQuality))
            {
                return true;
            }

            return false;
        }

        private bool CustomFormatCutoffNotMet(QualityProfile profile, List<CustomFormat> currentFormats)
        {
            var score = profile.CalculateCustomFormatScore(currentFormats);
            return score < profile.CutoffFormatScore;
        }

        public bool CutoffNotMet(QualityProfile profile, List<QualityModel> currentQualities, List<CustomFormat> currentFormats, QualityModel newQuality = null)
        {
            // Only files of the candidate's own media type answer the question. An audiobook
            // sitting at its cutoff says nothing about whether the ebook cutoff has been met.
            var comparable = currentQualities.Where(q => IsComparable(q, newQuality)).ToList();

            if (newQuality?.Quality != null && !comparable.Any())
            {
                _logger.Debug("No existing item of this media type, cut-off cannot have been met");
                return true;
            }

            foreach (var quality in comparable)
            {
                if (QualityCutoffNotMet(profile, quality, newQuality))
                {
                    return true;
                }
            }

            if (CustomFormatCutoffNotMet(profile, currentFormats))
            {
                return true;
            }

            _logger.Debug("Existing item meets cut-off. skipping.");

            return false;
        }

        public bool IsRevisionUpgrade(QualityModel currentQuality, QualityModel newQuality)
        {
            var compare = newQuality.Revision.CompareTo(currentQuality.Revision);

            // Comparing the quality directly because we don't want to upgrade to a proper for a webrip from a webdl or vice versa
            if (currentQuality.Quality == newQuality.Quality && compare > 0)
            {
                _logger.Debug("New quality is a better revision for existing quality");
                return true;
            }

            return false;
        }

        public bool IsUpgradeAllowed(QualityProfile qualityProfile, QualityModel currentQuality, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            if (!IsComparable(currentQuality, newQuality))
            {
                // A first copy of the other media type is an acquisition, not an upgrade, so it
                // stays allowed even when the profile forbids upgrades.
                return true;
            }

            var isQualityUpgrade = IsQualityUpgradable(qualityProfile, currentQuality, newQuality);
            var isCustomFormatUpgrade = qualityProfile.CalculateCustomFormatScore(newCustomFormats) > qualityProfile.CalculateCustomFormatScore(currentCustomFormats);

            return CheckUpgradeAllowed(qualityProfile, isQualityUpgrade, isCustomFormatUpgrade);
        }

        private bool CheckUpgradeAllowed(QualityProfile qualityProfile, ProfileComparisonResult isQualityUpgrade, bool isCustomFormatUpgrade)
        {
            if ((isQualityUpgrade == ProfileComparisonResult.Upgrade || isCustomFormatUpgrade) && qualityProfile.UpgradeAllowed)
            {
                _logger.Debug("Quality profile allows upgrading");
                return true;
            }

            if ((isQualityUpgrade == ProfileComparisonResult.Upgrade || isCustomFormatUpgrade) && !qualityProfile.UpgradeAllowed)
            {
                _logger.Debug("Quality profile does not allow upgrades, skipping");
                return false;
            }

            return true;
        }

        private enum ProfileComparisonResult
        {
            Downgrade = -1,
            Equal = 0,
            Upgrade = 1
        }
    }
}
