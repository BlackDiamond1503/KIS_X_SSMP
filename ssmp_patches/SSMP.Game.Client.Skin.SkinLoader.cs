using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SSMP.Game.Client.Skin
{
    /// <summary>
    /// Class responsible for disk interaction for all skin related operations.
    /// </summary>
    internal class SkinLoader
    {
        /// <summary>
        /// Temporary boolean that dictates whether skin loading is enabled.
        /// </summary>
        /// TODO: implement skins
        private static readonly bool Disabled = false;

        /// <summary>
        /// The name of the skin folder in the SSMP mod folder.
        /// </summary>
        private const string SkinFolderName = "Skins";

        /// <summary>
        /// The name of the Knight texture file.
        /// </summary>
        private const string KnightFolderName = "Knight";
        private const string AtlasFileName = "atlas{0}.png";

        /// <summary>
        /// The name of the file that contains the ID for a skin.
        /// </summary>
        private const string IdFileName = "id.txt";

        /// <summary>
        /// The full path of the skin folder.
        /// </summary>
        private readonly string _skinFolderPath;

        public SkinLoader()
        {
            var modsFolderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            _skinFolderPath = CombinePaths(modsFolderPath, SkinFolderName);
            if (!Directory.Exists(_skinFolderPath))
            {
                Directory.CreateDirectory(_skinFolderPath);
            }
        }

        /// <summary>
        /// Load all skins on disk in the given path into the given Dictionary. Assumes that the given
        /// dictionary is non-null.
        /// </summary>
        /// <param name="skins">A non-null dictionary that will contain the loaded skins.</param>
        public void LoadAllSkins(ref Dictionary<byte, PlayerSkin> skins)
        {
            if (Disabled)
            {
                return;
            }

            if (!Directory.Exists(_skinFolderPath))
            {
                Debug.LogWarning($"Tried to load all skins, but directory: {_skinFolderPath} did not exist");
                return;
            }

            var directoryPaths = Directory.GetDirectories(_skinFolderPath);
            if (directoryPaths.Length == 0)
            {
                Debug.LogWarning($"No skins can be loaded since there are no directories in: {_skinFolderPath}");
                return;
            }

            // Mapping of directory paths that do not have a file containing a valid ID to their skin
            var directoriesWithoutId = new Dictionary<string, PlayerSkin>();
            // Set of valid IDs that have been used for skins already
            var idsUsed = new HashSet<byte>();

            // We first loop over all directories and check whether they contain a file indicating their ID
            foreach (var directoryPath in directoryPaths)
            {
                // Try to load the player skin in this directory
                if (!LoadTexturesForSkin(directoryPath, out var playerSkin))
                {
                    Debug.LogWarning($"Tried to load player skin in directory: {directoryPath}, but failed");
                    continue;
                }

                // Check whether an ID file exists
                var idFilePath = Path.Combine(directoryPath, IdFileName);
                if (!File.Exists(idFilePath))
                {
                    directoriesWithoutId[directoryPath] = playerSkin;
                    continue;
                }

                // Read the ID from the file and do sanity checks an whether it is a valid ID
                var id = ReadIntFromFile(idFilePath);
                if (id == -1)
                {
                    Debug.LogWarning($"Tried to load player skin, but ID: {id} is not valid");
                    directoriesWithoutId[directoryPath] = playerSkin;
                    continue;
                }

                if (id > 255 || id < 1)
                {
                    Debug.LogWarning($"Tried to load player skin, but ID: {id} is not valid (< 1 or > 255)");
                    directoriesWithoutId[directoryPath] = playerSkin;
                    continue;
                }

                var idByte = (byte)id;

                Debug.Log($"Successfully loaded skin in directory: {directoryPath}, given ID: {idByte}");

                // Save it in the mapping and overwrite an existing entry
                skins[idByte] = playerSkin;
                // Also save the ID in a set so we know it is used already
                idsUsed.Add(idByte);
            }

            // Now we loop over the directories that didn't have an ID yet
            foreach (var directorySkinPair in directoriesWithoutId)
            {
                var directoryPath = directorySkinPair.Key;

                var idFilePath = Path.Combine(directoryPath, IdFileName);

                // Whether the file exists or not, this will give a StreamWriter that (over)writes the file
                var streamWriter = File.CreateText(idFilePath);

                // Find the lowest byte that hasn't been used yet for an ID
                int id;
                for (id = 1; id < 256; id++)
                {
                    if (!idsUsed.Contains((byte)id))
                    {
                        break;
                    }
                }

                if (id > 255)
                {
                    Debug.LogWarning("Could not find a valid ID for this skin, perhaps you have used all 255 slots?");
                    return;
                }

                var idByte = (byte)id;

                Debug.Log($"Successfully loaded skin in directory: {directoryPath}, given ID: {idByte}");

                // Write the ID to the file and close the StreamWriter
                streamWriter.Write(id);
                streamWriter.Close();

                // Save it in the mapping and overwrite an existing entry
                skins[idByte] = directorySkinPair.Value;
                // Also save the ID in a set so we know it is used now
                idsUsed.Add(idByte);
            }
        }

        /// <summary>
        /// Try to load the textures for a player skin from disk at the given path. This path should be
        /// the full path ending in the directory that contains the texture files.
        /// </summary>
        /// <param name="path">The full path of a directory containing a player skin.</param>
        /// <param name="playerSkin">If the method returns, will contain the loaded player skin or a fallback
        /// empty player skin if no skin could be loaded.</param>
        /// <returns>true if the skin could be loaded, false otherwise.</returns>
        private bool LoadTexturesForSkin(string path, out PlayerSkin playerSkin)
        {
            // Fallback out value to make sure we can always return false if loading failed
            playerSkin = new PlayerSkin();

            if (!Directory.Exists(path))
            {
                return false;
            }

            // Load Knight atlases
            var knightPath = Path.Combine(path, KnightFolderName);
            for (var i = 0; i < 4; i++)
            {
                var atlasPath = Path.Combine(knightPath, string.Format(AtlasFileName, i));
                if (LoadTexture(atlasPath, out var atlasTexture))
                {
                    playerSkin.SetKnightTexture(atlasTexture, i);
                }
            }

            return true;
        }

        /// <summary>
        /// Try to load a texture at the given file path.
        /// </summary>
        /// <param name="filePath">The full path of the file.</param>
        /// <param name="texture">If the method returns, will contain the loaded texture or null if no texture
        /// could be loaded.</param>
        /// <returns>true if the texture could be loaded, false otherwise.</returns>
        private bool LoadTexture(string filePath, out Texture2D texture)
        {
            texture = null;

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Tried to load texture at: {filePath}, but it didn't exist");
                return false;
            }

            var textureBytes = File.ReadAllBytes(filePath);
            texture = new Texture2D(1, 1);

            return ImageLoad(texture, textureBytes);
        }

        private static bool ImageLoad(Texture2D tex, byte[] data)
        {
            // Texture2D.LoadImage(byte[]) is UnityEngine method; call via reflection to avoid compile mismatch.
            var mi = typeof(Texture2D).GetMethod("LoadImage", new System.Type[] { typeof(byte[]) });
            if (mi == null) return false;
            var result = mi.Invoke(tex, new object[] { data });
            if (result is bool b) return b;
            return true;
        }

        /// <summary>
        /// Reads an integer from a file at the given path. Returns -1 if the file contents cannot be parsed
        /// to an int.
        /// </summary>
        /// <param name="path">The path of the file to read from.</param>
        /// <returns><code>-1</code> if the file contents cannot be parsed as an int, otherwise the int
        /// value.</returns>
        private static int ReadIntFromFile(string path)
        {
            var fileContent = File.ReadAllText(path);
            if (!int.TryParse(fileContent, out var id))
            {
                return -1;
            }

            return id;
        }

        /// <summary>
        /// Combines the variable number of given existing paths into a complete path. Uses Path.Combine
        /// for intermediate steps.
        /// </summary>
        /// <param name="paths">String array containing the path to combine.</param>
        /// <returns>The combined path from the given paths.</returns>
        private static string CombinePaths(params string[] paths)
        {
            if (paths.Length == 0)
            {
                return "";
            }

            if (paths.Length == 1)
            {
                return paths[0];
            }

            var resultPath = "";
            foreach (var path in paths)
            {
                resultPath = Path.Combine(resultPath, path);
            }

            return resultPath;
        }
    }
}
