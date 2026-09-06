using System.Collections.Generic;
using NUnit.Framework;

namespace LogoSurvivor.ClassLoadout.Tests
{
    public sealed class ClassLoadoutDataStateTests
    {
        [Test]
        public void DefaultCatalog_HasThreeClassesAndThirtySixValidatedWeapons()
        {
            ClassLoadoutCatalog catalog = ClassLoadoutCatalog.CreateDefault();

            Assert.That(catalog.Validate().IsValid, Is.True);
            Assert.That(catalog.Classes, Has.Count.EqualTo(3));
            Assert.That(catalog.Weapons, Has.Count.EqualTo(36));

            foreach (PlayerClassDefinition playerClass in catalog.Classes)
            {
                foreach (LoadoutWeaponSlot slot in new[]
                         {
                             LoadoutWeaponSlot.Slot1,
                             LoadoutWeaponSlot.Slot2,
                             LoadoutWeaponSlot.Slot3,
                             LoadoutWeaponSlot.Slot4
                         })
                {
                    IReadOnlyList<LoadoutWeaponDefinition> choices = catalog.GetChoices(playerClass.Id, slot);
                    Assert.That(choices, Has.Count.EqualTo(3));
                    Assert.That(choices[0].OptionIndex, Is.EqualTo(1));
                    Assert.That(choices[1].OptionIndex, Is.EqualTo(2));
                    Assert.That(choices[2].OptionIndex, Is.EqualTo(3));
                }
            }
        }

        [Test]
        public void DefaultCatalog_PreservesApprovedNamesAndSharedArchetypeColumns()
        {
            ClassLoadoutCatalog catalog = ClassLoadoutCatalog.CreateDefault();

            AssertChoice(catalog, PlayerClassId.GameDesigner, LoadoutWeaponSlot.Slot1, 1, "Игровой цикл", "auto_pistol");
            AssertChoice(catalog, PlayerClassId.Artist, LoadoutWeaponSlot.Slot3, 2, "ZBrush", "acid_pools");
            AssertChoice(catalog, PlayerClassId.Programmer, LoadoutWeaponSlot.Slot4, 3, "Release Branch", "protective_field");
        }

        [Test]
        public void ClassChoice_IsMandatoryAndQueuesSlotOne()
        {
            ClassLoadoutSession session = CreateSession();

            ClassLoadoutOperationResult result = session.TrySelectClass(PlayerClassId.Artist);

            Assert.That(result.Status, Is.EqualTo(ClassLoadoutOperationStatus.ClassSelected));
            Assert.That(session.SelectedClass.DisplayName, Is.EqualTo("Художник"));
            Assert.That(session.ClassChoicePending, Is.False);
            Assert.That(session.PendingSlot, Is.EqualTo(LoadoutWeaponSlot.Slot1));
            Assert.That(session.GetSlotSnapshot(LoadoutWeaponSlot.Slot1).State, Is.EqualTo(LoadoutSlotState.ChoicePending));
        }

        [Test]
        public void SlotSelections_AreFinalAndFollowMilestoneUnlocks()
        {
            ClassLoadoutSession session = CreateSession();
            session.TrySelectClass(PlayerClassId.Programmer);
            ClassLoadoutOperationResult slot1 = session.TrySelectPendingWeapon(2);
            ClassLoadoutOperationResult unlock2 = session.TryUnlockSlot(LoadoutWeaponSlot.Slot2);
            ClassLoadoutOperationResult slot2 = session.TrySelectPendingWeapon(3);
            ClassLoadoutOperationResult duplicate = session.TrySelectWeapon(
                LoadoutWeaponSlot.Slot2,
                session.Catalog.GetChoices(PlayerClassId.Programmer, LoadoutWeaponSlot.Slot2)[0].Id);

            Assert.That(slot1.Weapon.DisplayName, Is.EqualTo("State Machine"));
            Assert.That(unlock2.Status, Is.EqualTo(ClassLoadoutOperationStatus.SlotUnlocked));
            Assert.That(slot2.Weapon.DisplayName, Is.EqualTo("API Contract"));
            Assert.That(duplicate.Status, Is.EqualTo(ClassLoadoutOperationStatus.DuplicateIgnored));
            Assert.That(session.GetSlotSnapshot(LoadoutWeaponSlot.Slot2).SelectedWeapon.DisplayName, Is.EqualTo("API Contract"));
        }

        [Test]
        public void DuplicateMilestone_DoesNotReopenSelectedSlot()
        {
            ClassLoadoutSession session = CreateSession();
            session.TrySelectClass(PlayerClassId.GameDesigner);
            session.TrySelectPendingWeapon(1);

            ClassLoadoutOperationResult duplicate = session.TryUnlockSlot(LoadoutWeaponSlot.Slot1);

            Assert.That(duplicate.Status, Is.EqualTo(ClassLoadoutOperationStatus.DuplicateIgnored));
            Assert.That(session.HasMandatoryChoice, Is.False);
            Assert.That(session.GetSlotSnapshot(LoadoutWeaponSlot.Slot1).State, Is.EqualTo(LoadoutSlotState.Selected));
        }

