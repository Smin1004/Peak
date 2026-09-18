using System;
using System.IO;
using System.Reflection;
using Peak.Core;
using Peak.Network;
using Peak.UI;
using Peak.Visual;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Peak.Editor
{
    /// <summary>
    /// Peak > Setup > Rebuild M0 Scenes — M0 골격 씬 5개·데이터 에셋·레이어·빌드 목록을 코드로 (재)생성한다 (Docs/Prompts/M0-1_skeleton.md 4·7장).
    /// M0-2: Game·Sandbox_Climb 임시 카메라 삭제, Sandbox_Climb 임시 경사, Sandbox_ProcGen SpawnPoint 삭제, [Boot]/PlayerSpawner (Docs/Prompts/M0-2_player.md 7장).
    /// 실행 순서: Peak > Setup > Rebuild Player Prefab → 이 메뉴.
    /// 멱등: 이미 있는 씬은 열어서 같은 이름의 오브젝트를 찾아 설정만 다시 맞춘다 (fileID 가 바뀌지 않으므로 두 번 실행해도 diff 없음).
    /// 손으로 배치하지 않는다 — 씬을 바꾸려면 이 파일을 고치고 다시 실행한다 (Docs/201_common.md 3장).
    /// </summary>
    public static class M0SceneBuilder
    {
        private const string MenuPath = "Peak/Setup/Rebuild M0 Scenes";

        // ── 경로 ─────────────────────────────────────────────────────────
        internal const string ProjectRoot = "Assets/_Project";
        private const string ScenesDir = ProjectRoot + "/Scenes";
        private const string DataDir = ProjectRoot + "/Data";
        private const string FontsDir = ProjectRoot + "/UI/Fonts";
        private const string TuningAssetPath = DataDir + "/GameTuning.asset";
        internal const string ThemeAssetPath = DataDir + "/VisualTheme.asset";
        private const string NetworkPrefabsAssetPath = DataDir + "/NetworkPrefabs.asset";
        private const string FontAssetPath = FontsDir + "/MalgunGothic SDF.asset";
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";

        /// <summary>TMP 가 필수 리소스 존재 여부를 판단하는 고정 경로 (ugui 패키지 내부 하드코딩). _Project 밖이지만 TMP 제약.</summary>
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string UguiPackagePath = "Packages/com.unity.ugui";
        private const string TmpEssentialsRelativePath = "Package Resources/TMP Essential Resources.unitypackage";

        // ── 오브젝트 이름 ──────────────────────────────────────────────────
        private const string NetworkManagerName = "NetworkManager";
        private const string BootRootName = "[Boot]";
        private const string SceneFlowName = "SceneFlow";
        private const string DebugOverlayName = "DebugOverlay";
        private const string EventSystemName = "EventSystem";
        private const string MainCameraName = "Main Camera";
        private const string MainCameraTag = "MainCamera";
        private const string LightName = "Directional Light";
        private const string GroundName = "Ground";
        private const string SpawnPointName = PlayerSpawner.SpawnPointName;
        private const string PlayerSpawnerName = "PlayerSpawner";
        private const string Ramp30Name = "Temp_Ramp30";
        private const string Ramp60Name = "Temp_Ramp60";
        private const string BootstrapperName = "Bootstrapper";
        private const string LobbyCanvasName = "LobbyCanvas";

        // ── 레이어 (M0-1 프롬프트 4장: 미리 예약) ────────────────────────────
        private static readonly string[] RequiredLayers = { Layers.Terrain, Layers.Player, Layers.Rope, Layers.Item, Layers.Interactable };
        private const int FirstUserLayer = 6;
        private const int LayerCount = 32;

        // ── 월드 배치 ───────────────────────────────────────────────────────
        /// <summary>Unity Plane 프리미티브는 10 m × 10 m. 200 m 지면 = 스케일 20, 50 m = 스케일 5.</summary>
        private const float PlaneSizeMeters = 10f;
        private const float GameGroundMeters = 200f;
        private const float SandboxGroundMeters = 120f;
        private static readonly Vector3 SpawnPointPosition = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 LightEuler = new Vector3(50f, -30f, 0f);
        private static readonly Vector3 CameraPosition = new Vector3(0f, 4f, -10f);
        private static readonly Vector3 CameraEuler = new Vector3(15f, 0f, 0f);
        private static readonly Vector3 LobbyCameraPosition = new Vector3(0f, 1f, -10f);

        // ── Sandbox_Climb 임시 경사 (M0-2 7장: 경사 정지·미끄러짐 확인용. M1 벽 세트가 대체) ──
        private const float Ramp30Angle = 30f;
        private const float Ramp60Angle = 60f;
        /// <summary>폭(X) · 두께(Y) · 길이(Z) m.</summary>
        private static readonly Vector3 RampSize = new Vector3(4f, 0.5f, 6f);
        private const float Ramp30X = -5f;
        private const float Ramp60X = 5f;
        /// <summary>윗면의 낮은 모서리가 지면(y 0)에 닿는 Z. SpawnPoint(0, 1, 0) 에서 몇 걸음.</summary>
        private const float RampStartZ = 4f;

        // ── UI 수치 (Docs/204_ui.md 3장: 1920×1080 Scale With Screen Size, match 0.5) ──
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        private const float MatchWidthOrHeight = 0.5f;
        private const int OverlaySortOrder = 100;
        private const float OverlayMargin = 16f;
        private static readonly Vector2 OverlaySize = new Vector2(900f, 700f);
        private const float OverlayFontSize = 22f;
        private const float TitleFontSize = 96f;
        private const float LabelFontSize = 28f;
        private static readonly Vector2 TitleSize = new Vector2(600f, 140f);
        private const float TitleOffsetY = -140f;
        private static readonly Vector2 ControlSize = new Vector2(320f, 56f);
        private const float SeedLabelY = 90f;
        private const float SeedInputY = 30f;
        private const float StartButtonY = -60f;
        private const float QuitButtonY = -140f;

        // ── 폰트: 한글 표시용 OS 폰트 (Pretendard 는 Docs/102 ⚠ 미확정 — 들어오면 교체) ──
        private const string FontFamilyName = "Malgun Gothic";
        private const string FontStyleName = "Regular";
        private const int FontPointSize = 90;
        /// <summary>
        /// 생성 시 미리 아틀라스에 넣는 글자. DynamicOS 폰트는 새 글자를 그릴 때마다 아틀라스가 자라 에셋이 바뀌므로
        /// 오버레이·로비가 쓰는 ASCII 와 한글을 미리 넣어 재실행·플레이 후 diff 를 줄인다.
        /// </summary>
        private const string PrepopulatedCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "시드싱글작종료";

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log.Warn(LogCategory.Core, "플레이 모드에서는 씬을 재생성할 수 없다");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureFolders();
            if (!EnsureTmpEssentials())
            {
                Log.Error(LogCategory.Core, "TMP Essential Resources 가져오기가 끝나지 않았다. 메뉴를 한 번 더 실행할 것");
                return;
            }
            EnsureLayers();

            // 에셋은 여기서 존재만 보장한다. 참조는 각 씬을 연 뒤 다시 로드한다 — EditorSceneManager.OpenScene(Single) 이
            // 씬에서 참조되지 않는 에셋을 언로드해 로컬 변수의 참조가 fake-null 이 되기 때문 (M0-1 에서 실제로 겪음)
            EnsureAsset<GameTuning>(TuningAssetPath);
            // 코드에 필드가 추가되면 에셋 파일에도 기본값을 써 둔다 ("에셋이 진실", 201 4장). 내용이 같으면 파일도 같다
            AssetDatabase.ForceReserializeAssets(new[] { TuningAssetPath });
            EnsureAsset<VisualTheme>(ThemeAssetPath);
            EnsureNetworkPrefabsList();
            EnsureKoreanFont();

            BuildScene(SceneNames.Boot, BuildBoot);
            BuildScene(SceneNames.Lobby, BuildLobby);
            BuildScene(SceneNames.Game, scene => BuildGame(scene, GameGroundMeters));
            BuildScene(SceneNames.SandboxClimb, BuildSandboxClimb);
            BuildScene(SceneNames.SandboxProcGen, BuildSandboxProcGen);

            EnsureBuildScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath(SceneNames.Boot), OpenSceneMode.Single);
            Log.Info(LogCategory.Core, "Rebuild M0 Scenes 완료: Boot, Lobby, Game, Sandbox_Climb, Sandbox_ProcGen / GameTuning, VisualTheme, NetworkPrefabs");
        }

        // ── 준비 ─────────────────────────────────────────────────────────

        private static void EnsureFolders()
        {
            EnsureFolder(ScenesDir);
            EnsureFolder(DataDir);
            EnsureFolder(FontsDir);
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>TMP 필수 리소스가 없으면 ugui 패키지의 .unitypackage 를 비대화형으로 가져온다.</summary>
        private static bool EnsureTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath))
            {
                return true;
            }
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(UguiPackagePath);
            if (packageInfo == null)
            {
                Log.Error(LogCategory.Core, "com.unity.ugui 패키지를 찾을 수 없다");
                return false;
            }
            string packagePath = Path.Combine(packageInfo.resolvedPath, TmpEssentialsRelativePath);
            if (!File.Exists(packagePath))
            {
                Log.Error(LogCategory.Core, $"TMP Essential Resources 패키지가 없다: {packagePath}");
                return false;
            }
            Log.Info(LogCategory.Core, "TMP Essential Resources 가져오기");
            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh();
            return File.Exists(TmpSettingsPath);
        }

        private static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(TagManagerPath)[0]);
            var layers = tagManager.FindProperty("layers");
            bool changed = false;
            foreach (var layerName in RequiredLayers)
            {
                if (FindLayerIndex(layers, layerName) >= 0)
                {
                    continue;
                }
                int slot = FindEmptyLayerSlot(layers);
                if (slot < 0)
                {
                    Log.Error(LogCategory.Core, $"레이어 슬롯이 없어 {layerName} 를 추가하지 못했다");
                    continue;
                }
                layers.GetArrayElementAtIndex(slot).stringValue = layerName;
                changed = true;
                Log.Info(LogCategory.Core, $"레이어 추가: {layerName} = {slot}");
            }
            if (changed)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static int FindLayerIndex(SerializedProperty layers, string name)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindEmptyLayerSlot(SerializedProperty layers)
        {
            for (int i = FirstUserLayer; i < Mathf.Min(LayerCount, layers.arraySize); i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    return i;
                }
            }
            return -1;
        }

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            Log.Info(LogCategory.Core, $"에셋 생성: {path}");
            return asset;
        }

        /// <summary>
        /// NGO 네트워크 프리팹 목록. NGO 는 기본 목록을 Assets/DefaultNetworkPrefabs.asset 에 자동 생성하므로
        /// 프로젝트 설정의 경로를 _Project/Data 로 바꿔 우리 폴더 규칙(201 3장)을 지킨다. M0-2 가 Player 프리팹을 여기 등록한다.
        /// </summary>
        internal static NetworkPrefabsList EnsureNetworkPrefabsList()
        {
            var settings = NetcodeForGameObjectsProjectSettings.instance;
            if (settings.NetworkPrefabsPath != NetworkPrefabsAssetPath)
            {
                settings.NetworkPrefabsPath = NetworkPrefabsAssetPath;
                var save = typeof(NetcodeForGameObjectsProjectSettings).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (save != null)
                {
                    save.Invoke(settings, null);
                }
                else
                {
                    Log.Warn(LogCategory.Core, "NetcodeForGameObjectsProjectSettings.SaveSettings 를 찾지 못했다 — 경로 변경이 세션에만 유지된다");
                }
            }

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsAssetPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, NetworkPrefabsAssetPath);
                Log.Info(LogCategory.Core, $"에셋 생성: {NetworkPrefabsAssetPath}");
            }
            SetBool(list, "IsDefault", true);
            return list;
        }

        /// <summary>한글 표시용 DynamicOS 폰트 에셋 (Windows 기본 맑은 고딕). 없으면 null 을 돌려주고 TMP 기본 폰트를 쓴다.</summary>
        private static TMP_FontAsset EnsureKoreanFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                return existing;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(FontFamilyName, FontStyleName, FontPointSize);
            if (fontAsset == null)
            {
                Log.Warn(LogCategory.UI, $"OS 폰트 {FontFamilyName} 를 찾지 못했다 — TMP 기본 폰트 사용 (한글이 깨질 수 있음)");
                return null;
            }

            string assetName = Path.GetFileNameWithoutExtension(FontAssetPath);
            fontAsset.name = assetName;
            fontAsset.TryAddCharacters(PrepopulatedCharacters);
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            if (fontAsset.material != null)
            {
                fontAsset.material.name = assetName + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
            if (fontAsset.atlasTexture != null)
            {
                fontAsset.atlasTexture.name = assetName + " Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            }
            AssetDatabase.SaveAssets();
            Log.Info(LogCategory.UI, $"폰트 에셋 생성: {FontAssetPath}");
            return fontAsset;
        }

        // ── 씬 공통 ──────────────────────────────────────────────────────

        private static string ScenePath(string sceneName) => $"{ScenesDir}/{sceneName}.unity";

        internal static void BuildScene(string sceneName, Action<Scene> populate)
        {
            string path = ScenePath(sceneName);
            bool isNew = !File.Exists(path);
            Scene scene = isNew
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            populate(scene);
            RefreshNetworkObjectHashes(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                Log.Error(LogCategory.Core, $"씬 저장 실패: {path}");
                return;
            }

            if (isNew)
            {
                // 새 씬의 in-scene NetworkObject 는 씬이 저장돼 GUID 가 생긴 뒤에야 GlobalObjectIdHash 가 확정된다 → 다시 열어 한 번 더 (멱등)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                populate(scene);
                RefreshNetworkObjectHashes(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, path);
            }
            Log.Info(LogCategory.Core, $"씬 {(isNew ? "생성" : "갱신")}: {path}");
        }

        /// <summary>NGO 는 in-scene NetworkObject 해시를 OnValidate 에서 계산한다 (internal). 저장 직전에 강제로 한 번 돌린다.</summary>
        private static void RefreshNetworkObjectHashes(Scene scene)
        {
            var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (validate == null)
            {
                return;
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    validate.Invoke(networkObject, null);
                }
            }
        }

        internal static GameObject EnsureRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        internal static GameObject EnsureChild(GameObject parent, string name)
        {
            var existing = parent.transform.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static GameObject EnsurePrimitive(Scene scene, string name, PrimitiveType type)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        internal static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        internal static void SetRef(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Log.Error(LogCategory.Core, $"{target.GetType().Name} 에 직렬화 필드 {property} 가 없다");
                return;
            }
            if (prop.objectReferenceValue != value)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>씬을 연 뒤 다시 로드하는 필수 에셋. 없으면 에러 (Rebuild 앞단의 Ensure 가 만들었어야 한다).</summary>
        internal static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Log.Error(LogCategory.Core, $"필수 에셋이 없다: {path}");
            }
            return asset;
        }

        private static void SetBool(Object target, string property, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Log.Error(LogCategory.Core, $"{target.GetType().Name} 에 직렬화 필드 {property} 가 없다");
                return;
            }
            if (prop.boolValue != value)
            {
                prop.boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static void SetTransform(GameObject go, Vector3 position, Vector3 euler, Vector3 scale)
        {
            var t = go.transform;
            t.localPosition = position;
            t.localEulerAngles = euler;
            t.localScale = scale;
        }

        private static GameObject EnsureCamera(Scene scene, Vector3 position, Vector3 euler, Color background)
        {
            var go = EnsureRoot(scene, MainCameraName);
            go.tag = MainCameraTag;
            var camera = EnsureComponent<Camera>(go);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            SetTransform(go, position, euler, Vector3.one);
            return go;
        }

        private static void EnsureDirectionalLight(Scene scene)
        {
            var go = EnsureRoot(scene, LightName);
            var light = EnsureComponent<Light>(go);
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            SetTransform(go, Vector3.zero, LightEuler, Vector3.one);
        }

        private static void EnsureSpawnPoint(Scene scene)
        {
            var go = EnsureRoot(scene, SpawnPointName);
            SetTransform(go, SpawnPointPosition, Vector3.zero, Vector3.one);
        }

        private static void EnsureBootstrapper(Scene scene)
        {
            var go = EnsureRoot(scene, BootstrapperName);
            EnsureComponent<Bootstrapper>(go);
        }

        private static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static TextMeshProUGUI EnsureText(GameObject parent, string name, string content, float fontSize, Color color, TMP_FontAsset font, TextAlignmentOptions alignment)
        {
            var go = EnsureChild(parent, name);
            var text = EnsureComponent<TextMeshProUGUI>(go);
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            if (font != null)
            {
                text.font = font;
            }
            return text;
        }

        // ── Boot ─────────────────────────────────────────────────────────

        private static void BuildBoot(Scene scene)
        {
            var tuning = LoadRequired<GameTuning>(TuningAssetPath);
            var theme = LoadRequired<VisualTheme>(ThemeAssetPath);
            var prefabsList = LoadRequired<NetworkPrefabsList>(NetworkPrefabsAssetPath);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabBuilder.PlayerPrefabPath);
            if (playerPrefab == null)
            {
                Log.Error(LogCategory.Core, $"{PlayerPrefabBuilder.PlayerPrefabPath} 이 없다 — Peak > Setup > Rebuild Player Prefab 먼저 실행한 뒤 Rebuild M0 Scenes 를 다시");
            }

            // NetworkManager: NGO 가 부모 오브젝트를 금지하므로 [Boot] 밖 별도 루트. 스스로 DontDestroyOnLoad 한다
            var managerGo = EnsureRoot(scene, NetworkManagerName);
            var transport = EnsureComponent<UnityTransport>(managerGo);
            var manager = EnsureComponent<NetworkManager>(managerGo);
            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.EnableSceneManagement = true;
            // 자동 스폰 금지 — 플레이어 스폰은 [Boot]/PlayerSpawner (M0-2 6장)
            manager.NetworkConfig.PlayerPrefab = null;
            var lists = manager.NetworkConfig.Prefabs.NetworkPrefabsLists;
            if (lists.Count != 1 || lists[0] != prefabsList)
            {
                lists.Clear();
                lists.Add(prefabsList);
            }
            transport.ConnectionData = new UnityTransport.ConnectionAddressData
            {
                Address = NetService.LocalAddress,
                Port = NetService.LocalPort,
                ServerListenAddress = NetService.LocalAddress,
            };
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(transport);

            // [Boot]: BootRoot + in-scene NetworkObject + GameManager. 자식: SceneFlow, DebugOverlay, EventSystem
            var boot = EnsureRoot(scene, BootRootName);
            EnsureComponent<BootRoot>(boot);
            EnsureComponent<NetworkObject>(boot);
            var gameManager = EnsureComponent<GameManager>(boot);
            SetRef(gameManager, "tuning", tuning);
            SetRef(gameManager, "theme", theme);

            EnsureComponent<SceneFlow>(EnsureChild(boot, SceneFlowName));

            var overlayGo = EnsureChild(boot, DebugOverlayName);
            var canvas = EnsureComponent<Canvas>(overlayGo);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortOrder;
            ConfigureScaler(EnsureComponent<CanvasScaler>(overlayGo));
            var overlayText = EnsureText(overlayGo, "Text", "Debug overlay (F3)", OverlayFontSize, theme.uiText, font, TextAlignmentOptions.TopLeft);
            SetRect(overlayText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(OverlayMargin, -OverlayMargin), OverlaySize);
            var overlay = EnsureComponent<DebugOverlay>(overlayGo);
            SetRef(overlay, "canvas", canvas);
            SetRef(overlay, "text", overlayText);

            var eventSystemGo = EnsureChild(boot, EventSystemName);
            EnsureComponent<EventSystem>(eventSystemGo);
            EnsureComponent<InputSystemUIInputModule>(eventSystemGo);

            // PlayerSpawner: [Boot] in-scene NetworkObject 의 자식 NetworkBehaviour (호스트 전용 로직)
            var spawner = EnsureComponent<PlayerSpawner>(EnsureChild(boot, PlayerSpawnerName));
            SetRef(spawner, "playerPrefab", playerPrefab);
        }

        // ── Lobby ────────────────────────────────────────────────────────

        private static void BuildLobby(Scene scene)
        {
            var theme = LoadRequired<VisualTheme>(ThemeAssetPath);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

            EnsureCamera(scene, LobbyCameraPosition, Vector3.zero, theme.uiBackground);

            var canvasGo = EnsureRoot(scene, LobbyCanvasName);
            var canvas = EnsureComponent<Canvas>(canvasGo);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            ConfigureScaler(EnsureComponent<CanvasScaler>(canvasGo));
            EnsureComponent<GraphicRaycaster>(canvasGo);

            var background = EnsureComponent<Image>(EnsureChild(canvasGo, "Background"));
            background.color = theme.uiBackground;
            SetRect(background.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var title = EnsureText(canvasGo, "Title", "Peak", TitleFontSize, theme.uiText, font, TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, TitleOffsetY), TitleSize);

            var seedLabel = EnsureText(canvasGo, "SeedLabel", "시드", LabelFontSize, theme.uiText, font, TextAlignmentOptions.Center);
            SetRect(seedLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, SeedLabelY), ControlSize);

            var resources = UiResources();
            var seedInput = EnsureInputField(canvasGo, "SeedInput", resources, font, theme);
            SetRect((RectTransform)seedInput.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, SeedInputY), ControlSize);

            var startButton = EnsureButton(canvasGo, "StartSingleButton", "싱글 시작", resources, font, theme);
            SetRect((RectTransform)startButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, StartButtonY), ControlSize);

            var quitButton = EnsureButton(canvasGo, "QuitButton", "종료", resources, font, theme);
            SetRect((RectTransform)quitButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, QuitButtonY), ControlSize);

            var lobbyUi = EnsureComponent<LobbyUI>(canvasGo);
            SetRef(lobbyUi, "seedInput", seedInput);
            SetRef(lobbyUi, "startSingleButton", startButton);
            SetRef(lobbyUi, "quitButton", quitButton);
        }

        private static TMP_DefaultControls.Resources UiResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = Builtin("UI/Skin/UISprite.psd"),
                background = Builtin("UI/Skin/Background.psd"),
                inputField = Builtin("UI/Skin/InputFieldBackground.psd"),
                knob = Builtin("UI/Skin/Knob.psd"),
                checkmark = Builtin("UI/Skin/Checkmark.psd"),
                dropdown = Builtin("UI/Skin/DropdownArrow.psd"),
                mask = Builtin("UI/Skin/UIMask.psd"),
            };
        }

        private static Sprite Builtin(string path) => AssetDatabase.GetBuiltinExtraResource<Sprite>(path);

        private static TMP_InputField EnsureInputField(GameObject parent, string name, TMP_DefaultControls.Resources resources, TMP_FontAsset font, VisualTheme theme)
        {
            var existing = parent.transform.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = TMP_DefaultControls.CreateInputField(resources);
                go.name = name;
                go.transform.SetParent(parent.transform, false);
            }
            var input = go.GetComponent<TMP_InputField>();
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.text = RunConfig.DefaultSeed.ToString();
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.fontSize = LabelFontSize;
                text.alignment = TextAlignmentOptions.Center;
                if (font != null)
                {
                    text.font = font;
                }
            }
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "시드";
            }
            return input;
        }

        private static Button EnsureButton(GameObject parent, string name, string label, TMP_DefaultControls.Resources resources, TMP_FontAsset font, VisualTheme theme)
        {
            var existing = parent.transform.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = TMP_DefaultControls.CreateButton(resources);
                go.name = name;
                go.transform.SetParent(parent.transform, false);
            }
            var button = go.GetComponent<Button>();
            var image = go.GetComponent<Image>();
            if (image != null)
            {
                image.color = theme.uiAccent;
            }
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.fontSize = LabelFontSize;
                text.color = theme.uiBackground;
                text.alignment = TextAlignmentOptions.Center;
                if (font != null)
                {
                    text.font = font;
                }
            }
            return button;
        }

        // ── Game / Sandbox ───────────────────────────────────────────────

        /// <summary>
        /// 임시 지면 + SpawnPoint + Directional Light + Bootstrapper. 카메라는 플레이어 CameraRig 프리팹(오너 스폰 시 생성)이 맡으므로
        /// M0-1 의 임시 Main Camera 를 지운다 (M0-2 7장). EnsureRoot 는 삭제를 하지 않으므로 명시적으로.
        /// </summary>
        private static void BuildGame(Scene scene, float groundMeters)
        {
            var ground = EnsurePrimitive(scene, GroundName, PrimitiveType.Plane);
            float scale = groundMeters / PlaneSizeMeters;
            SetTransform(ground, Vector3.zero, Vector3.zero, new Vector3(scale, 1f, scale));
            ground.layer = LayerMask.NameToLayer(Layers.Terrain);

            EnsureSpawnPoint(scene);
            EnsureDirectionalLight(scene);
            EnsureBootstrapper(scene);
            DeleteRoot(scene, MainCameraName);
        }

        /// <summary>Game 과 같은 구성 + 임시 경사 30°·60° (경사 정지·미끄러짐 확인용, M1 벽 세트가 대체).</summary>
        private static void BuildSandboxClimb(Scene scene)
        {
            BuildGame(scene, SandboxGroundMeters);
            EnsureRamp(scene, Ramp30Name, Ramp30Angle, Ramp30X);
            EnsureRamp(scene, Ramp60Name, Ramp60Angle, Ramp60X);
        }

        /// <summary>
        /// 플레이어 없이 호스트·오버레이·기존 카메라만. 스폰 방식은 M2 가 생성 결과로 정하므로 SpawnPoint 를 지운다 (M0-2 7장)
        /// → PlayerSpawner 는 SpawnPoint 가 없어 스폰하지 않는다.
        /// </summary>
        private static void BuildSandboxProcGen(Scene scene)
        {
            DeleteRoot(scene, SpawnPointName);
            EnsureBootstrapper(scene);
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemeAssetPath);
            EnsureCamera(scene, CameraPosition, CameraEuler, theme != null ? theme.uiBackground : Color.black);
        }

        /// <summary>
        /// 경사면 큐브. X 축으로 기울여 +Z 로 올라가게 하고, 윗면의 낮은 모서리가 지면(y 0)·<see cref="RampStartZ"/> 에 오게 둔다 → 지면에서 턱 없이 걸어 올라탄다.
        /// </summary>
        private static void EnsureRamp(Scene scene, string name, float angleDegrees, float x)
        {
            var ramp = EnsurePrimitive(scene, name, PrimitiveType.Cube);
            float radians = angleDegrees * Mathf.Deg2Rad;
            float halfLength = RampSize.z * 0.5f;
            float halfThickness = RampSize.y * 0.5f;
            float y = halfLength * Mathf.Sin(radians) - halfThickness * Mathf.Cos(radians);
            float z = RampStartZ + halfLength * Mathf.Cos(radians) + halfThickness * Mathf.Sin(radians);
            SetTransform(ramp, new Vector3(x, y, z), new Vector3(-angleDegrees, 0f, 0f), RampSize);
            ramp.layer = LayerMask.NameToLayer(Layers.Terrain);
        }

        /// <summary>같은 이름의 루트 오브젝트를 모두 지운다 (Ensure* 는 삭제를 하지 않는다).</summary>
        private static void DeleteRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    Object.DestroyImmediate(root);
                    Log.Info(LogCategory.Core, $"{scene.name}: 루트 {name} 삭제");
                }
            }
        }

        // ── Build Settings ───────────────────────────────────────────────

        private static void EnsureBuildScenes()
        {
            string[] wanted = { ScenePath(SceneNames.Boot), ScenePath(SceneNames.Lobby), ScenePath(SceneNames.Game) };
            var current = EditorBuildSettings.scenes;
            bool same = current.Length == wanted.Length;
            for (int i = 0; same && i < wanted.Length; i++)
            {
                same = current[i].enabled && current[i].path == wanted[i];
            }
            if (same)
            {
                return;
            }
            var scenes = new EditorBuildSettingsScene[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                scenes[i] = new EditorBuildSettingsScene(wanted[i], true);
            }
            EditorBuildSettings.scenes = scenes;
            Log.Info(LogCategory.Core, "Build Settings 씬 목록: Boot(0), Lobby(1), Game(2)");
        }
    }
}
