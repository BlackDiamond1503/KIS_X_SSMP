using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

[BepInPlugin("com.huntervsspeedrunner.kissxssmp", "KiSS X SSMP", "1.0.0")]
[BepInDependency("ssmp")]

public class KiSSxSSMP : BaseUnityPlugin
{
    private bool _ssmpPatched = false;
    // Find the SSMP assembly among currently loaded assemblies instead of referencing types
                                      // directly which can throw if SSMP isn't loaded yet.
    Assembly asm = System.Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a =>
        a.GetType("SSMP.Game.Client.Skin.SkinManager") != null || a.GetType("SSMP.Api.Eventing.InterEvent") != null || a.GetName().Name.ToLower().Contains("ssmp")
    );
    private static int ReadIntFromFile(string path)
    {
        var fileContent = File.ReadAllText(path);
        if (!int.TryParse(fileContent, out var id))
        {
            return -1;
        }

        return id;
    }

    // Generic prefix used for aggressive patching. Returning false skips original method.
    private static bool GenericPrefix(params object[] args)
    {
        // Minimal behavior: do nothing and skip the original method.
        return false;
    }

    private void Awake()
    {
        Logger.LogInfo("Knight in SilkSong X SilkSongMultiPlayer loaded");

        // Attempt initial patch. If SSMP assembly is not loaded yet, subscribe to AssemblyLoad and retry.
        TryPatchSSMP();

        AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
        {
            if (!_ssmpPatched)
            {
                TryPatchSSMP();
            }
        };
    }

    // Prefix to replace SkinLoader.LoadAllSkins(ref Dictionary<byte, PlayerSkin> skins)
    // We accept the dictionary as a ref object and manipulate it via reflection.
    private static bool SkinLoader_LoadAllSkins_Prefix(object __instance, ref object skins)
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
                var playerSkinObj = CreatePlayerSkinFromDirectory(directoryPath, playerSkinType);
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

                var id = ReadIntFromFile(idFilePath);
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

    // For now, do not intercept the lower-level helpers; let original implementations run if needed.
    private static bool SkinLoader_LoadTexturesForSkin_Prefix(object __instance, object path, out object playerSkin)
    {
        playerSkin = null;
        return true;
    }

    private static bool SkinLoader_LoadTexture_Prefix(object __instance, object filePath, out object texture)
    {
        texture = null;
        return true;
    }

    private static object CreatePlayerSkinFromDirectory(string directoryPath, Type playerSkinType)
    {
        try
        {
            if (playerSkinType == null) return null;

            var skinObj = Activator.CreateInstance(playerSkinType);
            var setMethod = playerSkinType.GetMethod("SetKnightTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var knightFolder = Path.Combine(directoryPath, "Knight");
            for (int i = 0; i < 4; i++)
            {
                var atlasPath = Path.Combine(knightFolder, string.Format("atlas{0}.png", i));
                if (!File.Exists(atlasPath)) continue;
                var bytes = File.ReadAllBytes(atlasPath);
                var tex = new Texture2D(1, 1);
                var mi = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]) });
                if (mi != null)
                {
                    mi.Invoke(tex, new object[] { bytes });
                }
                setMethod.Invoke(skinObj, new object[] { tex, i });
            }

            return skinObj;
        }
        catch { return null; }
    }
                

    private void TryPatchSSMP()
    {
        try
        {
            var harmony = new Harmony("com.huntervsspeedrunner.kissxssmp");

            
            if (asm == null)
            {
                // SSMP not loaded yet; bail and wait for AssemblyLoad event.
                Logger.LogInfo("SSMP assembly not found among loaded assemblies; will retry on AssemblyLoad.");
                return;
            }
            var skinLoaderType = asm.GetType("SkinLoader") ?? System.Array.Find(asm.GetTypes(), t => t.Name == "SkinLoader");
            if (skinLoaderType == null)
            {
                Logger.LogWarning("SkinLoader type not found in SSMP assembly; available types: " + string.Join(", ", System.Array.ConvertAll(asm.GetTypes(), t => t.FullName)));
                return;
            }

            var field = AccessTools.Field(skinLoaderType, "Disabled") ?? skinLoaderType.GetField("Disabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Logger.LogWarning("Field 'Disabled' not found on SkinLoader");
                return;
            }

            if (field.FieldType != typeof(bool))
            {
                Logger.LogWarning("Found 'Disabled' field but it is not a boolean (type: " + field.FieldType.FullName + ")");
                return;
            }

            try
            {
                field.SetValue(null, false);
                Logger.LogInfo("Set SkinLoader.Disabled (static bool) to false");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("Failed to set SkinLoader.Disabled field: " + ex.Message);
            }

            // Patch SkinLoader methods: LoadAllSkins, LoadTexturesForSkin, LoadTexture
            try
            {
                var loadAll = System.Array.Find(skinLoaderType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), m => m.Name == "LoadAllSkins");
                if (loadAll != null)
                {
                    var prefix = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinLoader_LoadAllSkins_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                    harmony.Patch(loadAll, prefix: prefix);
                    Logger.LogInfo("Patched SkinLoader.LoadAllSkins");
                }

                var loadTextures = System.Array.Find(skinLoaderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), m => m.Name == "LoadTexturesForSkin");
                if (loadTextures != null)
                {
                    var prefix2 = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinLoader_LoadTexturesForSkin_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                    harmony.Patch(loadTextures, prefix: prefix2);
                    Logger.LogInfo("Patched SkinLoader.LoadTexturesForSkin");
                }

                var loadTexture = System.Array.Find(skinLoaderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), m => m.Name == "LoadTexture");
                if (loadTexture != null)
                {
                    var prefix3 = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinLoader_LoadTexture_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                    harmony.Patch(loadTexture, prefix: prefix3);
                    Logger.LogInfo("Patched SkinLoader.LoadTexture");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to patch SkinLoader methods: " + ex.Message);
            }

            // Patch SkinManager methods in SSMP assembly to use custom implementations via Harmony prefixes.
            var skinManagerType = asm.GetType("SSMP.Game.Client.Skin.SkinManager") ?? System.Array.Find(asm.GetTypes(), t => t.Name == "SkinManager");
            if (skinManagerType == null)
            {
                Logger.LogWarning("SkinManager type not found in SSMP assembly");
                return;
            }

            // Patch public UpdatePlayerSkin(GameObject, byte)
            var updateMethod = AccessTools.Method(skinManagerType, "UpdatePlayerSkin", new System.Type[] { typeof(GameObject), typeof(byte) });
            if (updateMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(UpdatePlayerSkin_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(updateMethod, prefix: prefix);
                Logger.LogInfo("Patched SkinManager.UpdatePlayerSkin");
            }
            else
            {
                Logger.LogWarning("UpdatePlayerSkin method not found on SkinManager");
            }

            // Find HeroController type for accurate StoreDefaultPlayerSkin lookup
            System.Type heroControllerType = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                heroControllerType = a.GetType("HeroController") ?? System.Array.Find(a.GetTypes(), t => t.Name == "HeroController");
                if (heroControllerType != null) break;
            }

            MethodInfo storeMethod = null;
            if (heroControllerType != null)
            {
                storeMethod = AccessTools.Method(skinManagerType, "StoreDefaultPlayerSkin", new System.Type[] { heroControllerType });
            }

            if (storeMethod == null)
            {
                // try to find by name only (private signature)
                storeMethod = System.Array.Find(skinManagerType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), m => m.Name == "StoreDefaultPlayerSkin");
            }

            if (storeMethod != null)
            {
                var prefix2 = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(StoreDefaultPlayerSkin_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(storeMethod, prefix: prefix2);
                Logger.LogInfo("Patched SkinManager.StoreDefaultPlayerSkin");
            }
            else
            {
                Logger.LogWarning("StoreDefaultPlayerSkin method not found on SkinManager");
            }

            // Aggressively patch all fields and methods on core types (SkinLoader, SkinManager, PlayerSkin)
            try
            {
                var playerSkinType = asm.GetType("SSMP.Game.Client.Skin.PlayerSkin") ?? System.Array.Find(asm.GetTypes(), t => t.Name == "PlayerSkin");
                var typesToPatch = new System.Type[] { skinLoaderType, skinManagerType, playerSkinType };
                foreach (var t in typesToPatch)
                {
                    if (t == null) continue;

                    // Set static fields to safe defaults where possible
                    foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        try
                        {
                            if (f.FieldType == typeof(bool))
                            {
                                f.SetValue(null, false);
                                Logger.LogInfo($"Set static bool {t.FullName}.{f.Name} = false");
                            }
                            else if (f.FieldType == typeof(string))
                            {
                                f.SetValue(null, string.Empty);
                                Logger.LogInfo($"Cleared static string {t.FullName}.{f.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to set static field {t.FullName}.{f.Name}: {ex.Message}");
                        }
                    }

                    // Patch methods with a generic prefix that skips original execution
                    var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var m in methods)
                    {
                        try
                        {
                            if (m.IsSpecialName || m.IsConstructor) continue;
                            var genericPrefix = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(GenericPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                            harmony.Patch(m, prefix: genericPrefix);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to patch method {t.FullName}.{m.Name}: {ex.Message}");
                        }
                    }
                }
                Logger.LogInfo("Aggressively patched core SSMP types");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Aggressive patching failed: " + ex.Message);
            }

            _ssmpPatched = true;
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning("TryPatchSSMP failed: " + ex.Message);
        }
    }
    /*private static bool SkinLoaderDisabledPrefix(object __instance)
    {
        var loggerField = typeof(KiSSxSSMP).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        UnityEngine.Debug.Log("SkinLoader.Disabled invoked (patched)");
        return true;
    }*/

    // Harmony prefix to replace SkinManager.UpdatePlayerSkin(GameObject, byte)
    private static bool UpdatePlayerSkin_Prefix(object __instance, GameObject playerObject, byte skinId)
    {
        try
        {
            if (playerObject == null)
            {
                return false; // skip original
            }

            var type = __instance.GetType();

            var defaultField = type.GetField("_defaultPlayerSkin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var playerSkinsField = type.GetField("_playerSkins", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            var defaultSkin = defaultField?.GetValue(__instance);
            object playerSkin = defaultSkin;

            if (skinId != 0 && playerSkinsField != null)
            {
                var dict = playerSkinsField.GetValue(__instance);
                if (dict != null)
                {
                    var tryGet = dict.GetType().GetMethod("TryGetValue");
                    var args = new object[] { skinId, null };
                    var found = (bool)tryGet.Invoke(dict, args);
                    if (found)
                    {
                        playerSkin = args[1];
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Tried to update skin with ID: {skinId}, but there was no such skin loaded");
                        playerSkin = defaultSkin;
                    }
                }
            }

            if (playerSkin == null)
            {
                return false;
            }

            // Find tk2dSpriteAnimator type
            System.Type spriteAnimatorType = null;
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                spriteAnimatorType = a.GetType("tk2dSpriteAnimator") ?? System.Array.Find(a.GetTypes(), t => t.Name == "tk2dSpriteAnimator");
                if (spriteAnimatorType != null) break;
            }

            if (spriteAnimatorType == null)
            {
                UnityEngine.Debug.LogWarning("tk2dSpriteAnimator type not found");
                return false;
            }

            var getComponentMethod = typeof(GameObject).GetMethod("GetComponent", new System.Type[] { typeof(System.Type) });
            var spriteAnimator = getComponentMethod.Invoke(playerObject, new object[] { spriteAnimatorType });
            if (spriteAnimator == null)
            {
                UnityEngine.Debug.LogWarning("Tried to update player skin, but SpriteAnimator is null");
                return false;
            }

            // Get clip Sprint -> frames[0] -> spriteCollection
            var getClip = spriteAnimatorType.GetMethod("GetClipByName");
            var clip = getClip.Invoke(spriteAnimator, new object[] { "Sprint" });
            if (clip == null) return false;

            var framesField = clip.GetType().GetField("frames");
            var frames = framesField.GetValue(clip) as System.Array;
            if (frames == null || frames.Length == 0) return false;
            var frame0 = frames.GetValue(0);
            var spriteCollectionField = frame0.GetType().GetField("spriteCollection");
            var spriteCollection = spriteCollectionField.GetValue(frame0);

            var materialsField = spriteCollection.GetType().GetField("materials");
            var materials = materialsField.GetValue(spriteCollection) as System.Array;

            var knightField = playerSkin.GetType().GetField("Knight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var knightArray = knightField?.GetValue(playerSkin) as System.Array;
            var defaultKnightArray = defaultSkin?.GetType().GetField("Knight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(defaultSkin) as System.Array;

            for (int i = 0; i < 4; i++)
            {
                object atlas = null;
                if (knightArray != null) atlas = knightArray.GetValue(i);
                if (atlas == null && defaultKnightArray != null) atlas = defaultKnightArray.GetValue(i);
                if (atlas != null && materials != null && i < materials.Length)
                {
                    var mat = materials.GetValue(i);
                    var mainTextureProp = mat.GetType().GetProperty("mainTexture");
                    if (mainTextureProp != null)
                    {
                        mainTextureProp.SetValue(mat, atlas);
                    }
                }
            }

            return false; // skip original
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning("UpdatePlayerSkin patch failed: " + ex.Message);
            return true; // fall back to original if something goes wrong
        }
    }

    // Harmony prefix to replace SkinManager.StoreDefaultPlayerSkin(HeroController)
    private static bool StoreDefaultPlayerSkin_Prefix(object __instance, object heroController)
    {
        try
        {
            if (heroController == null) return false;

            var heroType = heroController.GetType();
            var gameObjectProp = heroType.GetProperty("gameObject");
            var localPlayerObject = gameObjectProp?.GetValue(heroController) as GameObject;
            if (localPlayerObject == null) return false;

            System.Type spriteAnimatorType = null;
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                spriteAnimatorType = a.GetType("tk2dSpriteAnimator") ?? System.Array.Find(a.GetTypes(), t => t.Name == "tk2dSpriteAnimator");
                if (spriteAnimatorType != null) break;
            }

            if (spriteAnimatorType == null) return false;

            var getComponentMethod = typeof(GameObject).GetMethod("GetComponent", new System.Type[] { typeof(System.Type) });
            var spriteAnimator = getComponentMethod.Invoke(localPlayerObject, new object[] { spriteAnimatorType });
            if (spriteAnimator == null) return false;

            var getClip = spriteAnimatorType.GetMethod("GetClipByName");
            var clip = getClip.Invoke(spriteAnimator, new object[] { "Idle" });
            if (clip == null) return false;

            var framesField = clip.GetType().GetField("frames");
            var frames = framesField.GetValue(clip) as System.Array;
            if (frames == null || frames.Length == 0) return false;
            var frame0 = frames.GetValue(0);
            var spriteCollectionField = frame0.GetType().GetField("spriteCollection");
            var spriteCollection = spriteCollectionField.GetValue(frame0);

            var materialsField = spriteCollection.GetType().GetField("materials");
            var materials = materialsField.GetValue(spriteCollection) as System.Array;

            if (materials == null)
            {
                UnityEngine.Debug.LogWarning("Tried to store default player skin, but player sprite collection was null");
                return false;
            }

            var playerSkinType = __instance.GetType().Assembly.GetType("SSMP.Game.Client.Skin.PlayerSkin") ?? System.Array.Find(__instance.GetType().Assembly.GetTypes(), t => t.Name == "PlayerSkin");
            if (playerSkinType == null) return false;

            var skinObj = System.Activator.CreateInstance(playerSkinType);
            var setMethod = playerSkinType.GetMethod("SetKnightTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < 4; i++)
            {
                if (i >= materials.Length)
                {
                    UnityEngine.Debug.LogWarning($"Tried to store default player skin, but atlas {i} texture was null");
                    return false;
                }

                var mat = materials.GetValue(i);
                var mainTextureProp = mat.GetType().GetProperty("mainTexture");
                var tex = mainTextureProp?.GetValue(mat);
                setMethod.Invoke(skinObj, new object[] { tex, i });
            }

            var defaultField = __instance.GetType().GetField("_defaultPlayerSkin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            defaultField.SetValue(__instance, skinObj);

            return false;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("StoreDefaultPlayerSkin patch failed: " + ex.Message);
            return true;
        }
    }
}