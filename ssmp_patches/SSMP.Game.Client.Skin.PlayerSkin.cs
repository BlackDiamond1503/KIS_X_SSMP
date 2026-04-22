using System;
using UnityEngine;

namespace SSMP.Game.Client.Skin
{
    /// <summary>
    /// Data class for player skin textures.
    /// </summary>
    internal class PlayerSkin
    {
        public Texture[] Knight { get; private set; } = new Texture[4];

        private void CheckIndexInBounds(int index, int max, int min = 0)
        {
            if (index > max || index < min)
            {
                throw new IndexOutOfRangeException($"Atlas index must be between {min} and {max}");
            }
        }

        /// <summary>
        /// Set the knight texture for the skin.
        /// </summary>
        /// <param name="atlasTexture">The knight atlas texture.</param>
        /// <param name="atlasIndex">The index of the atlas</param>
        public void SetKnightTexture(Texture atlasTexture, int atlasIndex)
        {
            CheckIndexInBounds(atlasIndex, max: 3);
            Knight[atlasIndex] = atlasTexture;
        }
    }
}
