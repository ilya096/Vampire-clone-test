using System;
using System.Collections.Generic;

namespace LogoSurvivor.ClassLoadout
{
    public sealed class ClassLoadoutSession
    {
        public const int WeaponLevelCap = 9;

        private static readonly LoadoutWeaponSlot[] OrderedSlots =
        {
            LoadoutWeaponSlot.Slot1,
            LoadoutWeaponSlot.Slot2,
            LoadoutWeaponSlot.Slot3,
            LoadoutWeaponSlot.Slot4
        };

        private readonly ClassLoadoutCatalog _catalog;
        private readonly Dictionary<LoadoutWeaponSlot, MutableSlotState> _slots = new();

        public ClassLoadoutSession(ClassLoadoutCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            ClassLoadoutValidationResult validation = catalog.Validate();
            if (validation.IsValid == false)
            {
                throw new ArgumentException(
                    "Class loadout catalog is invalid: " + string.Join(" | ", validation.Errors),
                    nameof(catalog));
            }

            foreach (LoadoutWeaponSlot slot in OrderedSlots)
            {
                _slots[slot] = new MutableSlotState(GetMilestone(slot));
            }

            Reset();
        }

        public ClassLoadoutCatalog Catalog => _catalog;
        public ClassLoadoutSessionState State { get; private set; }
        public PlayerClassId SelectedClassId { get; private set; }
        public bool ClassChoicePending => State == ClassLoadoutSessionState.Active
            && SelectedClassId == PlayerClassId.None;
        public LoadoutWeaponSlot? PendingSlot
        {
            get
            {
                if (State != ClassLoadoutSessionState.Active)
                {
                    return null;
                }

                foreach (LoadoutWeaponSlot slot in OrderedSlots)
                {
                    if (_slots[slot].State == LoadoutSlotState.ChoicePending)
                    {
                        return slot;
                    }
                }

                return null;
            }
        }
        public bool HasMandatoryChoice => ClassChoicePending || PendingSlot.HasValue;

        public PlayerClassDefinition SelectedClass
        {
            get
            {
                return _catalog.TryGetClass(SelectedClassId, out PlayerClassDefinition definition)
                    ? definition
                    : null;
            }
        }

        public IReadOnlyList<LoadoutWeaponDefinition> PendingChoices => PendingSlot.HasValue
            ? _catalog.GetChoices(SelectedClassId, PendingSlot.Value)
            : Array.Empty<LoadoutWeaponDefinition>();

        public ClassLoadoutOperationResult TrySelectClass(PlayerClassId playerClass)
        {
            if (State != ClassLoadoutSessionState.Active)
            {
                return Result(ClassLoadoutOperationStatus.SessionFinished, default, null, "Class choice ignored because the session is finished.");
            }

            if (SelectedClassId != PlayerClassId.None)
            {
                return Result(ClassLoadoutOperationStatus.DuplicateIgnored, default, null, $"Class {SelectedClassId} is already selected.");
            }

            if (playerClass == PlayerClassId.None
                || _catalog.TryGetClass(playerClass, out PlayerClassDefinition selectedClass) == false)
            {
                return Result(ClassLoadoutOperationStatus.InvalidClass, default, null, $"Unknown class ignored: {playerClass}.");
            }

            SelectedClassId = playerClass;
            _slots[LoadoutWeaponSlot.Slot1].State = LoadoutSlotState.ChoicePending;
            return Result(
                ClassLoadoutOperationStatus.ClassSelected,
                LoadoutWeaponSlot.Slot1,
                null,
                $"Class '{selectedClass.StableId}' selected; slot 1 choice is pending.");
        }

