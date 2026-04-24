using System.Reflection;
using UnityEngine;

namespace Patches
{
    class SkinManagerPatcer
    {
        public static bool UpdatePlayerSkin_Prefix(object __instance, GameObject playerObject, byte skinId)
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
        public static bool StoreDefaultPlayerSkin_Prefix(object __instance, object heroController)
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
}