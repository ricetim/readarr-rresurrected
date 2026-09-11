using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Core.Qualities;

namespace Readarr.Api.V1.Profiles.Quality
{
    public static class QualityCutoffValidator
    {
        public static IRuleBuilderOptions<T, int> ValidCutoff<T>(this IRuleBuilder<T, int> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new ValidCutoffValidator<T>());
        }
    }

    public class ValidCutoffValidator<T> : PropertyValidator
    {
        protected override string GetDefaultMessageTemplate() => "Cutoff must be an allowed quality or group";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            var cutoff = (int)context.PropertyValue;
            dynamic instance = context.ParentContext.InstanceToValidate;
            var items = instance.Items as IList<QualityProfileQualityItemResource>;

            var cutoffItem = items?.SingleOrDefault(i => (i.Quality == null && i.Id == cutoff) || i.Quality?.Id == cutoff);

            if (cutoffItem == null)
            {
                return false;
            }

            // A profile that allows nothing of this media type does not want that type at all,
            // so its cutoff is inert. Demanding an allowed format there would make an
            // audiobook-only profile impossible to save.
            var mediaType = MediaTypeOf(cutoffItem);

            if (!items.Any(i => i.Allowed && MediaTypeOf(i) == mediaType))
            {
                return true;
            }

            return cutoffItem.Allowed;
        }

        private static QualityMediaType MediaTypeOf(QualityProfileQualityItemResource item)
        {
            return item.Quality?.MediaType
                   ?? item.Items?.FirstOrDefault(i => i.Quality != null)?.Quality.MediaType
                   ?? QualityMediaType.Ebook;
        }
    }
}
