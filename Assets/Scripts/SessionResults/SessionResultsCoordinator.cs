using System;
using System.Collections.Generic;
using LogoSurvivor.AchievementCards;
using LogoSurvivor.QrContent;

namespace LogoSurvivor.SessionResults
{
    public sealed class SessionResultsCoordinator
    {
        private readonly HashSet<int> _completedWaveSlots = new();
        private double _activeGameplayTimeSeconds;

        public SessionResultSnapshot Snapshot { get; private set; }
        public bool HasSnapshot => Snapshot != null;
        public double ActiveGameplayTimeSeconds => _activeGameplayTimeSeconds;
        public int CompletedWaveCount => _completedWaveSlots.Count;

        public void AddActiveTime(double deltaSeconds, bool gameplayActive, bool focused)
        {
            if (gameplayActive == false || focused == false || deltaSeconds <= 0d || HasSnapshot)
            {
                return;
            }

            _activeGameplayTimeSeconds += deltaSeconds;
        }

        public bool RegisterCompletedWave(int waveSlot)
        {
            if (HasSnapshot || waveSlot < 1 || waveSlot > 6)
            {
                return false;
            }

            return _completedWaveSlots.Add(waveSlot);
        }

        public bool TryCreateSnapshot(
            SessionOutcome outcome,
            int confirmedKills,
            long actualDamage,
            int finalLevel,
            IList<SessionUpgradeSnapshot> upgrades,
            AchievementCardsSnapshot achievementCards,
            QrContentSnapshot qrContent,
            out SessionResultSnapshot snapshot)
        {
            if (Snapshot != null)
            {
                snapshot = Snapshot;
                return false;
            }

            Snapshot = new SessionResultSnapshot(
                outcome,
                _activeGameplayTimeSeconds,
                confirmedKills,
                actualDamage,
                finalLevel,
                _completedWaveSlots.Count,
                upgrades,
                achievementCards,
                qrContent);
            snapshot = Snapshot;
            return true;
        }

        public void Reset()
        {
            _completedWaveSlots.Clear();
            _activeGameplayTimeSeconds = 0d;
            Snapshot = null;
        }

        public static int ClampActualDamage(int requestedDamage, int targetHpBeforeHit)
        {
            return Math.Max(0, Math.Min(Math.Max(0, requestedDamage), Math.Max(0, targetHpBeforeHit)));
        }
    }
}
