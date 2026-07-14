using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.Scenes
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DOOMS/Scene Smoke Test Launcher")]
    public class SceneSmokeTestLauncher : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Optional override. Leave empty to use SceneDirector.Instance.")]
        public SceneDirector sceneDirector;

        [Tooltip("Optional override. Leave empty to use SceneRegistrySO.Instance.")]
        public SceneRegistrySO sceneRegistry;

        [Header("Selection")]
        [Tooltip("Index into the registered scene list.")]
        public int selectedSceneIndex = 0;

        [Tooltip("If true, selection wraps around when stepping next or previous.")]
        public bool wrapSelection = true;

        [Header("Smoke Test")]
        [Tooltip("If true, launches the selected scene automatically when Play Mode starts.")]
        public bool launchOnPlay = false;

        [Tooltip("If true, the current scene is deactivated before each launch.")]
        public bool deactivateBeforeLaunch = true;

        [Min(0f)]
        [Tooltip("Delay before launching a scene, in seconds.")]
        public float launchDelaySec = 0f;

        [Min(0f)]
        [Tooltip("Intensity passed to SceneDirector.ActivateScene.")]
        public float defaultIntensity = 0.75f;

        [Tooltip("If true, the launcher cycles through every registered scene instead of launching only the selected one.")]
        public bool autoCycleScenes = false;

        [Min(0f)]
        [Tooltip("How long each scene stays active during auto-cycle smoke testing before the next scene is launched.")]
        public float holdBeforeNextSceneSec = 3f;

        [Min(0f)]
        [Tooltip("Delay between scenes when auto-cycling.")]
        public float intervalBetweenScenesSec = 0.5f;

        [Tooltip("If true, the auto-cycle routine loops back to the first scene when it reaches the end.")]
        public bool loopAutoCycle = false;

        private Coroutine _launchRoutine;
        private Coroutine _cycleRoutine;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnValidate()
        {
            if (selectedSceneIndex < 0)
            {
                selectedSceneIndex = 0;
            }
        }

        private void OnDisable()
        {
            StopAllLaunches();
        }

        private void Start()
        {
            if (!Application.isPlaying || !launchOnPlay)
            {
                return;
            }

            if (autoCycleScenes)
            {
                StartAutoCycle();
            }
            else
            {
                LaunchSelectedScene();
            }
        }

        public void ResolveDependencies()
        {
            if (sceneDirector == null)
            {
                sceneDirector = SceneDirector.Instance;
            }

            if (sceneRegistry == null)
            {
                sceneRegistry = SceneRegistrySO.Instance;
            }
        }

        public void StopAllLaunches()
        {
            if (_launchRoutine != null)
            {
                StopCoroutine(_launchRoutine);
                _launchRoutine = null;
            }

            if (_cycleRoutine != null)
            {
                StopCoroutine(_cycleRoutine);
                _cycleRoutine = null;
            }
        }

        public void LaunchSelectedScene()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] LaunchSelectedScene requires Play Mode.");
                return;
            }

            ResolveDependencies();
            var scene = GetSelectedScene();
            if (scene == null)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] No valid scene selected.");
                return;
            }

            StartLaunchRoutine(scene);
        }

        public void LaunchSceneByIndex(int index)
        {
            selectedSceneIndex = index;
            LaunchSelectedScene();
        }

        public void LaunchNextScene()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] LaunchNextScene requires Play Mode.");
                return;
            }

            var count = GetSceneCount();
            if (count <= 0)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] Scene registry is empty.");
                return;
            }

            selectedSceneIndex++;
            if (selectedSceneIndex >= count)
            {
                selectedSceneIndex = wrapSelection ? 0 : count - 1;
            }

            LaunchSelectedScene();
        }

        public void LaunchPreviousScene()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] LaunchPreviousScene requires Play Mode.");
                return;
            }

            var count = GetSceneCount();
            if (count <= 0)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] Scene registry is empty.");
                return;
            }

            selectedSceneIndex--;
            if (selectedSceneIndex < 0)
            {
                selectedSceneIndex = wrapSelection ? count - 1 : 0;
            }

            LaunchSelectedScene();
        }

        public void StartAutoCycle()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] StartAutoCycle requires Play Mode.");
                return;
            }

            ResolveDependencies();
            if (sceneRegistry == null || sceneRegistry.scenes == null || sceneRegistry.scenes.Count == 0)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] Scene registry is empty.");
                return;
            }

            if (_cycleRoutine != null)
            {
                StopCoroutine(_cycleRoutine);
            }

            _cycleRoutine = StartCoroutine(AutoCycleRoutine());
        }

        private void StartLaunchRoutine(SceneSO scene)
        {
            if (_launchRoutine != null)
            {
                StopCoroutine(_launchRoutine);
            }

            _launchRoutine = StartCoroutine(LaunchSceneRoutine(scene));
        }

        private IEnumerator LaunchSceneRoutine(SceneSO scene)
        {
            if (scene == null)
            {
                yield break;
            }

            if (launchDelaySec > 0f)
            {
                yield return new WaitForSeconds(launchDelaySec);
            }

            ResolveDependencies();
            if (sceneDirector == null)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] SceneDirector is missing.");
                yield break;
            }

            if (deactivateBeforeLaunch)
            {
                sceneDirector.DeactivateScene($"Smoke test launcher switching to '{scene.sceneId}'");
            }

            sceneDirector.ActivateScene(scene.sceneId, defaultIntensity);
            _launchRoutine = null;
        }

        private IEnumerator AutoCycleRoutine()
        {
            var scenes = GetRegisteredScenes();
            if (scenes.Count == 0)
            {
                _cycleRoutine = null;
                yield break;
            }

            do
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    if (!Application.isPlaying)
                    {
                        _cycleRoutine = null;
                        yield break;
                    }

                    var scene = scenes[i];
                    if (scene == null || string.IsNullOrWhiteSpace(scene.sceneId))
                    {
                        continue;
                    }

                    selectedSceneIndex = i;
                    yield return LaunchSceneAndWait(scene);

                    if (intervalBetweenScenesSec > 0f)
                    {
                        yield return new WaitForSeconds(intervalBetweenScenesSec);
                    }
                }
            } while (loopAutoCycle && Application.isPlaying);

            _cycleRoutine = null;
        }

        private IEnumerator LaunchSceneAndWait(SceneSO scene)
        {
            if (scene == null)
            {
                yield break;
            }

            if (launchDelaySec > 0f)
            {
                yield return new WaitForSeconds(launchDelaySec);
            }

            ResolveDependencies();
            if (sceneDirector == null)
            {
                Debug.LogWarning("[DOOMS][SceneSmokeTestLauncher] SceneDirector is missing.");
                yield break;
            }

            if (deactivateBeforeLaunch)
            {
                sceneDirector.DeactivateScene($"Smoke test launcher switching to '{scene.sceneId}'");
            }

            sceneDirector.ActivateScene(scene.sceneId, defaultIntensity);

            if (holdBeforeNextSceneSec > 0f)
            {
                yield return new WaitForSeconds(holdBeforeNextSceneSec);
            }
        }

        private SceneSO GetSelectedScene()
        {
            var scenes = GetRegisteredScenes();
            if (scenes.Count == 0)
            {
                return null;
            }

            if (selectedSceneIndex < 0)
            {
                selectedSceneIndex = 0;
            }
            else if (selectedSceneIndex >= scenes.Count)
            {
                selectedSceneIndex = wrapSelection ? 0 : scenes.Count - 1;
            }

            return scenes[selectedSceneIndex];
        }

        private List<SceneSO> GetRegisteredScenes()
        {
            ResolveDependencies();
            if (sceneRegistry == null || sceneRegistry.scenes == null)
            {
                return new List<SceneSO>();
            }

            var scenes = new List<SceneSO>();
            foreach (var scene in sceneRegistry.scenes)
            {
                if (scene != null && !string.IsNullOrWhiteSpace(scene.sceneId))
                {
                    scenes.Add(scene);
                }
            }
            return scenes;
        }

        private int GetSceneCount()
        {
            return GetRegisteredScenes().Count;
        }
    }
}
