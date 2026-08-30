using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LogoSurvivor.AchievementCards
{
    public enum AchievementCardKind
    {
        Direction,
        Stage
    }

    public enum AchievementWaveSlot
    {
        ArenaPWave1 = 1,
        ArenaPWave2 = 2,
        ArenaRWave1 = 3,
        ArenaRWave2 = 4,
        ArenaOWave1 = 5,
        ArenaOWave2 = 6
    }

    public enum AchievementMaterialKind
    {
        Placeholder,
        Real
    }

    public enum AchievementRightsStatus
    {
        NotApproved,
        Approved
    }

    public enum AchievementAwardStatus
    {
        Awarded,
        DuplicateIgnored,
        FeatureDisabled,
        SessionFinished,
        UnknownWaveSlot
    }

    public enum AchievementSessionState
    {
        Active,
        Victory,
        Defeat
    }

    public sealed class AchievementCardImageDefinition
    {
        public AchievementCardImageDefinition(
            AchievementMaterialKind materialKind,
            AchievementRightsStatus rightsStatus,
            string realAssetId,
            string placeholderAssetId,
            string altTextKey,
            string source,
            string author)
        {
            MaterialKind = materialKind;
            RightsStatus = rightsStatus;
            RealAssetId = realAssetId ?? string.Empty;
            PlaceholderAssetId = placeholderAssetId ?? string.Empty;
            AltTextKey = altTextKey ?? string.Empty;
            Source = source ?? string.Empty;
            Author = author ?? string.Empty;
        }

        public AchievementMaterialKind MaterialKind { get; }
        public AchievementRightsStatus RightsStatus { get; }
        public string RealAssetId { get; }
        public string PlaceholderAssetId { get; }
        public string AltTextKey { get; }
        public string Source { get; }
        public string Author { get; }

        public AchievementResolvedImage Resolve()
        {
            bool canUseRealAsset = MaterialKind == AchievementMaterialKind.Real
                && RightsStatus == AchievementRightsStatus.Approved
                && string.IsNullOrWhiteSpace(RealAssetId) == false;

            return canUseRealAsset
                ? new AchievementResolvedImage(RealAssetId, false)
                : new AchievementResolvedImage(PlaceholderAssetId, MaterialKind == AchievementMaterialKind.Real);
        }
    }

    public readonly struct AchievementResolvedImage
    {
        public AchievementResolvedImage(string assetId, bool usedRightsFallback)
        {
            AssetId = assetId ?? string.Empty;
            UsedRightsFallback = usedRightsFallback;
        }

        public string AssetId { get; }
        public bool UsedRightsFallback { get; }
    }

    public sealed class AchievementCardDefinition
    {
        public AchievementCardDefinition(
            string id,
            AchievementCardKind kind,
            int order,
            AchievementWaveSlot waveSlot,
            string titleKey,
            string introKey,
            string point1Key,
            string point2Key,
            AchievementCardImageDefinition image)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Order = order;
            WaveSlot = waveSlot;
            TitleKey = titleKey ?? string.Empty;
            IntroKey = introKey ?? string.Empty;
            Point1Key = point1Key ?? string.Empty;
            Point2Key = point2Key ?? string.Empty;
            Image = image ?? throw new ArgumentNullException(nameof(image));
        }

        public string Id { get; }
        public AchievementCardKind Kind { get; }
        public int Order { get; }
        public AchievementWaveSlot WaveSlot { get; }
        public string TitleKey { get; }
        public string IntroKey { get; }
        public string Point1Key { get; }
        public string Point2Key { get; }
        public AchievementCardImageDefinition Image { get; }
        public string TypeLocalizationKey => Kind == AchievementCardKind.Direction
            ? "achievement_cards.type_direction"
            : "achievement_cards.type_stage";
    }

    public sealed class AchievementCardsConfig
    {
        public AchievementCardsConfig(
            bool enabled,
            float introAutoCloseSeconds,
            float cardContinueDelaySeconds)
        {
            if (introAutoCloseSeconds < 1f || introAutoCloseSeconds > 30f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(introAutoCloseSeconds),
                    "Intro auto-close must be between 1 and 30 seconds.");
            }

            if (cardContinueDelaySeconds < 0f || cardContinueDelaySeconds > 3f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cardContinueDelaySeconds),
                    "Card continue delay must be between 0 and 3 seconds.");
            }

            Enabled = enabled;
            IntroAutoCloseSeconds = introAutoCloseSeconds;
            CardContinueDelaySeconds = cardContinueDelaySeconds;
        }

        public bool Enabled { get; }
        public float IntroAutoCloseSeconds { get; }
        public float CardContinueDelaySeconds { get; }

        public static AchievementCardsConfig CreateDefault(bool enabled = true)
        {
            return new AchievementCardsConfig(enabled, 10f, 1f);
        }
    }

    public readonly struct AchievementAwardResult
    {
        public AchievementAwardResult(
            AchievementAwardStatus status,
            AchievementCardDefinition card,
            string diagnosticMessage)
        {
            Status = status;
            Card = card;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public AchievementAwardStatus Status { get; }
        public AchievementCardDefinition Card { get; }
        public string DiagnosticMessage { get; }
        public bool ChangedCollection => Status == AchievementAwardStatus.Awarded;
    }

    public sealed class AchievementCardSlotState
    {
        public AchievementCardSlotState(AchievementCardDefinition definition, bool obtained)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Obtained = obtained;
        }

        public AchievementCardDefinition Definition { get; }
        public bool Obtained { get; }
    }

    public sealed class AchievementCardsSnapshot
    {
        public AchievementCardsSnapshot(IList<AchievementCardSlotState> slots)
        {
            if (slots == null)
            {
                throw new ArgumentNullException(nameof(slots));
            }

            Slots = new ReadOnlyCollection<AchievementCardSlotState>(
                new List<AchievementCardSlotState>(slots));

            int obtainedCount = 0;
            foreach (AchievementCardSlotState slot in Slots)
            {
                if (slot.Obtained)
                {
                    obtainedCount++;
                }
            }

            ObtainedCount = obtainedCount;
        }

        public IReadOnlyList<AchievementCardSlotState> Slots { get; }
        public int ObtainedCount { get; }
        public int TotalCount => Slots.Count;
    }

    public sealed class AchievementCardsValidationResult
    {
        public AchievementCardsValidationResult(IList<string> errors)
        {
            Errors = new ReadOnlyCollection<string>(new List<string>(errors));
        }

        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }
}
