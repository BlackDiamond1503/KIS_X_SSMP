using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Patches;
using HutongGames.PlayMaker.Actions;

[BepInPlugin("com.huntervsspeedrunner.kissxssmp", "KiSS X SSMP", "1.0.0")]
[BepInDependency("ssmp")]
[BepInDependency("io.github.shownyoung.knightinsilksong")]

public class KiSSxSSMP : BaseUnityPlugin
{
    private bool _ssmpPatched = false;
    // Find the SSMP assembly among currently loaded assemblies instead of referencing types
                                      // directly which can throw if SSMP isn't loaded yet.
    Assembly asm = System.Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a =>
        a.GetType("SSMP.Game.Client.Skin.SkinManager") != null || a.GetType("SSMP.Api.Eventing.InterEvent") != null || a.GetName().Name.ToLower().Contains("ssmp")
    );
    public static int ReadIntFromFile(string path)
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

    // For now, do not intercept the lower-level helpers; let original implementations run if needed.

    public static object CreatePlayerSkinFromDirectory(string directoryPath, Type playerSkinType)
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
                    var prefix = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinLoaderPatcher.SkinLoader_LoadAllSkins_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                    harmony.Patch(loadAll, prefix: prefix);
                    Logger.LogInfo("Patched SkinLoader.LoadAllSkins");
                }

                var loadTextures = System.Array.Find(skinLoaderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), m => m.Name == "LoadTexturesForSkin");
                if (loadTextures != null)
                {
                    var prefix2 = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinLoaderPatcher.SkinLoader_LoadTexturesForSkin_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
                    harmony.Patch(loadTextures, prefix: prefix2);
                    Logger.LogInfo("Patched SkinLoader.LoadTexturesForSkin");
                }

                var loadTexture = System.Array.Find(skinLoaderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), m => m.Name == "LoadTexture");
                if (loadTexture != null)
                {
                    var prefix3 = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinLoaderPatcher.SkinLoader_LoadTexture_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
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
                var prefix = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinManagerPatcer.UpdatePlayerSkin_Prefix), BindingFlags.Public | BindingFlags.Static));
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
                var prefix2 = new HarmonyMethod(typeof(KiSSxSSMP).GetMethod(nameof(SkinManagerPatcer.StoreDefaultPlayerSkin_Prefix), BindingFlags.NonPublic | BindingFlags.Static));
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
    
}