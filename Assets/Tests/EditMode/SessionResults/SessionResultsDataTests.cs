using System.Collections.Generic;
using LogoSurvivor.AchievementCards;
using LogoSurvivor.QrContent;
using NUnit.Framework;

namespace LogoSurvivor.SessionResults.Tests
{
    public sealed class SessionResultsDataTests
    {
        [Test]
        public void ActiveTime_ExcludesPauseModalAndFocusLoss()
        {
            SessionResultsCoordinator coordinator = new();

            coordinator.AddActiveTime(1.5d, gameplayActive: true, focused: true);
            coordinator.AddActiveTime(5d, gameplayActive: false, focused: true);
            coordinator.AddActiveTime(5d, gameplayActive: true, focused: false);

            Assert.That(coordinator.ActiveGameplayTimeSeconds, Is.EqualTo(1.5d));
        }

        [Test]
        public void CompletedWaves_AreUniqueAndBounded()
        {
            SessionResultsCoordinator coordinator = new();

            Assert.That(coordinator.RegisterCompletedWave(1), Is.True);
            Assert.That(coordinator.RegisterCompletedWave(1), Is.False);
            Assert.That(coordinator.RegisterCompletedWave(0), Is.False);
            Assert.That(coordinator.RegisterCompletedWave(7), Is.False);
            Assert.That(coordinator.CompletedWaveCount, Is.EqualTo(1));
        }

        [Test]
        public void Outcome_CreatesOneImmutableSnapshot()
        {
            SessionResultsCoordinator coordinator = CreateCoordinatorWithTwoWaves();

            bool first = coordinator.TryCreateSnapshot(
                SessionOutcome.Defeat,
                12,
                345,
                4,
                new[] { new SessionUpgradeSnapshot("Пистолет", 2) },
                CreateCardsSnapshot(),
                CreateQrSnapshot(),
                out SessionResultSnapshot snapshot);
            bool duplicate = coordinator.TryCreateSnapshot(
                SessionOutcome.Victory,
                99,
                999,
                99,
                new List<SessionUpgradeSnapshot>(),
                CreateCardsSnapshot(),
                CreateQrSnapshot(),
                out SessionResultSnapshot sameSnapshot);

            Assert.That(first, Is.True);
            Assert.That(duplicate, Is.False);
            Assert.That(sameSnapshot, Is.SameAs(snapshot));
            Assert.That(snapshot.Outcome, Is.EqualTo(SessionOutcome.Defeat));
            Assert.That(snapshot.CompletedWaveCount, Is.EqualTo(2));
        }

        [Test]
        public void Snapshot_ClampsInvalidCountersToSafeRanges()
        {
            SessionResultSnapshot snapshot = new(
                SessionOutcome.Defeat,
                -1d,
                -2,
                -3,
                0,
                99,
                null,
                CreateCardsSnapshot(),
                CreateQrSnapshot());

            Assert.That(snapshot.ActiveGameplayTimeSeconds, Is.Zero);
            Assert.That(snapshot.ConfirmedKills, Is.Zero);
            Assert.That(snapshot.ActualDamage, Is.Zero);
            Assert.That(snapshot.FinalLevel, Is.EqualTo(1));
            Assert.That(snapshot.CompletedWaveCount, Is.EqualTo(6));
            Assert.That(snapshot.Upgrades, Is.Empty);
        }

        [Test]
        public void Reset_ClearsSnapshotTimeAndWaveState()
        {
            SessionResultsCoordinator coordinator = CreateCoordinatorWithTwoWaves();
            coordinator.AddActiveTime(8d, true, true);
            coordinator.TryCreateSnapshot(
                SessionOutcome.Victory,
                1,
                1,
                1,
                new List<SessionUpgradeSnapshot>(),
                CreateCardsSnapshot(),
                CreateQrSnapshot(),
                out _);

            coordinator.Reset();

            Assert.That(coordinator.HasSnapshot, Is.False);
            Assert.That(coordinator.ActiveGameplayTimeSeconds, Is.Zero);
            Assert.That(coordinator.CompletedWaveCount, Is.Zero);
        }

        [Test]
        public void ActualDamage_ExcludesOverkillAndNegativeValues()
        {
            Assert.That(SessionResultsCoordinator.ClampActualDamage(50, 20), Is.EqualTo(20));
            Assert.That(SessionResultsCoordinator.ClampActualDamage(10, 20), Is.EqualTo(10));
            Assert.That(SessionResultsCoordinator.ClampActualDamage(-1, 20), Is.Zero);
            Assert.That(SessionResultsCoordinator.ClampActualDamage(10, -1), Is.Zero);
        }

        [Test]
        public void StartSettingsAndCredits_ReturnToStart()
        {
            SessionShellStateMachine shell = new();

            Assert.That(shell.OpenSettings(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Start));
            Assert.That(shell.OpenCredits(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Start));
        }

        [Test]
        public void PauseSettingsAndConfirmation_ReturnWithoutResume()
        {
            SessionShellStateMachine shell = new();
            shell.StartGame();
            shell.Pause();

            Assert.That(shell.OpenSettings(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Pause));
            Assert.That(shell.OpenRestartConfirmation(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Pause));
        }

        [Test]
        public void PauseFromResult_ContinuesBackToResult()
        {
            SessionShellStateMachine shell = new();
            shell.StartGame();
            shell.ShowResult();

            Assert.That(shell.Pause(), Is.True);
            Assert.That(shell.IsPauseLayerActive, Is.True);
            Assert.That(shell.Continue(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Result));
            Assert.That(shell.IsPauseLayerActive, Is.False);
        }

        [Test]
        public void DevelopmentPages_ReturnOneLevelAtATime()
        {
            SessionShellStateMachine shell = new();
            shell.StartGame();
            shell.Pause();

            Assert.That(shell.OpenDevelopmentHub(), Is.True);
            Assert.That(shell.OpenDevelopmentLog(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.DevelopmentHub));
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Pause));
            Assert.That(shell.IsPauseLayerActive, Is.True);
        }

        [Test]
        public void ResetToStart_ClearsPauseNavigation()
        {
            SessionShellStateMachine shell = new();
            shell.StartGame();
            shell.Pause();
            shell.OpenDevelopmentHub();

            shell.ResetToStart();

            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Start));
            Assert.That(shell.IsPauseLayerActive, Is.False);
            Assert.That(shell.CancelOverlay(), Is.False);
        }

        [Test]
        public void ResultExpandedCardAndExit_ReturnToResult()
        {
            SessionShellStateMachine shell = new();
            shell.StartGame();
            shell.ShowResult();

            Assert.That(shell.OpenExpandedCard(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Result));
            Assert.That(shell.OpenExitConfirmation(), Is.True);
            Assert.That(shell.CancelOverlay(), Is.True);
            Assert.That(shell.Current, Is.EqualTo(SessionShellState.Result));
        }

        private static SessionResultsCoordinator CreateCoordinatorWithTwoWaves()
        {
            SessionResultsCoordinator coordinator = new();
            coordinator.RegisterCompletedWave(1);
            coordinator.RegisterCompletedWave(2);
            return coordinator;
        }

        private static AchievementCardsSnapshot CreateCardsSnapshot()
        {
            return new AchievementCardsSession(
                AchievementCardsConfig.CreateDefault(),
                AchievementCardsCatalog.CreateDefault()).CreateSnapshot();
        }

        private static QrContentSnapshot CreateQrSnapshot()
        {
            return QrContentCatalog.CreateCanonicalFallback().CreateSnapshot();
        }
    }
}
