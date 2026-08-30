using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LogoSurvivor.QrContent
{
    public enum QrDestinationState
    {
        Available,
        Fallback,
        Hidden
    }

    public sealed class QrDestinationDefinition
    {
        public QrDestinationDefinition(
            string id,
            string title,
            string description,
            string canonicalUrl,
            string trackedUrl,
            bool trackingMarkerValidated,
            string effectiveUrl,
            string qrAssetId,
            string qrTargetUrl,
            bool qrValidated,
            bool visible = true)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            CanonicalUrl = canonicalUrl ?? string.Empty;
            TrackedUrl = trackedUrl ?? string.Empty;
            TrackingMarkerValidated = trackingMarkerValidated;
            EffectiveUrl = effectiveUrl ?? string.Empty;
            QrAssetId = qrAssetId ?? string.Empty;
            QrTargetUrl = qrTargetUrl ?? string.Empty;
            QrValidated = qrValidated;
            Visible = visible;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string CanonicalUrl { get; }
        public string TrackedUrl { get; }
        public bool TrackingMarkerValidated { get; }
        public string EffectiveUrl { get; }
        public string QrAssetId { get; }
        public string QrTargetUrl { get; }
        public bool QrValidated { get; }
        public bool Visible { get; }

        public QrDestinationState State => Visible == false
            ? QrDestinationState.Hidden
            : QrValidated && string.IsNullOrWhiteSpace(QrAssetId) == false
                ? QrDestinationState.Available
                : QrDestinationState.Fallback;
    }

    public sealed class QrContentSnapshot
    {
        public QrContentSnapshot(IList<QrDestinationDefinition> destinations)
        {
            if (destinations == null)
            {
                throw new ArgumentNullException(nameof(destinations));
            }

            Destinations = new ReadOnlyCollection<QrDestinationDefinition>(
                new List<QrDestinationDefinition>(destinations));
        }

        public IReadOnlyList<QrDestinationDefinition> Destinations { get; }
        public bool HasVisibleDestinations => Destinations.Count > 0;
    }

    public sealed class QrContentValidationResult
    {
        public QrContentValidationResult(IList<string> errors)
        {
            Errors = new ReadOnlyCollection<string>(new List<string>(errors));
        }

        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }
}
