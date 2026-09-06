using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LogoSurvivor.ClassLoadout
{
    public sealed class ClassLoadoutCatalog
    {
        public const int RequiredClassCount = 3;
        public const int RequiredSlotCount = 4;
        public const int RequiredChoicesPerSlot = 3;
        public const int RequiredWeaponCount = RequiredClassCount * RequiredSlotCount * RequiredChoicesPerSlot;

        private static readonly PlayerClassId[] ApprovedClasses =
        {
            PlayerClassId.GameDesigner,
            PlayerClassId.Artist,
            PlayerClassId.Programmer
        };

        private static readonly string[] ApprovedClassStableIds =
        {
            "game_designer",
            "artist",
            "programmer"
        };

        private static readonly string[] ApprovedClassDisplayNames =
        {
            "Геймдизайнер",
            "Художник",
            "Программист"
        };

        private static readonly string[,] ApprovedArchetypes =
        {
            { "auto_pistol", "revolver", "sawed_off" },
            { "light_machine_gun", "grenade_launcher", "dmr" },
            { "flamethrower", "acid_pools", "chain_lightning" },
            { "turret", "homing_volley", "protective_field" }
        };

        private static readonly string[,,] ApprovedWeaponNames =
        {
            {
                { "Игровой цикл", "Ключевая механика", "Игровое ощущение" },
                { "Баланс", "Мета", "Системный дизайн" },
                { "Прогрессия", "Level Design", "Encounter Design" },
                { "Плейтест", "Аналитика", "Полиш" }
            },
            {
                { "Ритм", "Контраст", "Силуэт" },
                { "Стилизация", "Cozy", "Реализм" },
                { "Blender", "ZBrush", "Maya" },
                { "Key Art", "Трейлер", "Store Capsule" }
            },
            {
                { "Update Loop", "State Machine", "Event Broadcast" },
                { "Job System", "Message Bus", "API Contract" },
                { "Profiler", "Garbage Collector", "Debugger" },
                { "CI Pipeline", "Deployment", "Release Branch" }
            }
        };

        private readonly ReadOnlyCollection<PlayerClassDefinition> _classes;
        private readonly ReadOnlyCollection<LoadoutWeaponDefinition> _weapons;
        private readonly Dictionary<PlayerClassId, PlayerClassDefinition> _classesById = new();
        private readonly Dictionary<(PlayerClassId, LoadoutWeaponSlot), ReadOnlyCollection<LoadoutWeaponDefinition>> _weaponsByClassAndSlot = new();

        public ClassLoadoutCatalog(
            IList<PlayerClassDefinition> classes,
            IList<LoadoutWeaponDefinition> weapons)
        {
            if (classes == null)
            {
                throw new ArgumentNullException(nameof(classes));
            }

            if (weapons == null)
            {
                throw new ArgumentNullException(nameof(weapons));
            }

            _classes = new ReadOnlyCollection<PlayerClassDefinition>(new List<PlayerClassDefinition>(classes));

            List<LoadoutWeaponDefinition> orderedWeapons = new(weapons);
            orderedWeapons.Sort(CompareWeapons);
            _weapons = new ReadOnlyCollection<LoadoutWeaponDefinition>(orderedWeapons);

            foreach (PlayerClassDefinition definition in _classes)
            {
                if (definition != null && _classesById.ContainsKey(definition.Id) == false)
                {
                    _classesById.Add(definition.Id, definition);
                }
            }

            foreach (PlayerClassId playerClass in ApprovedClasses)
            {
                foreach (LoadoutWeaponSlot slot in Enum.GetValues(typeof(LoadoutWeaponSlot)))
                {
                    List<LoadoutWeaponDefinition> choices = orderedWeapons.FindAll(
                        definition => definition != null
                            && definition.PlayerClass == playerClass
                            && definition.Slot == slot);
                    choices.Sort((left, right) => left.OptionIndex.CompareTo(right.OptionIndex));
                    _weaponsByClassAndSlot[(playerClass, slot)] =
                        new ReadOnlyCollection<LoadoutWeaponDefinition>(choices);
                }
            }
        }

        public IReadOnlyList<PlayerClassDefinition> Classes => _classes;
        public IReadOnlyList<LoadoutWeaponDefinition> Weapons => _weapons;

        public bool TryGetClass(PlayerClassId playerClass, out PlayerClassDefinition definition)
        {
            return _classesById.TryGetValue(playerClass, out definition);
        }

        public IReadOnlyList<LoadoutWeaponDefinition> GetChoices(
            PlayerClassId playerClass,
            LoadoutWeaponSlot slot)
        {
            return _weaponsByClassAndSlot.TryGetValue((playerClass, slot), out ReadOnlyCollection<LoadoutWeaponDefinition> choices)
                ? choices
                : Array.Empty<LoadoutWeaponDefinition>();
        }

        public ClassLoadoutValidationResult Validate()
        {
            List<string> errors = new();
            if (_classes.Count != RequiredClassCount)
            {
                errors.Add($"Catalog must contain exactly {RequiredClassCount} classes; found {_classes.Count}.");
            }

            if (_weapons.Count != RequiredWeaponCount)
            {
                errors.Add($"Catalog must contain exactly {RequiredWeaponCount} weapons; found {_weapons.Count}.");
            }

            HashSet<PlayerClassId> classIds = new();
            HashSet<string> classStableIds = new(StringComparer.Ordinal);
            foreach (PlayerClassDefinition definition in _classes)
            {
                if (definition == null)
                {
                    errors.Add("Catalog contains a null class definition.");
                    continue;
                }

                if (Array.IndexOf(ApprovedClasses, definition.Id) < 0)
                {
                    errors.Add($"Class enum ID is unsupported: {definition.Id}.");
                }
                else if (classIds.Add(definition.Id) == false)
                {
                    errors.Add($"Class enum ID must be concrete and unique: {definition.Id}.");
                }

                if (string.IsNullOrWhiteSpace(definition.StableId)
                    || classStableIds.Add(definition.StableId) == false)
                {
                    errors.Add($"Class stable ID must be non-empty and unique: '{definition.StableId}'.");
                }

                ValidateRequiredText(definition.DisplayName, definition.StableId, "display name", errors);
                ValidateRequiredText(definition.LocalizationKey, definition.StableId, "localization key", errors);

                int approvedClassIndex = Array.IndexOf(ApprovedClasses, definition.Id);
                if (approvedClassIndex >= 0)
                {
                    if (string.Equals(definition.StableId, ApprovedClassStableIds[approvedClassIndex], StringComparison.Ordinal) == false)
                    {
                        errors.Add($"Class {definition.Id} must use stable ID '{ApprovedClassStableIds[approvedClassIndex]}'; found '{definition.StableId}'.");
                    }

                    if (string.Equals(definition.DisplayName, ApprovedClassDisplayNames[approvedClassIndex], StringComparison.Ordinal) == false)
                    {
                        errors.Add($"Class {definition.Id} must use approved name '{ApprovedClassDisplayNames[approvedClassIndex]}'; found '{definition.DisplayName}'.");
                    }
                }
            }

            HashSet<string> weaponIds = new(StringComparer.Ordinal);
            foreach (LoadoutWeaponDefinition weapon in _weapons)
            {
                if (weapon == null)
                {
                    errors.Add("Catalog contains a null weapon definition.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(weapon.Id) || weaponIds.Add(weapon.Id) == false)
                {
                    errors.Add($"Weapon ID must be non-empty and unique: '{weapon.Id}'.");
                }

                int approvedClassIndex = Array.IndexOf(ApprovedClasses, weapon.PlayerClass);
                if (approvedClassIndex < 0)
                {
                    errors.Add($"Weapon '{weapon.Id}' has unsupported class {weapon.PlayerClass}.");
                }

                bool validSlot = Enum.IsDefined(typeof(LoadoutWeaponSlot), weapon.Slot);
                if (validSlot == false)
                {
                    errors.Add($"Weapon '{weapon.Id}' has invalid slot {weapon.Slot}.");
                }

                bool validOption = weapon.OptionIndex >= 1 && weapon.OptionIndex <= RequiredChoicesPerSlot;
                if (validOption == false)
                {
                    errors.Add($"Weapon '{weapon.Id}' option must be inside 1..{RequiredChoicesPerSlot}: {weapon.OptionIndex}.");
                }

                ValidateRequiredText(weapon.DisplayName, weapon.Id, "display name", errors);
                ValidateRequiredText(weapon.ArchetypeId, weapon.Id, "archetype ID", errors);
                ValidateRequiredText(weapon.LocalizationKey, weapon.Id, "localization key", errors);

                if (approvedClassIndex >= 0 && validSlot && validOption)
                {
                    string approvedName = ApprovedWeaponNames[
                        approvedClassIndex,
                        (int)weapon.Slot - 1,
                        weapon.OptionIndex - 1];
                    if (string.Equals(weapon.DisplayName, approvedName, StringComparison.Ordinal) == false)
                    {
                        errors.Add($"Class {weapon.PlayerClass}, slot {(int)weapon.Slot}, option {weapon.OptionIndex} must use approved name '{approvedName}'; found '{weapon.DisplayName}'.");
                    }
                }
            }

            foreach (PlayerClassId playerClass in ApprovedClasses)
            {
                if (classIds.Contains(playerClass) == false)
                {
                    errors.Add($"Catalog is missing class {playerClass}.");
                }

                foreach (LoadoutWeaponSlot slot in Enum.GetValues(typeof(LoadoutWeaponSlot)))
                {
                    IReadOnlyList<LoadoutWeaponDefinition> choices = GetChoices(playerClass, slot);
                    if (choices.Count != RequiredChoicesPerSlot)
                    {
                        errors.Add($"Class {playerClass}, slot {(int)slot} must contain {RequiredChoicesPerSlot} choices; found {choices.Count}.");
                        continue;
                    }

                    HashSet<int> optionIndexes = new();
                    foreach (LoadoutWeaponDefinition choice in choices)
                    {
                        if (optionIndexes.Add(choice.OptionIndex) == false)
                        {
                            errors.Add($"Class {playerClass}, slot {(int)slot} repeats option {choice.OptionIndex}.");
                        }

                        if (choice.OptionIndex >= 1 && choice.OptionIndex <= RequiredChoicesPerSlot)
                        {
                            string approvedArchetype = ApprovedArchetypes[(int)slot - 1, choice.OptionIndex - 1];
                            if (string.Equals(choice.ArchetypeId, approvedArchetype, StringComparison.Ordinal) == false)
                            {
                                errors.Add($"Class {playerClass}, slot {(int)slot}, option {choice.OptionIndex} must map to '{approvedArchetype}'; found '{choice.ArchetypeId}'.");
                            }
                        }
                    }
                }
            }

            return new ClassLoadoutValidationResult(errors);
        }

        public static ClassLoadoutCatalog CreateDefault()
        {
            PlayerClassDefinition[] classes =
            {
                CreateClass(0),
                CreateClass(1),
                CreateClass(2)
            };

            List<LoadoutWeaponDefinition> weapons = new(RequiredWeaponCount);
            AddClassWeapons(weapons, 0);
            AddClassWeapons(weapons, 1);
            AddClassWeapons(weapons, 2);

            return new ClassLoadoutCatalog(classes, weapons);
        }

        private static PlayerClassDefinition CreateClass(int classIndex)
        {
            PlayerClassId id = ApprovedClasses[classIndex];
            string stableId = ApprovedClassStableIds[classIndex];
            return new PlayerClassDefinition(
                id,
                stableId,
                ApprovedClassDisplayNames[classIndex],
                $"class_loadout.class.{stableId}.name");
        }

        private static void AddClassWeapons(
            ICollection<LoadoutWeaponDefinition> destination,
            int classIndex)
        {
            PlayerClassId playerClass = ApprovedClasses[classIndex];
            string classId = ApprovedClassStableIds[classIndex];
            for (int slotIndex = 0; slotIndex < RequiredSlotCount; slotIndex++)
            {
                LoadoutWeaponSlot slot = (LoadoutWeaponSlot)(slotIndex + 1);
                for (int optionIndex = 0; optionIndex < RequiredChoicesPerSlot; optionIndex++)
                {
                    string displayName = ApprovedWeaponNames[classIndex, slotIndex, optionIndex];
                    string weaponId = $"{classId}_slot_{slotIndex + 1}_option_{optionIndex + 1}";
                    destination.Add(new LoadoutWeaponDefinition(
                        weaponId,
                        playerClass,
                        slot,
                        optionIndex + 1,
                        displayName,
                        ApprovedArchetypes[slotIndex, optionIndex],
                        $"class_loadout.weapon.{weaponId}.name"));
                }
            }
        }

        private static int CompareWeapons(
            LoadoutWeaponDefinition left,
            LoadoutWeaponDefinition right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int classComparison = left.PlayerClass.CompareTo(right.PlayerClass);
            if (classComparison != 0)
            {
                return classComparison;
            }

            int slotComparison = left.Slot.CompareTo(right.Slot);
            return slotComparison != 0
                ? slotComparison
                : left.OptionIndex.CompareTo(right.OptionIndex);
        }

        private static void ValidateRequiredText(
            string value,
            string ownerId,
            string field,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"'{ownerId}' has no {field}.");
            }
        }
    }
}
