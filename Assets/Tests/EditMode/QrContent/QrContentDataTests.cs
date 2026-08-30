using System.Collections.Generic;
using NUnit.Framework;

namespace LogoSurvivor.QrContent.Tests
{
    public sealed class QrContentDataTests
    {
        [Test]
        public void DefaultCatalog_UsesCanonicalFallbackForBothDestinations()
        {
            QrContentCatalog catalog = QrContentCatalog.CreateCanonicalFallback();
            QrContentSnapshot snapshot = catalog.CreateSnapshot();

            Assert.That(catalog.Validate().IsValid, Is.True);
            Assert.That(snapshot.Destinations, Has.Count.EqualTo(2));
            Assert.That(snapshot.Destinations[0].Id, Is.EqualTo("telegram"));
            Assert.That(snapshot.Destinations[1].Id, Is.EqualTo("vk"));
            Assert.That(snapshot.Destinations[0].EffectiveUrl, Is.EqualTo(QrContentCatalog.TelegramCanonicalUrl));
            Assert.That(snapshot.Destinations[1].EffectiveUrl, Is.EqualTo(QrContentCatalog.VkCanonicalUrl));
            Assert.That(snapshot.Destinations[0].State, Is.EqualTo(QrDestinationState.Fallback));
        }

        [Test]
        public void TrackedCandidates_ContainOnlyFixedNonPersonalMarker()
        {
            QrContentCatalog catalog = QrContentCatalog.CreateCanonicalFallback();

            foreach (QrDestinationDefinition destination in catalog.Destinations)
            {
                Assert.That(destination.TrackedUrl, Does.EndWith("?source=logogame"));
                Assert.That(destination.TrackedUrl, Does.Not.Contain("session"));
                Assert.That(destination.TrackedUrl, Does.Not.Contain("device"));
                Assert.That(destination.TrackedUrl, Does.Not.Contain("timestamp"));
            }
        }

        [Test]
        public void ValidatedMarker_BecomesEffectiveUrl()
        {
            QrDestinationDefinition telegram = CreateTelegram(
                markerValidated: true,
                effectiveUrl: QrContentCatalog.TelegramCanonicalUrl + "?source=logogame");
            QrContentCatalog catalog = ReplaceTelegram(telegram);

            Assert.That(catalog.Validate().IsValid, Is.True);
            Assert.That(catalog.CreateSnapshot().Destinations[0].EffectiveUrl, Is.EqualTo(telegram.TrackedUrl));
        }

        [Test]
        public void MismatchedEffectiveUrl_FailsValidation()
        {
            QrDestinationDefinition telegram = CreateTelegram(
                markerValidated: false,
                effectiveUrl: QrContentCatalog.TelegramCanonicalUrl + "?source=logogame");

            Assert.That(ReplaceTelegram(telegram).Validate().IsValid, Is.False);
        }

        [Test]
        public void MismatchedQrTarget_FailsValidation()
        {
            QrDestinationDefinition telegram = new(
                "telegram",
                "TELEGRAM",
                "Новости и анонсы",
                QrContentCatalog.TelegramCanonicalUrl,
                QrContentCatalog.TelegramCanonicalUrl + "?source=logogame",
                false,
                QrContentCatalog.TelegramCanonicalUrl,
                "qr/telegram",
                "https://example.invalid",
                true);

            Assert.That(ReplaceTelegram(telegram).Validate().IsValid, Is.False);
        }

        [Test]
        public void DisabledFeature_ReturnsEmptySnapshotWithoutChangingCatalog()
        {
            QrContentCatalog catalog = QrContentCatalog.CreateCanonicalFallback();

            Assert.That(catalog.CreateSnapshot(enabled: false).Destinations, Is.Empty);
            Assert.That(catalog.Destinations, Has.Count.EqualTo(2));
        }

        private static QrDestinationDefinition CreateTelegram(bool markerValidated, string effectiveUrl)
        {
            return new QrDestinationDefinition(
                "telegram",
                "TELEGRAM",
                "Новости и анонсы",
                QrContentCatalog.TelegramCanonicalUrl,
                QrContentCatalog.TelegramCanonicalUrl + "?source=logogame",
                markerValidated,
                effectiveUrl,
                string.Empty,
                string.Empty,
                false);
        }

        private static QrContentCatalog ReplaceTelegram(QrDestinationDefinition telegram)
        {
            List<QrDestinationDefinition> destinations = new(QrContentCatalog.CreateCanonicalFallback().Destinations);
            destinations[0] = telegram;
            return new QrContentCatalog(destinations);
        }
    }
}