        [Test]
        public void MultiplePendingMilestones_AreResolvedInSlotOrder()
        {
            ClassLoadoutSession session = CreateSession();
            session.TrySelectClass(PlayerClassId.GameDesigner);
            session.TryUnlockSlot(LoadoutWeaponSlot.Slot3);
            session.TryUnlockSlot(LoadoutWeaponSlot.Slot2);

            Assert.That(session.PendingSlot, Is.EqualTo(LoadoutWeaponSlot.Slot1));
            session.TrySelectPendingWeapon(1);
            Assert.That(session.PendingSlot, Is.EqualTo(LoadoutWeaponSlot.Slot2));
            session.TrySelectPendingWeapon(1);
            Assert.That(session.PendingSlot, Is.EqualTo(LoadoutWeaponSlot.Slot3));
        }

        [Test]
        public void TerminalOutcome_SuppressesPendingChoiceAndRejectsFurtherMutation()
        {
            ClassLoadoutSession session = CreateSession();
            session.TrySelectClass(PlayerClassId.Artist);

            bool finished = session.TryFinish();
            ClassLoadoutOperationResult selection = session.TrySelectPendingWeapon(1);
            ClassLoadoutOperationResult unlock = session.TryUnlockSlot(LoadoutWeaponSlot.Slot2);

            Assert.That(finished, Is.True);
            Assert.That(session.HasMandatoryChoice, Is.False);
            Assert.That(selection.Status, Is.EqualTo(ClassLoadoutOperationStatus.SessionFinished));
            Assert.That(unlock.Status, Is.EqualTo(ClassLoadoutOperationStatus.SessionFinished));
            Assert.That(session.GetSlotSnapshot(LoadoutWeaponSlot.Slot1).State, Is.EqualTo(LoadoutSlotState.Locked));
        }

        [Test]
        public void Reset_ClearsClassSlotsWeaponsAndLevels()
        {
            ClassLoadoutSession session = CreateSession();
            session.TrySelectClass(PlayerClassId.Programmer);
            session.TrySelectPendingWeapon(1);
            session.TrySetWeaponLevelForDebug(LoadoutWeaponSlot.Slot1, 9);
            session.TryUnlockSlot(LoadoutWeaponSlot.Slot2);

            session.Reset();

            Assert.That(session.State, Is.EqualTo(ClassLoadoutSessionState.Active));
            Assert.That(session.SelectedClassId, Is.EqualTo(PlayerClassId.None));
            Assert.That(session.ClassChoicePending, Is.True);
            foreach (ClassLoadoutSlotSnapshot slot in session.CreateSnapshot().Slots)
            {
                Assert.That(slot.State, Is.EqualTo(LoadoutSlotState.Locked));
                Assert.That(slot.SelectedWeapon, Is.Null);
                Assert.That(slot.Level, Is.Zero);
            }
        }

        [Test]
        public void DebugLevel_IsBoundedToZeroThroughNine()
        {
            ClassLoadoutSession session = CreateSession();
            session.TrySelectClass(PlayerClassId.GameDesigner);
            session.TrySelectPendingWeapon(1);

            ClassLoadoutOperationResult accepted = session.TrySetWeaponLevelForDebug(LoadoutWeaponSlot.Slot1, 9);
            ClassLoadoutOperationResult rejected = session.TrySetWeaponLevelForDebug(LoadoutWeaponSlot.Slot1, 10);

            Assert.That(accepted.Status, Is.EqualTo(ClassLoadoutOperationStatus.LevelChanged));
            Assert.That(rejected.Status, Is.EqualTo(ClassLoadoutOperationStatus.InvalidLevel));
            Assert.That(session.GetSlotSnapshot(LoadoutWeaponSlot.Slot1).Level, Is.EqualTo(9));
        }

        [Test]
        public void DuplicateWeaponId_FailsCatalogValidation()
        {
            ClassLoadoutCatalog source = ClassLoadoutCatalog.CreateDefault();
            List<LoadoutWeaponDefinition> weapons = new(source.Weapons);
            LoadoutWeaponDefinition original = weapons[1];
            weapons[1] = new LoadoutWeaponDefinition(
                weapons[0].Id,
                original.PlayerClass,
                original.Slot,
                original.OptionIndex,
                original.DisplayName,
                original.ArchetypeId,
                original.LocalizationKey);

            ClassLoadoutCatalog invalid = new(new List<PlayerClassDefinition>(source.Classes), weapons);

            Assert.That(invalid.Validate().IsValid, Is.False);
        }

        private static ClassLoadoutSession CreateSession()
        {
            return new ClassLoadoutSession(ClassLoadoutCatalog.CreateDefault());
        }

        private static void AssertChoice(
            ClassLoadoutCatalog catalog,
            PlayerClassId playerClass,
            LoadoutWeaponSlot slot,
            int optionIndex,
            string expectedName,
            string expectedArchetype)
        {
            LoadoutWeaponDefinition choice = catalog.GetChoices(playerClass, slot)[optionIndex - 1];
            Assert.That(choice.DisplayName, Is.EqualTo(expectedName));
            Assert.That(choice.ArchetypeId, Is.EqualTo(expectedArchetype));
        }
    }
}
