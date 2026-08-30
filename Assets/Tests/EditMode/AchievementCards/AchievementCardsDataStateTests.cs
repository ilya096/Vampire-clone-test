using System.Collections.Generic;
using NUnit.Framework;

namespace LogoSurvivor.AchievementCards.Tests
{
    public sealed class AchievementCardsDataStateTests
    {
        private static readonly string[] ExpectedIds =
        {
            "direction_game_design_guild",
            "stage_preproduction",
            "direction_art_club",
            "stage_production",
            "direction_programmers_club",
            "stage_release_postproduction"
        };

        [Test]
        public void DefaultCatalog_HasSixUniqueCardsInApprovedOrder()
        {
            AchievementCardsCatalog catalog = AchievementCardsCatalog.CreateDefault();

            Assert.That(catalog.ValidateForRuntime().IsValid, Is.True);
            Assert.That(catalog.ValidateForRelease().IsValid, Is.True);
            Assert.That(catalog.Cards, Has.Count.EqualTo(6));

            for (int index = 0; index < ExpectedIds.Length; index++)
            {
                Assert.That(catalog.Cards[index].Id, Is.EqualTo(ExpectedIds[index]));
                Assert.That(catalog.Cards[index].Order, Is.EqualTo(index + 1));
                Assert.That((int)catalog.Cards[index].WaveSlot, Is.EqualTo(index + 1));
            }
        }

        [Test]
        public void SwappedWaveMapping_FailsRuntimeValidation()
        {
            AchievementCardsCatalog source = AchievementCardsCatalog.CreateDefault();
            AchievementCardDefinition first = source.Cards[0];
            AchievementCardDefinition invalid = new(
                first.Id,
                first.Kind,
                first.Order,
                AchievementWaveSlot.ArenaPWave2,
                first.TitleKey,
                first.IntroKey,
                first.Point1Key,
                first.Point2Key,
                first.Image);

            AchievementCardsCatalog catalog = ReplaceFirstCard(invalid);

            Assert.That(catalog.ValidateForRuntime().IsValid, Is.False);
        }

