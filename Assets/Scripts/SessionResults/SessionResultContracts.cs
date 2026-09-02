using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LogoSurvivor.AchievementCards;
using LogoSurvivor.QrContent;

namespace LogoSurvivor.SessionResults
{
    public enum SessionOutcome
    {
        Victory,
        Defeat
    }

    public enum SessionShellState
    {
        Start,
        Gameplay,
        Pause,
        Settings,
        Credits,
        DevelopmentHub,
        DevelopmentParameters,
        DevelopmentSpecialCards,
        DevelopmentLog,
        Result,
        ExpandedCard,
        ConfirmRestart,
        ConfirmExit
    }

    public sealed class SessionUpgradeSnapshot
    {
        public SessionUpgradeSnapshot(string title, int level)
        {
            Title = title ?? string.Empty;
            Level = Math.Max(1, level);
        }

        public string Title { get; }
        public int Level { get; }
    }

    public sealed class SessionResultSnapshot
    {
        public SessionResultSnapshot(
            SessionOutcome outcome,
            double activeGameplayTimeSeconds,
            int confirmedKills,
            long actualDamage,
            int finalLevel,
            int completedWaveCount,
            IList<SessionUpgradeSnapshot> upgrades,
            AchievementCardsSnapshot achievementCards,
            QrContentSnapshot qrContent)
        {
            Outcome = outcome;
            ActiveGameplayTimeSeconds = Math.Max(0d, activeGameplayTimeSeconds);
            ConfirmedKills = Math.Max(0, confirmedKills);
            ActualDamage = Math.Max(0L, actualDamage);
            FinalLevel = Math.Max(1, finalLevel);
            CompletedWaveCount = Math.Max(0, Math.Min(6, completedWaveCount));
            Upgrades = new ReadOnlyCollection<SessionUpgradeSnapshot>(
                new List<SessionUpgradeSnapshot>(upgrades ?? Array.Empty<SessionUpgradeSnapshot>()));
            AchievementCards = achievementCards ?? throw new ArgumentNullException(nameof(achievementCards));
            QrContent = qrContent ?? throw new ArgumentNullException(nameof(qrContent));
        }

        public SessionOutcome Outcome { get; }
        public double ActiveGameplayTimeSeconds { get; }
        public int ConfirmedKills { get; }
        public long ActualDamage { get; }
        public int FinalLevel { get; }
        public int CompletedWaveCount { get; }
        public IReadOnlyList<SessionUpgradeSnapshot> Upgrades { get; }
        public AchievementCardsSnapshot AchievementCards { get; }
        public QrContentSnapshot QrContent { get; }
    }
}
