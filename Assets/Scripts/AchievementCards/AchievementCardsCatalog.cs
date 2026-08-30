using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LogoSurvivor.AchievementCards
{
    public sealed class AchievementCardsCatalog
    {
        public const int RequiredCardCount = 6;

        private static readonly string[] ApprovedIds =
        {
            "direction_game_design_guild",
            "stage_preproduction",
            "direction_art_club",
            "stage_production",
            "direction_programmers_club",
            "stage_release_postproduction"
        };

        private static readonly AchievementCardKind[] ApprovedKinds =
        {
            AchievementCardKind.Direction,
            AchievementCardKind.Stage,
            AchievementCardKind.Direction,
            AchievementCardKind.Stage,
            AchievementCardKind.Direction,
            AchievementCardKind.Stage
        };

        private readonly ReadOnlyCollection<AchievementCardDefinition> _cards;
        private readonly Dictionary<AchievementWaveSlot, AchievementCardDefinition> _cardsByWaveSlot;

        public AchievementCardsCatalog(IList<AchievementCardDefinition> cards)
        {
            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            List<AchievementCardDefinition> copy = new(cards);
            copy.Sort((left, right) =>
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return 1;
                }

                return right == null ? -1 : left.Order.CompareTo(right.Order);
            });
            _cards = new ReadOnlyCollection<AchievementCardDefinition>(copy);
            _cardsByWaveSlot = new Dictionary<AchievementWaveSlot, AchievementCardDefinition>();

            foreach (AchievementCardDefinition card in copy)
            {
                if (card != null && _cardsByWaveSlot.ContainsKey(card.WaveSlot) == false)
                {
                    _cardsByWaveSlot.Add(card.WaveSlot, card);
                }
            }
        }

        public IReadOnlyList<AchievementCardDefinition> Cards => _cards;

        public static AchievementCardsCatalog CreateDefault()
        {
            const string directionPlaceholder = "achievement_cards/placeholder_direction";
            const string stagePlaceholder = "achievement_cards/placeholder_stage";

            return new AchievementCardsCatalog(new[]
            {
                CreatePlaceholderDefinition(
                    "direction_game_design_guild",
                    AchievementCardKind.Direction,
                    1,
                    AchievementWaveSlot.ArenaPWave1,
                    directionPlaceholder),
                CreatePlaceholderDefinition(
                    "stage_preproduction",
                    AchievementCardKind.Stage,
                    2,
                    AchievementWaveSlot.ArenaPWave2,
                    stagePlaceholder),
                CreatePlaceholderDefinition(
                    "direction_art_club",
                    AchievementCardKind.Direction,
                    3,
                    AchievementWaveSlot.ArenaRWave1,
                    directionPlaceholder),
                CreatePlaceholderDefinition(
                    "stage_production",
                    AchievementCardKind.Stage,
                    4,
                    AchievementWaveSlot.ArenaRWave2,
                    stagePlaceholder),
                CreatePlaceholderDefinition(
                    "direction_programmers_club",
                    AchievementCardKind.Direction,
                    5,
                    AchievementWaveSlot.ArenaOWave1,
                    directionPlaceholder),
                CreatePlaceholderDefinition(
                    "stage_release_postproduction",
                    AchievementCardKind.Stage,
                    6,
                    AchievementWaveSlot.ArenaOWave2,
                    stagePlaceholder)
            });
        }

        public bool TryGetByWaveSlot(
            AchievementWaveSlot waveSlot,
            out AchievementCardDefinition definition)
        {
            return _cardsByWaveSlot.TryGetValue(waveSlot, out definition);
        }

        public AchievementCardsValidationResult ValidateForRuntime()
        {
            return Validate(includeReleaseRightsGate: false);
        }

        public AchievementCardsValidationResult ValidateForRelease()
        {
            return Validate(includeReleaseRightsGate: true);
        }

        private AchievementCardsValidationResult Validate(bool includeReleaseRightsGate)
        {
            List<string> errors = new();
            if (_cards.Count != RequiredCardCount)
            {
                errors.Add($"Catalog must contain exactly {RequiredCardCount} cards; found {_cards.Count}.");
            }

            HashSet<string> ids = new(StringComparer.Ordinal);
            HashSet<int> orders = new();
            HashSet<AchievementWaveSlot> waveSlots = new();

            foreach (AchievementCardDefinition card in _cards)
            {
                if (card == null)
                {
                    errors.Add("Catalog contains a null card definition.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(card.Id) || ids.Add(card.Id) == false)
                {
                    errors.Add($"Card ID must be non-empty and unique: '{card.Id}'.");
                }

                if (card.Order < 1 || card.Order > RequiredCardCount || orders.Add(card.Order) == false)
                {
                    errors.Add($"Card order must be unique and inside 1..{RequiredCardCount}: {card.Order}.");
                }
                else
                {
                    int approvedIndex = card.Order - 1;
                    if (string.Equals(card.Id, ApprovedIds[approvedIndex], StringComparison.Ordinal) == false)
                    {
                        errors.Add($"Order {card.Order} must use approved card ID '{ApprovedIds[approvedIndex]}'; found '{card.Id}'.");
                    }

                    if (card.Kind != ApprovedKinds[approvedIndex])
                    {
                        errors.Add($"Card '{card.Id}' has kind {card.Kind}; approved kind is {ApprovedKinds[approvedIndex]}.");
                    }

                    if ((int)card.WaveSlot != card.Order)
                    {
                        errors.Add($"Card '{card.Id}' order {card.Order} must map to wave slot value {card.Order}; found {(int)card.WaveSlot}.");
                    }
                }

                if (Enum.IsDefined(typeof(AchievementWaveSlot), card.WaveSlot) == false
                    || waveSlots.Add(card.WaveSlot) == false)
                {
                    errors.Add($"Wave slot must be a unique approved slot: {card.WaveSlot}.");
                }

                ValidateRequiredTextKey(card.TitleKey, card.Id, "title", errors);
                ValidateRequiredTextKey(card.IntroKey, card.Id, "intro", errors);
                ValidateRequiredTextKey(card.Point1Key, card.Id, "point_1", errors);
                ValidateRequiredTextKey(card.Point2Key, card.Id, "point_2", errors);

                if (string.IsNullOrWhiteSpace(card.Image.PlaceholderAssetId))
                {
                    errors.Add($"Card '{card.Id}' has no safe placeholder asset ID.");
                }

                if (string.IsNullOrWhiteSpace(card.Image.AltTextKey))
                {
                    errors.Add($"Card '{card.Id}' has no alt-text localization key.");
                }

                if (includeReleaseRightsGate
                    && card.Image.MaterialKind == AchievementMaterialKind.Real)
                {
                    if (string.IsNullOrWhiteSpace(card.Image.RealAssetId))
                    {
                        errors.Add($"Real material for '{card.Id}' has no asset ID.");
                    }

                    if (card.Image.RightsStatus != AchievementRightsStatus.Approved)
                    {
                        errors.Add($"Real material for '{card.Id}' is not approved for release.");
                    }

                    if (string.IsNullOrWhiteSpace(card.Image.Source)
                        || string.IsNullOrWhiteSpace(card.Image.Author))
                    {
                        errors.Add($"Real material for '{card.Id}' requires source and author metadata.");
                    }
                }
            }

            for (int order = 1; order <= RequiredCardCount; order++)
            {
                if (orders.Contains(order) == false)
                {
                    errors.Add($"Catalog is missing order {order}.");
                }
            }

            return new AchievementCardsValidationResult(errors);
        }

        private static AchievementCardDefinition CreatePlaceholderDefinition(
            string id,
            AchievementCardKind kind,
            int order,
            AchievementWaveSlot waveSlot,
            string placeholderAssetId)
        {
            string keyPrefix = $"achievement_cards.{id}";
            return new AchievementCardDefinition(
                id,
                kind,
                order,
                waveSlot,
                $"{keyPrefix}_title",
                $"{keyPrefix}_intro",
                $"{keyPrefix}_point_1",
                $"{keyPrefix}_point_2",
                new AchievementCardImageDefinition(
                    AchievementMaterialKind.Placeholder,
                    AchievementRightsStatus.NotApproved,
                    string.Empty,
                    placeholderAssetId,
                    $"{keyPrefix}_alt",
                    string.Empty,
                    string.Empty));
        }

        private static void ValidateRequiredTextKey(
            string key,
            string cardId,
            string field,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add($"Card '{cardId}' has no {field} localization key.");
            }
        }
    }
}