        public ClassLoadoutOperationResult TryUnlockSlot(LoadoutWeaponSlot slot)
        {
            if (State != ClassLoadoutSessionState.Active)
            {
                return Result(ClassLoadoutOperationStatus.SessionFinished, slot, null, $"Slot {(int)slot} unlock ignored because the session is finished.");
            }

            if (SelectedClassId == PlayerClassId.None)
            {
                return Result(ClassLoadoutOperationStatus.ClassRequired, slot, null, $"Slot {(int)slot} requires a selected class.");
            }

            if (_slots.TryGetValue(slot, out MutableSlotState slotState) == false)
            {
                return Result(ClassLoadoutOperationStatus.InvalidSlot, slot, null, $"Unknown loadout slot ignored: {(int)slot}.");
            }

            if (slotState.State != LoadoutSlotState.Locked)
            {
                return Result(ClassLoadoutOperationStatus.DuplicateIgnored, slot, slotState.SelectedWeapon, $"Duplicate unlock ignored for slot {(int)slot} in state {slotState.State}.");
            }

            slotState.State = LoadoutSlotState.ChoicePending;
            return Result(ClassLoadoutOperationStatus.SlotUnlocked, slot, null, $"Slot {(int)slot} unlocked; weapon choice is pending.");
        }

        public ClassLoadoutOperationResult TrySelectPendingWeapon(int optionIndex)
        {
            if (PendingSlot.HasValue == false)
            {
                return Result(
                    State == ClassLoadoutSessionState.Finished
                        ? ClassLoadoutOperationStatus.SessionFinished
                        : ClassLoadoutOperationStatus.SlotNotPending,
                    default,
                    null,
                    "No loadout weapon choice is pending.");
            }

            IReadOnlyList<LoadoutWeaponDefinition> choices = PendingChoices;
            if (optionIndex < 1 || optionIndex > choices.Count)
            {
                return Result(ClassLoadoutOperationStatus.InvalidChoice, PendingSlot.Value, null, $"Choice index must be inside 1..{choices.Count}: {optionIndex}.");
            }

            return TrySelectWeapon(PendingSlot.Value, choices[optionIndex - 1].Id);
        }

        public ClassLoadoutOperationResult TrySelectWeapon(
            LoadoutWeaponSlot slot,
            string weaponId)
        {
            if (State != ClassLoadoutSessionState.Active)
            {
                return Result(ClassLoadoutOperationStatus.SessionFinished, slot, null, "Weapon choice ignored because the session is finished.");
            }

            if (SelectedClassId == PlayerClassId.None)
            {
                return Result(ClassLoadoutOperationStatus.ClassRequired, slot, null, "Weapon choice requires a selected class.");
            }

            if (_slots.TryGetValue(slot, out MutableSlotState slotState) == false)
            {
                return Result(ClassLoadoutOperationStatus.InvalidSlot, slot, null, $"Unknown loadout slot ignored: {(int)slot}.");
            }

            if (slotState.State == LoadoutSlotState.Locked)
            {
                return Result(ClassLoadoutOperationStatus.SlotLocked, slot, null, $"Slot {(int)slot} is still locked.");
            }

            if (slotState.State == LoadoutSlotState.Selected)
            {
                return Result(ClassLoadoutOperationStatus.DuplicateIgnored, slot, slotState.SelectedWeapon, $"Slot {(int)slot} already contains '{slotState.SelectedWeapon.Id}'.");
            }

            IReadOnlyList<LoadoutWeaponDefinition> choices = _catalog.GetChoices(SelectedClassId, slot);
            LoadoutWeaponDefinition selectedWeapon = null;
            foreach (LoadoutWeaponDefinition choice in choices)
            {
                if (string.Equals(choice.Id, weaponId, StringComparison.Ordinal))
                {
                    selectedWeapon = choice;
                    break;
                }
            }

            if (selectedWeapon == null)
            {
                return Result(ClassLoadoutOperationStatus.InvalidChoice, slot, null, $"Weapon '{weaponId}' is not available for class {SelectedClassId}, slot {(int)slot}.");
            }

            slotState.SelectedWeapon = selectedWeapon;
            slotState.Level = 0;
            slotState.State = LoadoutSlotState.Selected;
            return Result(ClassLoadoutOperationStatus.WeaponSelected, slot, selectedWeapon, $"Weapon '{selectedWeapon.Id}' selected for slot {(int)slot}.");
        }

