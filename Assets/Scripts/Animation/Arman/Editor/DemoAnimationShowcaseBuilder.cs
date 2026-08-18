using System;
using System.Collections.Generic;
using System.Linq;
using MLA_SIM.Dooms.Registries;
using MLA_SIM.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MLA_SIM.Editor
{
    [InitializeOnLoad]
    public static class DemoAnimationShowcaseBuilder
    {
        private const string ScenePath = "Assets/Scenes/Demo_1/Demo_1.unity";
        private const string CharactersRootName = "Demo_Characters";
        private const string ShowcaseRootName = "Demo_Animation_Showcase";
        private const string ControllerPath = "Assets/01-Content/02-Animations/Humanoid_2.controller";
        private const string HammerPath = "Assets/02-Character_Assets/02-Proprs/Hammer/Hammer_1/Hammer_1.fbx";
        private const string BottlePath = "Assets/PolygonApocalypse/Models/SM_Item_Bottle_01.fbx";
        private const string PhonePath = "Assets/02-Character_Assets/02-Proprs/Phone/Phone.fbx";
        private const string TeddyBearPath = "Assets/02-Character_Assets/02-Proprs/Teddy_Bear/Teddy_Bear.fbx";
        private const string BenchPrefabPath = "Assets/02-Character_Assets/02-Proprs/Bench/Bench_Prefab.prefab";
        private const string HamburgerPath = "Assets/02-Character_Assets/02-Proprs/Hamburger/Hamburger.FBX";
        private const string ThrownPropUpgradeMarker = "_Thrown_Prop_Setup_V1";
        private const string PhonePropUpgradeMarker = "_Phone_Prop_Setup_V1";
        private const string TeddyBearPropUpgradeMarker = "_Teddy_Bear_Prop_Setup_V1";
        private const string EatingUpgradeMarker = "_Eating_Showcase_Setup_V2";

        private sealed class SoloAction
        {
            public readonly string sequenceId;
            public readonly string stateName;
            public readonly float duration;

            public SoloAction(string sequenceId, string stateName, float duration)
            {
                this.sequenceId = sequenceId;
                this.stateName = stateName;
                this.duration = duration;
            }
        }

        private static readonly SoloAction[] SoloActions =
        {
            new SoloAction("DoomScroll_2", "DoomScroll", 6.67f),
            new SoloAction("Catatonic", "Catatonic_Loop", 10f),
            new SoloAction("Holding_Comfort_Object", "Holding_Comfort_Object", 10f),
            new SoloAction("Looking_Away", "Looking_Away", 10f),
            new SoloAction("NO_NO_NO", "NO_NO_NO", 4.75f),
            new SoloAction("Smashing_Object", "Smashing_Object", 8.6f),
            new SoloAction("Throwing_Molotov", "Throwing_Molotov", 6f),
            new SoloAction("Self_Immolation", "Self_Immolation", 8f),
        };

        static DemoAnimationShowcaseBuilder()
        {
            EditorApplication.delayCall += BuildAutomaticallyOnce;
            EditorApplication.update += UpgradeEatingWhenSceneIsReady;
        }

        private static void UpgradeEatingWhenSceneIsReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return;

            GameObject showcaseRoot = FindInScene(scene, ShowcaseRootName);
            if (showcaseRoot == null) return;

            UpgradeEatingShowcase(scene, showcaseRoot);
            if (showcaseRoot.transform.Find(EatingUpgradeMarker) != null)
            {
                EditorApplication.update -= UpgradeEatingWhenSceneIsReady;
            }
        }

        [MenuItem("DOOMS/Animation/Build Demo 1 Showcase")]
        public static void BuildFromMenu()
        {
            Build(showCompletionDialog: true);
        }

        [MenuItem("DOOMS/Animation/Setup Eating Showcase")]
        public static void SetupEatingShowcaseFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[DemoAnimationShowcase] Open '{ScenePath}' before setting up Eating.");
                return;
            }

            GameObject showcaseRoot = FindInScene(scene, ShowcaseRootName);
            if (showcaseRoot == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Build the Demo_1 showcase before setting up Eating.");
                return;
            }

            UpgradeEatingShowcase(scene, showcaseRoot);
        }

        private static void BuildAutomaticallyOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return;

            GameObject showcaseRoot = FindInScene(scene, ShowcaseRootName);
            if (showcaseRoot != null)
            {
                UpgradeThrownPropShowcase(scene, showcaseRoot);
                UpgradePhonePropShowcase(scene, showcaseRoot);
                UpgradeTeddyBearPropShowcase(scene, showcaseRoot);
                UpgradeEatingShowcase(scene, showcaseRoot);
                return;
            }

            Build(showCompletionDialog: false);
        }

        private static void Build(bool showCompletionDialog)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[DemoAnimationShowcase] Open '{ScenePath}' before building the showcase.");
                return;
            }

            GameObject charactersRoot = FindInScene(scene, CharactersRootName);
            if (charactersRoot == null)
            {
                Debug.LogError($"[DemoAnimationShowcase] '{CharactersRootName}' was not found in Demo_1.");
                return;
            }

            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[DemoAnimationShowcase] Controller not found at '{ControllerPath}'.");
                return;
            }

            List<GameObject> characters = DirectChildren(charactersRoot.transform);
            GameObject female = characters.FirstOrDefault(IsFemaleCharacter);
            GameObject male = FindNearestMale(characters, female);
            if (female == null || male == null)
            {
                Debug.LogError("[DemoAnimationShowcase] A male/female character pair could not be resolved under Demo_Characters.");
                return;
            }

            GameObject showcaseRoot = GetOrCreateRoot(scene, ShowcaseRootName);
            DisableLegacyPairDemos(scene);

            List<GameObject> soloCharacters = characters
                .Where(character => character != male && character != female)
                .Take(SoloActions.Length)
                .ToList();

            if (soloCharacters.Count < SoloActions.Length)
            {
                Debug.LogError($"[DemoAnimationShowcase] Expected {SoloActions.Length} solo characters, found {soloCharacters.Count}.");
                return;
            }

            GameObject hammer = AssetDatabase.LoadAssetAtPath<GameObject>(HammerPath);
            GameObject bottle = AssetDatabase.LoadAssetAtPath<GameObject>(BottlePath);

            for (int i = 0; i < SoloActions.Length; i++)
            {
                ConfigureSoloCharacter(soloCharacters[i], SoloActions[i], controller, hammer, bottle, i);
            }

            ConfigurePairedCharacter(male, PairedAnimationSex.Male, controller);
            ConfigurePairedCharacter(female, PairedAnimationSex.Female, controller);
            ConfigurePairedDemo(showcaseRoot.transform, male, female);
            UpgradePhonePropShowcase(scene, showcaseRoot);
            UpgradeTeddyBearPropShowcase(scene, showcaseRoot);
            UpgradeEatingShowcase(scene, showcaseRoot);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = showcaseRoot;

            Debug.Log(
                $"[DemoAnimationShowcase] Demo_1 configured: {SoloActions.Length} solo previews plus looping Kiss/Hug playback.",
                showcaseRoot);

            if (showCompletionDialog)
            {
                EditorUtility.DisplayDialog(
                    "Demo Animation Showcase",
                    "Demo_1 now contains eight solo action previews and a looping Kiss/Hug pair showcase.",
                    "OK");
            }
        }

        private static void ConfigureSoloCharacter(
            GameObject character,
            SoloAction action,
            RuntimeAnimatorController controller,
            GameObject hammer,
            GameObject bottle,
            int index)
        {
            if (!ConfigureCommonCharacter(character, controller)) return;

            AnimatorActionPreview preview = EnsureComponent<AnimatorActionPreview>(character);
            preview.playOnStart = true;
            preview.loop = true;
            preview.pauseT4Brain = true;
            preview.startDelay = 1f + index * 0.12f;
            preview.transitionPause = 1f;
            preview.playlist = new List<AnimatorActionPreview.PreviewStep>
            {
                new AnimatorActionPreview.PreviewStep
                {
                    sequenceId = action.sequenceId,
                    holdSeconds = action.duration,
                },
            };

            AnimatorPropDriver propDriver = EnsureComponent<AnimatorPropDriver>(character);
            propDriver.propBindings.Clear();
            if (action.sequenceId == "Smashing_Object" && hammer != null)
            {
                propDriver.propBindings.Add(new AnimatorPropDriver.PropBinding
                {
                    stateName = action.stateName,
                    propPrefab = hammer,
                    boneName = "CC_Base_R_Hand",
                    localOffset = Vector3.zero,
                    localRotationEuler = new Vector3(90f, 0f, 0f),
                });
            }
            else if (action.sequenceId == "Throwing_Molotov" && bottle != null)
            {
                propDriver.propBindings.Add(new AnimatorPropDriver.PropBinding
                {
                    stateName = action.stateName,
                    propPrefab = bottle,
                    boneName = "CC_Base_R_Hand",
                    localOffset = Vector3.zero,
                    localRotationEuler = new Vector3(90f, 0f, 0f),
                });

                ConfigureThrownPropDriver(character, bottle);
            }

            MarkChanged(preview);
            MarkChanged(propDriver);
        }

        private static void UpgradeThrownPropShowcase(Scene scene, GameObject showcaseRoot)
        {
            if (showcaseRoot.transform.Find(ThrownPropUpgradeMarker) != null) return;

            GameObject charactersRoot = FindInScene(scene, CharactersRootName);
            GameObject bottle = AssetDatabase.LoadAssetAtPath<GameObject>(BottlePath);
            if (charactersRoot == null || bottle == null) return;

            foreach (GameObject character in DirectChildren(charactersRoot.transform))
            {
                AnimatorActionPreview preview = character.GetComponent<AnimatorActionPreview>();
                if (preview == null || preview.playlist == null) continue;
                bool isMolotovPreview = preview.playlist.Any(step => step != null
                    && string.Equals(step.sequenceId, "Throwing_Molotov", StringComparison.OrdinalIgnoreCase));
                if (!isMolotovPreview) continue;

                ConfigureThrownPropDriver(character, bottle);
                break;
            }

            GetOrCreateChild(showcaseRoot.transform, ThrownPropUpgradeMarker);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DemoAnimationShowcase] Added animation-event-driven Molotov projectile playback.", showcaseRoot);
        }

        private static void UpgradePhonePropShowcase(Scene scene, GameObject showcaseRoot)
        {
            if (showcaseRoot.transform.Find(PhonePropUpgradeMarker) != null) return;

            GameObject phone = AssetDatabase.LoadAssetAtPath<GameObject>(PhonePath);
            PropRegistrySO propRegistry = AssetDatabase.LoadAssetAtPath<PropRegistrySO>(
                "Assets/Resources/Dooms/PropRegistry.asset");
            AnimationSequenceRegistry sequenceRegistry = AssetDatabase.LoadAssetAtPath<AnimationSequenceRegistry>(
                "Assets/Resources/Dooms/AnimationSequenceRegistry.asset");
            if (phone == null || propRegistry == null || sequenceRegistry == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Phone prop or animation registries could not be loaded.");
                return;
            }

            PropEntry phoneEntry = propRegistry.FindProp("Phone");
            if (phoneEntry == null)
            {
                phoneEntry = new PropEntry { propId = "Phone" };
                propRegistry.props.Add(phoneEntry);
            }

            phoneEntry.itemId = "";
            phoneEntry.prefab = phone;
            phoneEntry.defaultBone = "CC_Base_R_Hand";
            phoneEntry.localOffset = Vector3.zero;
            phoneEntry.localRotationEuler = Vector3.zero;
            phoneEntry.description = "Phone used by DoomScroll_2 and other handheld phone actions.";

            ActionAnimSequence doomScroll = sequenceRegistry.FindSequence("DoomScroll_2");
            if (doomScroll == null)
            {
                Debug.LogError("[DemoAnimationShowcase] DoomScroll_2 was not found in AnimationSequenceRegistry.");
                return;
            }
            doomScroll.startPropId = "Phone";

            EditorUtility.SetDirty(propRegistry);
            EditorUtility.SetDirty(sequenceRegistry);
            AssetDatabase.SaveAssets();
            GetOrCreateChild(showcaseRoot.transform, PhonePropUpgradeMarker);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DemoAnimationShowcase] Registered the Phone prop for DoomScroll_2.", showcaseRoot);
        }

        private static void ConfigureThrownPropDriver(GameObject character, GameObject bottle)
        {
            AnimatorThrownPropDriver thrownProp = EnsureComponent<AnimatorThrownPropDriver>(character);
            thrownProp.stateName = "Throwing_Molotov";
            thrownProp.launchBoneName = "CC_Base_R_Hand";
            thrownProp.projectilePrefab = bottle;
            thrownProp.forwardSpeed = 7f;
            thrownProp.upwardSpeed = 3f;
            thrownProp.sidewaysSpeed = 0f;
            thrownProp.angularVelocity = new Vector3(8f, 4f, 6f);
            thrownProp.mass = 0.35f;
            thrownProp.linearDamping = 0.05f;
            thrownProp.angularDamping = 0.05f;
            thrownProp.projectileLifetime = 4f;
            thrownProp.addConvexCollider = true;
            MarkChanged(thrownProp);
        }

        private static void UpgradeTeddyBearPropShowcase(Scene scene, GameObject showcaseRoot)
        {
            if (showcaseRoot.transform.Find(TeddyBearPropUpgradeMarker) != null) return;

            GameObject teddyBear = AssetDatabase.LoadAssetAtPath<GameObject>(TeddyBearPath);
            PropRegistrySO propRegistry = AssetDatabase.LoadAssetAtPath<PropRegistrySO>(
                "Assets/Resources/Dooms/PropRegistry.asset");
            AnimationSequenceRegistry sequenceRegistry = AssetDatabase.LoadAssetAtPath<AnimationSequenceRegistry>(
                "Assets/Resources/Dooms/AnimationSequenceRegistry.asset");
            if (teddyBear == null || propRegistry == null || sequenceRegistry == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Teddy bear prop or animation registries could not be loaded.");
                return;
            }

            PropEntry teddyEntry = propRegistry.FindProp("TeddyBear");
            if (teddyEntry == null)
            {
                teddyEntry = new PropEntry { propId = "TeddyBear" };
                propRegistry.props.Add(teddyEntry);
            }

            teddyEntry.itemId = "";
            teddyEntry.prefab = teddyBear;
            teddyEntry.defaultBone = "CC_Base_R_Hand";
            teddyEntry.localOffset = Vector3.zero;
            teddyEntry.localRotationEuler = Vector3.zero;
            teddyEntry.description = "Comfort object used by Holding_Comfort_Object.";

            ActionAnimSequence comfortSequence = sequenceRegistry.FindSequence("Holding_Comfort_Object");
            if (comfortSequence == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Holding_Comfort_Object was not found in AnimationSequenceRegistry.");
                return;
            }
            comfortSequence.startPropId = "TeddyBear";

            EditorUtility.SetDirty(propRegistry);
            EditorUtility.SetDirty(sequenceRegistry);
            AssetDatabase.SaveAssets();
            GetOrCreateChild(showcaseRoot.transform, TeddyBearPropUpgradeMarker);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DemoAnimationShowcase] Registered the TeddyBear prop for Holding_Comfort_Object.", showcaseRoot);
        }

        private static void UpgradeEatingShowcase(Scene scene, GameObject showcaseRoot)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (showcaseRoot.transform.Find(EatingUpgradeMarker) != null) return;

            GameObject benchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BenchPrefabPath);
            GameObject hamburger = AssetDatabase.LoadAssetAtPath<GameObject>(HamburgerPath);
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            PropRegistrySO propRegistry = AssetDatabase.LoadAssetAtPath<PropRegistrySO>(
                "Assets/Resources/Dooms/PropRegistry.asset");
            AnimationSequenceRegistry sequenceRegistry = AssetDatabase.LoadAssetAtPath<AnimationSequenceRegistry>(
                "Assets/Resources/Dooms/AnimationSequenceRegistry.asset");
            GameObject charactersRoot = FindInScene(scene, CharactersRootName);

            if (benchPrefab == null || hamburger == null || controller == null
                || propRegistry == null || sequenceRegistry == null || charactersRoot == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Eating showcase dependencies could not be loaded.");
                return;
            }

            ConfigureBenchPrefabAnchors();

            PropEntry hamburgerEntry = propRegistry.FindProp("Hamburger");
            if (hamburgerEntry == null)
            {
                hamburgerEntry = new PropEntry
                {
                    propId = "Hamburger",
                    itemId = "",
                    prefab = hamburger,
                    defaultBone = "CC_Base_R_Hand",
                    localOffset = Vector3.zero,
                    localRotationEuler = Vector3.zero,
                    description = "Food prop used by the Eating sequence.",
                };
                propRegistry.props.Add(hamburgerEntry);
            }
            else
            {
                hamburgerEntry.prefab = hamburger;
            }

            ActionAnimSequence eatingSequence = sequenceRegistry.FindSequence("Eating");
            if (eatingSequence == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Eating was not found in AnimationSequenceRegistry.");
                return;
            }

            eatingSequence.startPropId = "";
            eatingSequence.loopPropId = "Hamburger";
            eatingSequence.endPropId = "";

            GameObject benchInstance = FindInScene(scene, "Eating_Bench_Showcase");
            if (benchInstance == null)
            {
                benchInstance = PrefabUtility.InstantiatePrefab(benchPrefab, scene) as GameObject;
                if (benchInstance == null)
                {
                    Debug.LogError("[DemoAnimationShowcase] Failed to instantiate the bench prefab.");
                    return;
                }

                Undo.RegisterCreatedObjectUndo(benchInstance, "Create Eating bench showcase");
                benchInstance.name = "Eating_Bench_Showcase";
                benchInstance.transform.SetParent(showcaseRoot.transform, true);
            }

            Transform pose = FindRecursive(benchInstance.transform, "Pose_1");
            if (pose == null)
            {
                Debug.LogError("[DemoAnimationShowcase] Pose_1 was not found in the bench prefab instance.");
                return;
            }

            List<GameObject> demoCharacters = DirectChildren(charactersRoot.transform);
            GameObject eatingCharacter = demoCharacters.FirstOrDefault(IsEatingPreviewCharacter)
                ?? demoCharacters.FirstOrDefault(character => character.GetComponent<AnimatorActionPreview>() == null
                    && character.GetComponent<PairedAnimationParticipant>() == null);
            if (eatingCharacter == null)
            {
                Debug.LogError("[DemoAnimationShowcase] No unused character is available for the Eating preview.");
                return;
            }

            if (!ConfigureCommonCharacter(eatingCharacter, controller)) return;

            eatingCharacter.SetActive(true);
            MarkChanged(eatingCharacter);

            AnimatorActionPreview preview = EnsureComponent<AnimatorActionPreview>(eatingCharacter);
            preview.playOnStart = true;
            preview.loop = true;
            preview.pauseT4Brain = true;
            preview.startDelay = 2f;
            preview.transitionPause = 1f;
            preview.actionAnchor = pose;
            preview.alignToAnchorBeforePlayback = true;
            preview.playlist = new List<AnimatorActionPreview.PreviewStep>
            {
                new AnimatorActionPreview.PreviewStep
                {
                    sequenceId = "Eating",
                    holdSeconds = 10f,
                },
            };

            EnsureComponent<AnimatorPropDriver>(eatingCharacter);
            eatingCharacter.transform.SetPositionAndRotation(pose.position, pose.rotation);

            EditorUtility.SetDirty(propRegistry);
            EditorUtility.SetDirty(sequenceRegistry);
            MarkChanged(preview);
            MarkChanged(eatingCharacter.transform);
            AssetDatabase.SaveAssets();

            GetOrCreateChild(showcaseRoot.transform, EatingUpgradeMarker);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DemoAnimationShowcase] Added the Pose_1 Eating preview with a registry-backed Hamburger prop.", showcaseRoot);
        }

        private static bool IsEatingPreviewCharacter(GameObject character)
        {
            AnimatorActionPreview preview = character != null
                ? character.GetComponent<AnimatorActionPreview>()
                : null;
            return preview != null && preview.playlist != null
                && preview.playlist.Any(step => step != null
                    && string.Equals(step.sequenceId, "Eating", StringComparison.OrdinalIgnoreCase));
        }

        private static void ConfigureBenchPrefabAnchors()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BenchPrefabPath);
            try
            {
                for (int i = 1; i <= 4; i++)
                {
                    Transform pose = FindRecursive(prefabRoot.transform, $"Pose_{i}");
                    if (pose == null) continue;

                    TargetTransformAnchor anchor = pose.GetComponent<TargetTransformAnchor>();
                    if (anchor == null) anchor = pose.gameObject.AddComponent<TargetTransformAnchor>();
                    anchor.anchor = pose;
                    anchor.targetClass = "FoodSpot";
                    anchor.animatorStateName = "Eating";
                    anchor.holdSeconds = 10f;
                    anchor.capacity = 1;
                    anchor.infectious = false;
                    anchor.allowedFactions = Array.Empty<string>();
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, BenchPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void ConfigurePairedCharacter(
            GameObject character,
            PairedAnimationSex sex,
            RuntimeAnimatorController controller)
        {
            if (!ConfigureCommonCharacter(character, controller)) return;

            AnimatorActionPreview preview = character.GetComponent<AnimatorActionPreview>();
            if (preview != null) Undo.DestroyObjectImmediate(preview);

            PairedAnimationParticipant participant = EnsureComponent<PairedAnimationParticipant>(character);
            participant.sex = sex;
            MarkChanged(participant);
        }

        private static bool ConfigureCommonCharacter(GameObject character, RuntimeAnimatorController controller)
        {
            Animator animator = character.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[DemoAnimationShowcase] '{character.name}' has no Animator on its model root.", character);
                return false;
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            AnimatorLocomotionDriver driver = EnsureComponent<AnimatorLocomotionDriver>(character);
            driver.useControllerActionTransitions = true;
            driver.smoothAnchorAlignment = true;
            driver.anchorAlignmentDuration = 0.45f;

            NavMeshAgent navAgent = character.GetComponent<NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.enabled = false;
            }

            MarkChanged(animator);
            MarkChanged(driver);
            if (navAgent != null) MarkChanged(navAgent);
            return true;
        }

        private static void ConfigurePairedDemo(Transform showcaseRoot, GameObject male, GameObject female)
        {
            Transform pairRoot = GetOrCreateChild(showcaseRoot, "Paired_Kiss_Hug_Showcase");
            pairRoot.SetPositionAndRotation(male.transform.position, male.transform.rotation);

            Transform kissRoot = GetOrCreateChild(pairRoot, "Kiss_Anchors");
            SetLocalPose(kissRoot, Vector3.zero, Quaternion.identity);
            Transform kissMale = ConfigureAnchor(kissRoot, "Male_Anchor", Vector3.zero, Quaternion.identity);
            Transform kissFemale = ConfigureAnchor(
                kissRoot,
                "Female_Anchor",
                new Vector3(0f, 0f, 0.675f),
                Quaternion.Euler(0f, 180f, 0f));

            Transform hugRoot = GetOrCreateChild(pairRoot, "Hug_Anchors");
            SetLocalPose(hugRoot, Vector3.zero, Quaternion.identity);
            Transform hugMale = ConfigureAnchor(hugRoot, "Male_Anchor", Vector3.zero, Quaternion.identity);
            Transform hugFemale = ConfigureAnchor(
                hugRoot,
                "Female_Anchor",
                new Vector3(0f, 0f, 1.352f),
                Quaternion.Euler(0f, 180f, 0f));

            PairedAnimationDemo demo = EnsureComponent<PairedAnimationDemo>(pairRoot.gameObject);
            demo.maleParticipant = male.GetComponent<PairedAnimationParticipant>();
            demo.femaleParticipant = female.GetComponent<PairedAnimationParticipant>();
            demo.actionAnchors = new List<PairedAnimationAnchorSet>
            {
                new PairedAnimationAnchorSet
                {
                    actionId = "Kiss",
                    maleAnchor = kissMale,
                    femaleAnchor = kissFemale,
                },
                new PairedAnimationAnchorSet
                {
                    actionId = "Hug",
                    maleAnchor = hugMale,
                    femaleAnchor = hugFemale,
                },
            };
            demo.playOnStart = true;
            demo.loop = true;
            demo.startDelay = 1f;
            demo.pauseBetweenActions = 2f;
            demo.actionPlaylist = new List<string> { "Kiss", "Hug" };
            demo.arrivalDistance = 0.05f;
            demo.approachTimeout = 0.25f;
            demo.exactSettleBeforePlayback = true;
            demo.pauseAgentBrains = true;
            MarkChanged(demo);
        }

        private static Transform ConfigureAnchor(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            Transform anchor = GetOrCreateChild(parent, name);
            SetLocalPose(anchor, localPosition, localRotation);
            return anchor;
        }

        private static void SetLocalPose(Transform transform, Vector3 position, Quaternion rotation)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = Vector3.one;
            MarkChanged(transform);
        }

        private static GameObject FindNearestMale(List<GameObject> characters, GameObject female)
        {
            if (female == null) return null;

            return characters
                .Where(character => character != female && !IsFemaleCharacter(character))
                .OrderBy(character => (character.transform.position - female.transform.position).sqrMagnitude)
                .FirstOrDefault();
        }

        private static bool IsFemaleCharacter(GameObject character)
        {
            return character != null
                && character.name.IndexOf("-f-", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<GameObject> DirectChildren(Transform parent)
        {
            var result = new List<GameObject>(parent.childCount);
            for (int i = 0; i < parent.childCount; i++)
            {
                result.Add(parent.GetChild(i).gameObject);
            }
            return result;
        }

        private static void DisableLegacyPairDemos(Scene scene)
        {
            string[] legacyNames = { "Kiss_Paired_Animation_Demo", "Hug_Paired_Animation_Demo" };
            foreach (string legacyName in legacyNames)
            {
                GameObject legacy = FindInScene(scene, legacyName);
                if (legacy == null || !legacy.activeSelf) continue;
                Undo.RecordObject(legacy, "Disable legacy paired animation demo");
                legacy.SetActive(false);
                MarkChanged(legacy);
            }
        }

        private static GameObject GetOrCreateRoot(Scene scene, string name)
        {
            GameObject existing = FindInScene(scene, name);
            if (existing != null) return existing;

            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create animation showcase root");
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;

            var child = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(child.gameObject, "Create animation showcase object");
            child.SetParent(parent, false);
            return child;
        }

        private static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(gameObject);
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindRecursive(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindRecursive(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void MarkChanged(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
