using System;
using System.Collections.Generic;
using UnityEngine;

namespace LogoSurvivor.AchievementCards
{
    [CreateAssetMenu(
        fileName = "AchievementCardPhotoLibrary",
        menuName = "Logo Survivor/Achievement Card Photo Library")]
    public sealed class AchievementCardPhotoLibrary : ScriptableObject
    {
        private static readonly string[] RequiredCardIds =
        {
            "direction_game_design_guild",
            "stage_preproduction",
            "direction_art_club",
            "stage_production",
            "direction_programmers_club",
            "stage_release_postproduction"
        };

        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private string _cardId;
            [SerializeField] private Texture2D _texture;

            public string CardId => _cardId;
            public Texture2D Texture => _texture;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public bool TryGetPhoto(string cardId, out Texture2D texture)
        {
            if (_entries != null && string.IsNullOrWhiteSpace(cardId) == false)
            {
                foreach (Entry entry in _entries)
                {
                    if (entry != null
                        && entry.Texture != null
                        && string.Equals(entry.CardId, cardId, StringComparison.Ordinal))
                    {
                        texture = entry.Texture;
                        return true;
                    }
                }
            }

            texture = null;
            return false;
        }

        public bool Validate(out string error)
        {
            if (_entries == null || _entries.Length != RequiredCardIds.Length)
            {
                error = $"Photo library must contain exactly {RequiredCardIds.Length} entries.";
                return false;
            }

            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (Entry entry in _entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.CardId) || entry.Texture == null)
                {
                    error = "Photo library contains an incomplete entry.";
                    return false;
                }

                if (ids.Add(entry.CardId) == false)
                {
                    error = $"Photo library contains duplicate card ID '{entry.CardId}'.";
                    return false;
                }
            }

            foreach (string requiredCardId in RequiredCardIds)
            {
                if (ids.Contains(requiredCardId) == false)
                {
                    error = $"Photo library is missing card ID '{requiredCardId}'.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
