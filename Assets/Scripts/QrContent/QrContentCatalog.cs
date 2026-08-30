using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LogoSurvivor.QrContent
{
    public sealed class QrContentCatalog
    {
        public const string CampaignSource = "logogame";
        public const string TelegramCanonicalUrl = "https://t.me/respawn_announcements";
        public const string VkCanonicalUrl = "https://vk.com/respawn_hub";

        private static readonly string[] ApprovedIds = { "telegram", "vk" };
        private static readonly string[] ApprovedCanonicalUrls =
        {
            TelegramCanonicalUrl,
            VkCanonicalUrl
        };

        private readonly ReadOnlyCollection<QrDestinationDefinition> _destinations;

        public QrContentCatalog(IList<QrDestinationDefinition> destinations)
        {
            if (destinations == null)
            {
                throw new ArgumentNullException(nameof(destinations));
            }

            _destinations = new ReadOnlyCollection<QrDestinationDefinition>(
                new List<QrDestinationDefinition>(destinations));
        }

        public IReadOnlyList<QrDestinationDefinition> Destinations => _destinations;

        public static QrContentCatalog CreateCanonicalFallback()
        {
            return new QrContentCatalog(new[]
            {
                CreateUnverified("telegram", "TELEGRAM", TelegramCanonicalUrl),
                CreateUnverified("vk", "VK", VkCanonicalUrl)
            });
        }

        public QrContentValidationResult Validate()
        {
            List<string> errors = new();
            if (_destinations.Count != ApprovedIds.Length)
            {
                errors.Add($"QR catalog must contain exactly {ApprovedIds.Length} destinations.");
            }

            HashSet<string> ids = new(StringComparer.Ordinal);
            for (int index = 0; index < _destinations.Count; index++)
            {
                QrDestinationDefinition destination = _destinations[index];
                if (destination == null)
                {
                    errors.Add("QR catalog contains a null destination.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(destination.Id) || ids.Add(destination.Id) == false)
                {
                    errors.Add($"QR destination ID must be non-empty and unique: '{destination.Id}'.");
                }

                if (index < ApprovedIds.Length)
                {
                    if (string.Equals(destination.Id, ApprovedIds[index], StringComparison.Ordinal) == false)
                    {
                        errors.Add($"QR destination {index} must use ID '{ApprovedIds[index]}'.");
                    }

                    if (string.Equals(destination.CanonicalUrl, ApprovedCanonicalUrls[index], StringComparison.Ordinal) == false)
                    {
                        errors.Add($"Destination '{destination.Id}' has an unapproved canonical URL.");
                    }
                }

                ValidateDestination(destination, errors);
            }

            return new QrContentValidationResult(errors);
        }

        public QrContentSnapshot CreateSnapshot(bool enabled = true)
        {
            QrContentValidationResult validation = Validate();
            if (validation.IsValid == false)
            {
                throw new InvalidOperationException(
                    "QR content catalog is invalid: " + string.Join(" | ", validation.Errors));
            }

            List<QrDestinationDefinition> visible = new();
            if (enabled)
            {
                foreach (QrDestinationDefinition destination in _destinations)
                {
                    if (destination.Visible)
                    {
                        visible.Add(destination);
                    }
                }
            }

            return new QrContentSnapshot(visible);
        }

        private static QrDestinationDefinition CreateUnverified(
            string id,
            string title,
            string canonicalUrl)
        {
            return new QrDestinationDefinition(
                id,
                title,
                "Новости и анонсы",
                canonicalUrl,
                $"{canonicalUrl}?source={CampaignSource}",
                trackingMarkerValidated: false,
                effectiveUrl: canonicalUrl,
                qrAssetId: string.Empty,
                qrTargetUrl: string.Empty,
                qrValidated: false);
        }

        private static void ValidateDestination(
            QrDestinationDefinition destination,
            ICollection<string> errors)
        {
            if (Uri.TryCreate(destination.CanonicalUrl, UriKind.Absolute, out Uri canonical) == false
                || string.Equals(canonical.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) == false
                || string.IsNullOrEmpty(canonical.Query) == false
                || string.IsNullOrEmpty(canonical.Fragment) == false)
            {
                errors.Add($"Destination '{destination.Id}' requires a canonical HTTPS URL without query or fragment.");
            }

            string expectedTracked = $"{destination.CanonicalUrl}?source={CampaignSource}";
            if (string.Equals(destination.TrackedUrl, expectedTracked, StringComparison.Ordinal) == false)
            {
                errors.Add($"Destination '{destination.Id}' tracked URL must contain only source={CampaignSource}.");
            }

            string expectedEffective = destination.TrackingMarkerValidated
                ? destination.TrackedUrl
                : destination.CanonicalUrl;
            if (string.Equals(destination.EffectiveUrl, expectedEffective, StringComparison.Ordinal) == false)
            {
                errors.Add($"Destination '{destination.Id}' effective URL does not match validation state.");
            }

            if (destination.QrValidated)
            {
                if (string.IsNullOrWhiteSpace(destination.QrAssetId)
                    || string.Equals(destination.QrTargetUrl, destination.EffectiveUrl, StringComparison.Ordinal) == false)
                {
                    errors.Add($"Destination '{destination.Id}' validated QR must target its effective URL.");
                }
            }
            else if (string.IsNullOrWhiteSpace(destination.QrAssetId) == false
                || string.IsNullOrWhiteSpace(destination.QrTargetUrl) == false)
            {
                errors.Add($"Destination '{destination.Id}' cannot expose an unvalidated QR asset.");
            }
        }
    }
}
