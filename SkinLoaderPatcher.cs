using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Patches
{
    class SkinLoaderPatcher
    {

        public static bool SkinLoader_LoadAllSkins_Prefix(object __instance, ref object skins)
        {
            try
            {
                if (skins == null)
                {
                    // nothing to populate
                    return false;
                }

                var skinsType = skins.GetType(); // Dictionary<byte, PlayerSkin>

                var asm = __instance.GetType().Assembly;

                // Check Disabled static field
                var disabledField = __instance.GetType().GetField("Disabled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (disabledField != null)
                {
                    var disabledVal = disabledField.GetValue(null);
                    if (disabledVal is bool b && b)
                    {
                        return false;
                    }
                }

                // get _skinFolderPath
                var pathField = __instance.GetType().GetField("_skinFolderPath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var skinFolderPath = pathField?.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(skinFolderPath) || !Directory.Exists(skinFolderPath))
                {
                    UnityEngine.Debug.LogWarning($"Tried to load all skins, but directory: {skinFolderPath} did not exist");
                    return false;
                }

                var directoryPaths = Directory.GetDirectories(skinFolderPath);
                if (directoryPaths.Length == 0)
                {
                    UnityEngine.Debug.LogWarning($"No skins can be loaded since there are no directories in: {skinFolderPath}");
                    return false;
                }

                // helpers
                var addOrSet = skinsType.GetProperty("Item"); // indexer
                var containsKey = skinsType.GetMethod("ContainsKey");
                var keysProperty = skinsType.GetProperty("Keys");

                var directoriesWithoutId = new System.Collections.Generic.Dictionary<string, object>();
                var idsUsed = new System.Collections.Generic.HashSet<byte>();

                var playerSkinType = asm.GetType("SSMP.Game.Client.Skin.PlayerSkin") ?? System.Array.Find(asm.GetTypes(), t => t.Name == "PlayerSkin");

                foreach (var directoryPath in directoryPaths)
                {
                    // load player skin from directory
                    var playerSkinObj = KiSSxSSMP.CreatePlayerSkinFromDirectory(directoryPath, playerSkinType);
                    if (playerSkinObj == null)
                    {
                        UnityEngine.Debug.LogWarning($"Tried to load player skin in directory: {directoryPath}, but failed");
                        continue;
                    }

                    var idFilePath = Path.Combine(directoryPath, "id.txt");
                    if (!File.Exists(idFilePath))
                    {
                        directoriesWithoutId[directoryPath] = playerSkinObj;
                        continue;
                    }

                    var id = KiSSxSSMP.ReadIntFromFile(idFilePath);
                    if (id == -1 || id < 1 || id > 255)
                    {
                        UnityEngine.Debug.LogWarning($"Tried to load player skin, but ID: {id} is not valid");
                        directoriesWithoutId[directoryPath] = playerSkinObj;
                        continue;
                    }

                    var idByte = (byte)id;
                    // set via indexer
                    addOrSet.SetValue(skins, playerSkinObj, new object[] { idByte });
                    idsUsed.Add(idByte);
                    UnityEngine.Debug.Log($"Successfully loaded skin in directory: {directoryPath}, given ID: {idByte}");
                }

                // Now assign IDs for directories without ID file
                foreach (var kv in directoriesWithoutId)
                {
                    int id;
                    for (id = 1; id < 256; id++)
                    {
                        if (!idsUsed.Contains((byte)id)) break;
                    }

                    if (id > 255)
                    {
                        UnityEngine.Debug.LogWarning("Could not find a valid ID for this skin, perhaps you have used all 255 slots?");
                        return false;
                    }

                    var idByte = (byte)id;
                    var idFilePath = Path.Combine(kv.Key, "id.txt");
                    File.WriteAllText(idFilePath, id.ToString());
                    addOrSet.SetValue(skins, kv.Value, new object[] { idByte });
                    idsUsed.Add(idByte);
                    UnityEngine.Debug.Log($"Successfully loaded skin in directory: {kv.Key}, given ID: {idByte}");
                }

                return false; // skip original method
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkinLoader.LoadAllSkins patch failed: " + ex.Message);
                return true;
            }
        }
        public static bool SkinLoader_LoadTexturesForSkin_Prefix(object __instance, object path, out object playerSkin)
        {
            playerSkin = null;
            return true;
        }

        public static bool SkinLoader_LoadTexture_Prefix(object __instance, object filePath, out object texture)
        {
            texture = null;
            return true;
        }
    }
}