using System;
using System.Collections.Generic;

namespace LogoSurvivor.AchievementCards
{
    public sealed class AchievementCardsSession
    {
        private readonly AchievementCardsConfig _config;
        private readonly AchievementCardsCatalog _catalog;
        private readonly HashSet<AchievementWaveSlot> _registeredWaveSlots = new();
        private readonly HashSet<string> _obtainedCardIds = new(StringComparer.Ordinal);
        private readonly Queue<AchievementCardDefinition> _presentationQueue = new();
        private bool _introPending;

        public AchievementCardsSession(
            AchievementCardsConfig config,
            AchievementCardsCatalog catalog)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            AchievementCardsValidationResult validation = catalog.ValidateForRuntime();
            if (validation.IsValid == false)
            {
                throw new ArgumentException(
                    "Achievement cards catalog is invalid: " + string.Join(" | ", validation.Errors),
                    nameof(catalog));
            }

            Reset();
        }

        public AchievementSessionState State { get; private set; }
        public AchievementCardsConfig Config => _config;
        public AchievementCardsCatalog Catalog => _catalog;
        public int ObtainedCount => _obtainedCardIds.Count;
        public int PendingPresentationCount => _presentationQueue.Count;
        public bool IntroPending => _config.Enabled && State == AchievementSessionState.Active && _introPending;

        public bool DismissIntro()
        {
            if (IntroPending == false)
            {
                return false;
            }

            _introPending = false;
            return true;
        }

        public AchievementAwardResult RegisterWaveCompleted(AchievementWaveSlot waveSlot)
        {
            if (_config.Enabled == false)
            {
                return Result(
                    AchievementAwardStatus.FeatureDisabled,
                    null,
                    $"Wave slot '{waveSlot}' ignored because achievement_cards is disabled.");
            }

            if (State != AchievementSessionState.Active)
            {
                return Result(
                    AchievementAwardStatus.SessionFinished,
                    null,
                    $"Wave slot '{waveSlot}' ignored because session state is {State}.");
            }

            if (_catalog.TryGetByWaveSlot(waveSlot, out AchievementCardDefinition card) == false)
            {
                return Result(
                    AchievementAwardStatus.UnknownWaveSlot,
                    null,
                    $"Wave slot '{waveSlot}' has no approved achievement card mapping.");
            }

            if (_registeredWaveSlots.Add(waveSlot) == false || _obtainedCardIds.Add(card.Id) == false)
            {
                return Result(
                    AchievementAwardStatus.DuplicateIgnored,
                    card,
                    $"Duplicate wave completion ignored for '{waveSlot}' and card '{card.Id}'.");
            }

            _presentationQueue.Enqueue(card);
            return Result(
                AchievementAwardStatus.Awarded,
                card,
                $"Achievement card '{card.Id}' awarded for '{waveSlot}'.");
        }

        public bool TryTakeNextForPresentation(
            bool upgradeChoiceActive,
            out AchievementCardDefinition card)
        {
            card = null;
            if (_config.Enabled == false
                || State != AchievementSessionState.Active
                || upgradeChoiceActive
                || _presentationQueue.Count == 0)
            {
                return false;
            }

            card = _presentationQueue.Dequeue();
            return true;
        }

        public bool TryFinish(AchievementSessionState finalState)
        {
            if (finalState is not (AchievementSessionState.Victory or AchievementSessionState.Defeat))
            {
                throw new ArgumentException(
                    "Achievement cards outcome must be Victory or Defeat. Restart and confirmed exit use Reset().",
                    nameof(finalState));
            }

            if (State != AchievementSessionState.Active)
            {
                return false;
            }

            State = finalState;
            _presentationQueue.Clear();
            _introPending = false;
            return true;
        }

        public bool IsObtained(string cardId)
        {
            return string.IsNullOrWhiteSpace(cardId) == false && _obtainedCardIds.Contains(cardId);
        }

        public AchievementCardsSnapshot CreateSnapshot()
        {
            List<AchievementCardSlotState> slots = new(AchievementCardsCatalog.RequiredCardCount);
            foreach (AchievementCardDefinition definition in _catalog.Cards)
            {
                slots.Add(new AchievementCardSlotState(
                    definition,
                    _obtainedCardIds.Contains(definition.Id)));
            }

            return new AchievementCardsSnapshot(slots);
        }

        public void Reset()
        {
            _registeredWaveSlots.Clear();
            _obtainedCardIds.Clear();
            _presentationQueue.Clear();
            State = AchievementSessionState.Active;
            _introPending = _config.Enabled;
        }

        private static AchievementAwardResult Result(
            AchievementAwardStatus status,
            AchievementCardDefinition card,
            string message)
        {
            return new AchievementAwardResult(status, card, message);
        }
    }
}