        public ClassLoadoutOperationResult TrySetWeaponLevelForDebug(
            LoadoutWeaponSlot slot,
            int level)
        {
            if (State != ClassLoadoutSessionState.Active)
            {
                return Result(ClassLoadoutOperationStatus.SessionFinished, slot, null, "Debug level change ignored because the session is finished.");
            }

            if (level < 0 || level > WeaponLevelCap)
            {
                return Result(ClassLoadoutOperationStatus.InvalidLevel, slot, null, $"Weapon level must be inside 0..{WeaponLevelCap}: {level}.");
            }

            if (_slots.TryGetValue(slot, out MutableSlotState slotState) == false)
            {
                return Result(ClassLoadoutOperationStatus.InvalidSlot, slot, null, $"Unknown loadout slot ignored: {(int)slot}.");
            }

            if (slotState.State != LoadoutSlotState.Selected)
            {
                return Result(ClassLoadoutOperationStatus.SlotNotSelected, slot, null, $"Slot {(int)slot} has no selected weapon.");
            }

            slotState.Level = level;
            return Result(ClassLoadoutOperationStatus.LevelChanged, slot, slotState.SelectedWeapon, $"Slot {(int)slot} debug level set to {level}/{WeaponLevelCap}.");
        }

        public ClassLoadoutSlotSnapshot GetSlotSnapshot(LoadoutWeaponSlot slot)
        {
            if (_slots.TryGetValue(slot, out MutableSlotState state) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown loadout slot.");
            }

            return new ClassLoadoutSlotSnapshot(
                slot,
                state.Milestone,
                state.State,
                state.SelectedWeapon,
                state.Level);
        }

        public ClassLoadoutSnapshot CreateSnapshot()
        {
            List<ClassLoadoutSlotSnapshot> slots = new(OrderedSlots.Length);
            foreach (LoadoutWeaponSlot slot in OrderedSlots)
            {
                slots.Add(GetSlotSnapshot(slot));
            }

            return new ClassLoadoutSnapshot(SelectedClass, slots, State);
        }

        public bool TryFinish()
        {
            if (State != ClassLoadoutSessionState.Active)
            {
                return false;
            }

            foreach (MutableSlotState slot in _slots.Values)
            {
                if (slot.State == LoadoutSlotState.ChoicePending)
                {
                    slot.State = LoadoutSlotState.Locked;
                }
            }

            State = ClassLoadoutSessionState.Finished;
            return true;
        }

        public void Reset()
        {
            State = ClassLoadoutSessionState.Active;
            SelectedClassId = PlayerClassId.None;
            foreach (MutableSlotState slot in _slots.Values)
            {
                slot.State = LoadoutSlotState.Locked;
                slot.SelectedWeapon = null;
                slot.Level = 0;
            }
        }

        private static LoadoutUnlockMilestone GetMilestone(LoadoutWeaponSlot slot)
        {
            return slot switch
            {
                LoadoutWeaponSlot.Slot1 => LoadoutUnlockMilestone.SessionStart,
                LoadoutWeaponSlot.Slot2 => LoadoutUnlockMilestone.ArenaR,
                LoadoutWeaponSlot.Slot3 => LoadoutUnlockMilestone.ArenaO,
                LoadoutWeaponSlot.Slot4 => LoadoutUnlockMilestone.BossSpawned,
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown loadout slot.")
            };
        }

        private static ClassLoadoutOperationResult Result(
            ClassLoadoutOperationStatus status,
            LoadoutWeaponSlot slot,
            LoadoutWeaponDefinition weapon,
            string message)
        {
            return new ClassLoadoutOperationResult(status, slot, weapon, message);
        }

        private sealed class MutableSlotState
        {
            public MutableSlotState(LoadoutUnlockMilestone milestone)
            {
                Milestone = milestone;
            }

            public LoadoutUnlockMilestone Milestone { get; }
            public LoadoutSlotState State;
            public LoadoutWeaponDefinition SelectedWeapon;
            public int Level;
        }
    }
}