        [Test]
        public void WaveCompletion_AwardsMappedCardAndDuplicateDoesNotShiftCollection()
        {
            AchievementCardsSession session = CreateSession();

            AchievementAwardResult first = session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);
            AchievementAwardResult duplicate = session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);
            AchievementAwardResult second = session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave2);

            Assert.That(first.Status, Is.EqualTo(AchievementAwardStatus.Awarded));
            Assert.That(first.Card.Id, Is.EqualTo(ExpectedIds[0]));
            Assert.That(duplicate.Status, Is.EqualTo(AchievementAwardStatus.DuplicateIgnored));
            Assert.That(second.Card.Id, Is.EqualTo(ExpectedIds[1]));
            Assert.That(session.ObtainedCount, Is.EqualTo(2));
            Assert.That(session.PendingPresentationCount, Is.EqualTo(2));
        }

        [Test]
        public void UpgradeChoice_KeepsCardQueuedUntilChoiceFinishes()
        {
            AchievementCardsSession session = CreateSession();
            session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);

            bool whileUpgradeIsOpen = session.TryTakeNextForPresentation(true, out _);
            bool afterUpgradeCloses = session.TryTakeNextForPresentation(false, out AchievementCardDefinition card);

            Assert.That(whileUpgradeIsOpen, Is.False);
            Assert.That(afterUpgradeCloses, Is.True);
            Assert.That(card.Id, Is.EqualTo(ExpectedIds[0]));
            Assert.That(session.PendingPresentationCount, Is.Zero);
        }

        [Test]
        public void DefeatAfterWave_KeepsAwardButSuppressesPendingPresentation()
        {
            AchievementCardsSession session = CreateSession();
            session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);

            bool finished = session.TryFinish(AchievementSessionState.Defeat);

            Assert.That(finished, Is.True);
            Assert.That(session.IsObtained(ExpectedIds[0]), Is.True);
            Assert.That(session.PendingPresentationCount, Is.Zero);
            Assert.That(session.CreateSnapshot().ObtainedCount, Is.EqualTo(1));
        }

        [Test]
        public void DefeatBeforeWave_PreventsAward()
        {
            AchievementCardsSession session = CreateSession();
            session.TryFinish(AchievementSessionState.Defeat);

            AchievementAwardResult result = session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);

            Assert.That(result.Status, Is.EqualTo(AchievementAwardStatus.SessionFinished));
            Assert.That(session.CreateSnapshot().ObtainedCount, Is.Zero);
        }

        [Test]
        public void Reset_ClearsSessionStateWithoutChangingCatalog()
        {
            AchievementCardsSession session = CreateSession();
            session.DismissIntro();
            session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);
            session.TryFinish(AchievementSessionState.Victory);

            session.Reset();

            Assert.That(session.State, Is.EqualTo(AchievementSessionState.Active));
            Assert.That(session.IntroPending, Is.True);
            Assert.That(session.ObtainedCount, Is.Zero);
            Assert.That(session.PendingPresentationCount, Is.Zero);
            Assert.That(session.CreateSnapshot().Slots, Has.Count.EqualTo(6));
        }

        [Test]
        public void DisabledFeature_HidesIntroAndDoesNotCollectCards()
        {
            AchievementCardsSession session = new(
                AchievementCardsConfig.CreateDefault(enabled: false),
                AchievementCardsCatalog.CreateDefault());

            AchievementAwardResult result = session.RegisterWaveCompleted(AchievementWaveSlot.ArenaPWave1);

            Assert.That(session.IntroPending, Is.False);
            Assert.That(result.Status, Is.EqualTo(AchievementAwardStatus.FeatureDisabled));
            Assert.That(session.ObtainedCount, Is.Zero);
        }

        [Test]
        public void UnapprovedRealMaterial_UsesPlaceholderAndFailsReleaseValidation()
        {
            AchievementCardDefinition unsafeCard = CreateRealMaterialCard(
                AchievementRightsStatus.NotApproved,
                source: "owner-source",
                author: "owner-author");
            AchievementResolvedImage resolved = unsafeCard.Image.Resolve();
            AchievementCardsCatalog catalog = ReplaceFirstCard(unsafeCard);

            Assert.That(resolved.AssetId, Is.EqualTo("safe-placeholder"));
            Assert.That(resolved.UsedRightsFallback, Is.True);
            Assert.That(catalog.ValidateForRuntime().IsValid, Is.True);
            Assert.That(catalog.ValidateForRelease().IsValid, Is.False);
        }

        [Test]
        public void ApprovedRealMaterial_UsesRealAssetAndPassesReleaseValidation()
        {
            AchievementCardDefinition approvedCard = CreateRealMaterialCard(
                AchievementRightsStatus.Approved,
                source: "owner-source",
                author: "owner-author");
            AchievementResolvedImage resolved = approvedCard.Image.Resolve();
            AchievementCardsCatalog catalog = ReplaceFirstCard(approvedCard);

            Assert.That(resolved.AssetId, Is.EqualTo("approved-real-asset"));
            Assert.That(resolved.UsedRightsFallback, Is.False);
            Assert.That(catalog.ValidateForRelease().IsValid, Is.True);
        }

        private static AchievementCardsSession CreateSession()
        {
            return new AchievementCardsSession(
                AchievementCardsConfig.CreateDefault(),
                AchievementCardsCatalog.CreateDefault());
        }

        private static AchievementCardDefinition CreateRealMaterialCard(
            AchievementRightsStatus rightsStatus,
            string source,
            string author)
        {
            AchievementCardDefinition sourceCard = AchievementCardsCatalog.CreateDefault().Cards[0];
            return new AchievementCardDefinition(
                sourceCard.Id,
                sourceCard.Kind,
                sourceCard.Order,
                sourceCard.WaveSlot,
                sourceCard.TitleKey,
                sourceCard.IntroKey,
                sourceCard.Point1Key,
                sourceCard.Point2Key,
                new AchievementCardImageDefinition(
                    AchievementMaterialKind.Real,
                    rightsStatus,
                    "approved-real-asset",
                    "safe-placeholder",
                    "achievement_cards.real_alt",
                    source,
                    author));
        }

        private static AchievementCardsCatalog ReplaceFirstCard(AchievementCardDefinition replacement)
        {
            List<AchievementCardDefinition> cards = new(AchievementCardsCatalog.CreateDefault().Cards);
            cards[0] = replacement;
            return new AchievementCardsCatalog(cards);
        }
    }
}
