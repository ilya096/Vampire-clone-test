using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LogoSurvivor.ClassLoadout
{
    public enum PlayerClassId
    {
        None,
        GameDesigner,
        Artist,
        Programmer
    }

    public enum LoadoutWeaponSlot
    {
        Slot1 = 1,
        Slot2 = 2,
        Slot3 = 3,
        Slot4 = 4
    }

    public enum LoadoutSlotState
    {
        Locked,
        ChoicePending,
        Selected
    }

    public enum LoadoutUnlockMilestone
    {
        SessionStart,
        ArenaR,
        ArenaO,
        BossSpawned
    }

    public enum ClassLoadoutSessionState
    {
        Active,
        Finished
    }

    public enum ClassLoadoutOperationStatus
    {
        ClassSelected,
        SlotUnlocked,
        WeaponSelected,
        LevelChanged,
        DuplicateIgnored,
        SessionFinished,
        ClassRequired,
        InvalidClass,
        InvalidSlot,
        SlotLocked,
        SlotNotPending,
        SlotNotSelected,
        InvalidChoice,
        InvalidLevel
    }

    public sealed class LoadoutWeaponDefinition
    {
        public LoadoutWeaponDefinition(
            string id,
            PlayerClassId playerClass,
            LoadoutWeaponSlot slot,
            int optionIndex,
            string displayName,
            string archetypeId,
            string localizationKey)
        {
            Id = id ?? string.Empty;
            PlayerClass = playerClass;
            Slot = slot;
            OptionIndex = optionIndex;
            DisplayName = displayName ?? string.Empty;
            ArchetypeId = archetypeId ?? string.Empty;
            LocalizationKey = localizationKey ?? string.Empty;
        }

        public string Id { get; }
        public PlayerClassId PlayerClass { get; }
        public LoadoutWeaponSlot Slot { get; }
        public int OptionIndex { get; }
        public string DisplayName { get; }
        public string ArchetypeId { get; }
        public string LocalizationKey { get; }
    }

    public sealed class PlayerClassDefinition
    {
        public PlayerClassDefinition(
            PlayerClassId id,
            string stableId,
            string displayName,
            string localizationKey)
        {
            Id = id;
            StableId = stableId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            LocalizationKey = localizationKey ?? string.Empty;
        }

        public PlayerClassId Id { get; }
        public string StableId { get; }
        public string DisplayName { get; }
        public string LocalizationKey { get; }
    }

    public readonly struct ClassLoadoutSlotSnapshot
    {
        public ClassLoadoutSlotSnapshot(
            LoadoutWeaponSlot slot,
            LoadoutUnlockMilestone milestone,
            LoadoutSlotState state,
            LoadoutWeaponDefinition selectedWeapon,
            int level)
        {
            Slot = slot;
            Milestone = milestone;
            State = state;
            SelectedWeapon = selectedWeapon;
            Level = level;
        }

        public LoadoutWeaponSlot Slot { get; }
        public LoadoutUnlockMilestone Milestone { get; }
        public LoadoutSlotState State { get; }
        public LoadoutWeaponDefinition SelectedWeapon { get; }
        public int Level { get; }
    }

    public sealed class ClassLoadoutSnapshot
    {
        public ClassLoadoutSnapshot(
            PlayerClassDefinition selectedClass,
            IList<ClassLoadoutSlotSnapshot> slots,
            ClassLoadoutSessionState state)
        {
            SelectedClass = selectedClass;
            Slots = new ReadOnlyCollection<ClassLoadoutSlotSnapshot>(
                new List<ClassLoadoutSlotSnapshot>(slots ?? throw new ArgumentNullException(nameof(slots))));
            State = state;
        }

        public PlayerClassDefinition SelectedClass { get; }
        public IReadOnlyList<ClassLoadoutSlotSnapshot> Slots { get; }
        public ClassLoadoutSessionState State { get; }
    }

    public readonly struct ClassLoadoutOperationResult
    {
        public ClassLoadoutOperationResult(
            ClassLoadoutOperationStatus status,
            LoadoutWeaponSlot slot,
            LoadoutWeaponDefinition weapon,
            string diagnosticMessage)
        {
            Status = status;
            Slot = slot;
            Weapon = weapon;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public ClassLoadoutOperationStatus Status { get; }
        public LoadoutWeaponSlot Slot { get; }
        public LoadoutWeaponDefinition Weapon { get; }
        public string DiagnosticMessage { get; }
        public bool ChangedState => Status is ClassLoadoutOperationStatus.ClassSelected
            or ClassLoadoutOperationStatus.SlotUnlocked
            or ClassLoadoutOperationStatus.WeaponSelected
            or ClassLoadoutOperationStatus.LevelChanged;
    }

    public sealed class ClassLoadoutValidationResult
    {
        public ClassLoadoutValidationResult(IList<string> errors)
        {
            Errors = new ReadOnlyCollection<string>(new List<string>(errors));
        }

        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }
}
