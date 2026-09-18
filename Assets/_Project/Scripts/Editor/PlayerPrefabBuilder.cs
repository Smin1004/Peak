using System;
using System.IO;
using System.Reflection;
using Peak.Core;
using Peak.Player;
using Peak.Visual;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Peak.Editor
{
    /// <summary>
    /// Peak > Setup > Rebuild Player Prefab — Player.prefab · CameraRig.prefab · 마찰 0 PhysicsMaterial 을 코드로 (재)생성하고
    /// NetworkPrefabs.asset 에 Player 를 등록한다 (Docs/Prompts/M0-2_player.md 1·4장).
    /// 멱등: 있는 프리팹은 LoadPrefabContents 로 열어 같은 이름의 자식·컴포넌트를 찾아 값만 다시 맞춘다 (fileID 유지 → 두 번 실행해도 diff 없음).
    /// 실행 순서: 이 메뉴 → Peak > Setup > Rebuild M0 Scenes (씬 빌더가 Player 프리팹을 PlayerSpawner 에 연결한다).
    /// </summary>
    public static class PlayerPrefabBuilder
    {
        private const string MenuPath = "Peak/Setup/Rebuild Player Prefab";

        // ── 경로 (201 3장: 프리팹 Prefabs/, 머티리얼류 Art/) ──────────────────
        private const string PrefabsDir = M0SceneBuilder.ProjectRoot + "/Prefabs";
        private const string ArtDir = M0SceneBuilder.ProjectRoot + "/Art";
        internal const string PlayerPrefabPath = PrefabsDir + "/Player.prefab";
        internal const string CameraRigPrefabPath = PrefabsDir + "/CameraRig.prefab";
        private const string ZeroFrictionMaterialPath = ArtDir + "/PlayerZeroFriction.physicMaterial";

        // ── 오브젝트 이름 ──────────────────────────────────────────────────
        private const string CameraTargetName = "CameraTarget";
        private const string VisualName = "Visual";
        private const string CapsuleName = "Capsule";
        private const string NoseName = "Nose";
        private const string RigCameraName = "Camera";
        private const string RigVirtualCameraName = "CinemachineCamera";
        private const string MainCameraTag = "MainCamera";

        // ── 플레이어 기하 (202 2장: 캡슐 1.8 × 0.35, 102 2장 4번: 모든 스케일의 기준) ──
        private const float CapsuleHeight = 1.8f;
        private const float CapsuleRadius = 0.35f;
        /// <summary>CapsuleCollider.direction: 0 = X, 1 = Y, 2 = Z.</summary>
        private const int CapsuleDirectionY = 1;
        private const float PlayerMass = 70f;
        /// <summary>카메라가 따라가는 점 높이 (머리 근처).</summary>
        private const float CameraTargetHeight = 1.5f;
        /// <summary>Unity Capsule 프리미티브 메시: 높이 2 m, 반지름 0.5 m.</summary>
        private const float PrimitiveCapsuleHeight = 2f;
        private const float PrimitiveCapsuleRadius = 0.5f;
        /// <summary>전방(+Z) 표시용 코 큐브 크기 m.</summary>
        private static readonly Vector3 NoseSize = new Vector3(0.12f, 0.08f, 0.2f);
        private const float NoseHeight = 1.5f;

        // ── 카메라 리그 (202 2장: Third Person Follow, 거리 4 m, 짧은 댐핑 / D14: Deoccluder) ──
        private static readonly Vector3 ShoulderOffset = new Vector3(0.5f, 0.2f, 0f);
        private const float VerticalArmLength = 0.4f;
        /// <summary>1 = 오른쪽 어깨.</summary>
        private const float CameraSide = 1f;
        private const float CameraDistance = 4f;
        private static readonly Vector3 FollowDamping = new Vector3(0.1f, 0.1f, 0.1f);
        private const float DeoccluderMinimumDistanceFromTarget = 0.3f;
        private const float DeoccluderCameraRadius = 0.2f;
        private const int DeoccluderMaximumEffort = 4;
        private const float DeoccluderDamping = 0.2f;
        private const float DeoccluderDampingWhenOccluded = 0f;

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log.Warn(LogCategory.Player, "플레이 모드에서는 프리팹을 재생성할 수 없다");
                return;
            }

            M0SceneBuilder.EnsureFolder(PrefabsDir);
            M0SceneBuilder.EnsureFolder(ArtDir);
            EnsureZeroFrictionMaterial();

            // 참조 에셋은 각 populate 안에서 다시 로드한다 (프리팹 저장·임포트 후 로컬 참조가 fake-null 이 되는 것 방지, M0SceneBuilder 와 같은 방식)
            BuildPrefab(CameraRigPrefabPath, PopulateCameraRig);
            BuildPrefab(PlayerPrefabPath, PopulatePlayer);
            RegisterPlayerNetworkPrefab();

            AssetDatabase.SaveAssets();
            Log.Info(LogCategory.Player, $"Rebuild Player Prefab 완료: {PlayerPrefabPath}, {CameraRigPrefabPath}, {ZeroFrictionMaterialPath}. 다음: Peak > Setup > Rebuild M0 Scenes");
        }

        // ── 에셋 ─────────────────────────────────────────────────────────

        /// <summary>캡슐용 마찰 0 재질. 정지·경사 처리는 PlayerController 가 속도로 한다 (벽에 비비며 걸릴 때 끼임 방지).</summary>
        private static void EnsureZeroFrictionMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ZeroFrictionMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial(Path.GetFileNameWithoutExtension(ZeroFrictionMaterialPath));
                AssetDatabase.CreateAsset(material, ZeroFrictionMaterialPath);
                Log.Info(LogCategory.Player, $"에셋 생성: {ZeroFrictionMaterialPath}");
            }
            material.dynamicFriction = 0f;
            material.staticFriction = 0f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// 프리팹이 없으면 새 GameObject 로 만들어 저장한 뒤 한 번 더 열어 채운다 (NetworkObject 해시는 에셋 GUID 가 생긴 뒤 확정).
        /// 있으면 LoadPrefabContents 로 열어 채우고 저장한다.
        /// </summary>
        private static void BuildPrefab(string path, Action<GameObject> populate)
        {
            bool isNew = AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;
            if (isNew)
            {
                var created = new GameObject(Path.GetFileNameWithoutExtension(path));
                try
                {
                    populate(created);
                    PrefabUtility.SaveAsPrefabAsset(created, path);
                }
                finally
                {
                    Object.DestroyImmediate(created);
                }
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                populate(root);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved)
                {
                    Log.Error(LogCategory.Player, $"프리팹 저장 실패: {path}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            RefreshNetworkPrefabHash(path);
            Log.Info(LogCategory.Player, $"프리팹 {(isNew ? "생성" : "갱신")}: {path}");
        }

        /// <summary>
        /// NGO 는 NetworkObject 의 GlobalObjectIdHash 를 OnValidate(internal) 에서 계산한다. 에셋에 대해 한 번 강제로 돌리고 프리팹을 다시 저장한다.
        /// 저장이 없으면 새 프리팹 첫 저장 때(아직 씬 오브젝트일 때) 계산된 해시가 파일에 남는다 — 에디터는 로드 때 다시 계산해 가려지지만
        /// 빌드는 파일 값을 쓰므로 에디터 호스트 ↔ 빌드 클라이언트 해시가 어긋난다. 내용이 같으면 파일도 같다 (멱등).
        /// </summary>
        private static void RefreshNetworkPrefabHash(string path)
        {
            var networkObject = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
            if (networkObject == null)
            {
                return;
            }
            var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            validate?.Invoke(networkObject, null);
            PrefabUtility.SavePrefabAsset(networkObject.gameObject, out bool saved);
            if (!saved)
            {
                Log.Error(LogCategory.Player, $"NetworkObject 해시 저장 실패: {path}");
            }
        }

        // ── Player ───────────────────────────────────────────────────────

        private static void PopulatePlayer(GameObject root)
        {
            var friction = M0SceneBuilder.LoadRequired<PhysicsMaterial>(ZeroFrictionMaterialPath);
            var rigPrefab = M0SceneBuilder.LoadRequired<GameObject>(CameraRigPrefabPath);
            int playerLayer = LayerMask.NameToLayer(Layers.Player);
            if (playerLayer < 0)
            {
                Log.Error(LogCategory.Player, $"레이어 {Layers.Player} 가 없다 — Peak > Setup > Rebuild M0 Scenes 를 한 번 실행해 레이어를 만든 뒤 다시");
                return;
            }

            root.layer = playerLayer;
            M0SceneBuilder.EnsureComponent<NetworkObject>(root);

            var body = M0SceneBuilder.EnsureComponent<Rigidbody>(root);
            body.isKinematic = false;
            body.useGravity = true;
            body.mass = PlayerMass;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var capsule = M0SceneBuilder.EnsureComponent<CapsuleCollider>(root);
            capsule.direction = CapsuleDirectionY;
            capsule.height = CapsuleHeight;
            capsule.radius = CapsuleRadius;
            capsule.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);
            capsule.sharedMaterial = friction;

            // 205 3장: 위치·회전은 오너 권한 NetworkTransform, 보간 켬. NGO 2.13 은 AuthorityMode 속성으로 오너 권한을 고른다
            var networkTransform = M0SceneBuilder.EnsureComponent<NetworkTransform>(root);
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.Interpolate = true;
            EditorUtility.SetDirty(networkTransform);

            // 비오너 인스턴스의 Rigidbody 를 kinematic 으로 (AutoUpdateKinematicState 기본값 true). 나머지 옵션 기본값
            M0SceneBuilder.EnsureComponent<NetworkRigidbody>(root);

            var cameraRig = M0SceneBuilder.EnsureComponent<PlayerCameraRig>(root);
            M0SceneBuilder.EnsureComponent<PlayerController>(root);

            var cameraTarget = M0SceneBuilder.EnsureChild(root, CameraTargetName);
            cameraTarget.layer = playerLayer;
            M0SceneBuilder.SetTransform(cameraTarget, new Vector3(0f, CameraTargetHeight, 0f), Vector3.zero, Vector3.one);

            M0SceneBuilder.SetRef(cameraRig, "rigPrefab", rigPrefab);
            M0SceneBuilder.SetRef(cameraRig, "cameraTarget", cameraTarget.transform);

            // Visual (102 2장): 프리미티브 + IVisualState. 콜라이더 없음
            var visual = M0SceneBuilder.EnsureChild(root, VisualName);
            visual.layer = playerLayer;
            M0SceneBuilder.SetTransform(visual, Vector3.zero, Vector3.zero, Vector3.one);
            M0SceneBuilder.EnsureComponent<PlayerVisual>(visual);

            var capsuleMesh = EnsurePrimitiveChild(visual, CapsuleName, PrimitiveType.Capsule, playerLayer);
            var capsuleScale = new Vector3(
                CapsuleRadius / PrimitiveCapsuleRadius,
                CapsuleHeight / PrimitiveCapsuleHeight,
                CapsuleRadius / PrimitiveCapsuleRadius);
            M0SceneBuilder.SetTransform(capsuleMesh, new Vector3(0f, CapsuleHeight * 0.5f, 0f), Vector3.zero, capsuleScale);

            var nose = EnsurePrimitiveChild(visual, NoseName, PrimitiveType.Cube, playerLayer);
            M0SceneBuilder.SetTransform(nose, new Vector3(0f, NoseHeight, CapsuleRadius), Vector3.zero, NoseSize);
        }

        /// <summary>프리미티브 자식을 찾거나 만들고 콜라이더를 제거한다 (Visual 은 콜라이더 없음, 102 2장 2번).</summary>
        private static GameObject EnsurePrimitiveChild(GameObject parent, string name, PrimitiveType type, int layer)
        {
            var existing = parent.transform.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(parent.transform, false);
            }
            foreach (var collider in go.GetComponents<Collider>())
            {
                Object.DestroyImmediate(collider, true);
            }
            go.layer = layer;
            return go;
        }

        // ── CameraRig ────────────────────────────────────────────────────

        private static void PopulateCameraRig(GameObject root)
        {
            var theme = M0SceneBuilder.LoadRequired<VisualTheme>(M0SceneBuilder.ThemeAssetPath);

            var cameraGo = M0SceneBuilder.EnsureChild(root, RigCameraName);
            cameraGo.tag = MainCameraTag;
            var camera = M0SceneBuilder.EnsureComponent<Camera>(cameraGo);
            camera.clearFlags = CameraClearFlags.SolidColor;
            if (theme != null)
            {
                camera.backgroundColor = theme.uiBackground;
            }
            M0SceneBuilder.EnsureComponent<CinemachineBrain>(cameraGo);

            var virtualCameraGo = M0SceneBuilder.EnsureChild(root, RigVirtualCameraName);
            M0SceneBuilder.EnsureComponent<CinemachineCamera>(virtualCameraGo);

            var follow = M0SceneBuilder.EnsureComponent<CinemachineThirdPersonFollow>(virtualCameraGo);
            follow.ShoulderOffset = ShoulderOffset;
            follow.VerticalArmLength = VerticalArmLength;
            follow.CameraSide = CameraSide;
            follow.CameraDistance = CameraDistance;
            follow.Damping = FollowDamping;
            // 지형 관통 방지는 Deoccluder 하나만 (D14). 내장 장애물 회피를 같이 켜면 두 보정이 겹쳐 떨린다
            var builtInAvoidance = follow.AvoidObstacles;
            builtInAvoidance.Enabled = false;
            follow.AvoidObstacles = builtInAvoidance;
            EditorUtility.SetDirty(follow);

            var deoccluder = M0SceneBuilder.EnsureComponent<CinemachineDeoccluder>(virtualCameraGo);
            deoccluder.CollideAgainst = LayerMask.GetMask(Layers.Terrain);
            deoccluder.IgnoreTag = string.Empty;
            deoccluder.TransparentLayers = 0;
            deoccluder.MinimumDistanceFromTarget = DeoccluderMinimumDistanceFromTarget;
            var avoid = deoccluder.AvoidObstacles;
            avoid.Enabled = true;
            avoid.DistanceLimit = 0f;
            avoid.MinimumOcclusionTime = 0f;
            avoid.CameraRadius = DeoccluderCameraRadius;
            avoid.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            avoid.MaximumEffort = DeoccluderMaximumEffort;
            avoid.SmoothingTime = 0f;
            avoid.Damping = DeoccluderDamping;
            avoid.DampingWhenOccluded = DeoccluderDampingWhenOccluded;
            deoccluder.AvoidObstacles = avoid;
            var quality = deoccluder.ShotQualityEvaluation;
            quality.Enabled = false;
            deoccluder.ShotQualityEvaluation = quality;
            EditorUtility.SetDirty(deoccluder);
        }

        // ── NetworkPrefabs ───────────────────────────────────────────────

        /// <summary>NetworkPrefabs.asset 에 Player 등록 (중복 없이). NetworkConfig.PlayerPrefab 은 비워 둔다 — 스폰은 PlayerSpawner.</summary>
        private static void RegisterPlayerNetworkPrefab()
        {
            var list = M0SceneBuilder.EnsureNetworkPrefabsList();
            var prefab = M0SceneBuilder.LoadRequired<GameObject>(PlayerPrefabPath);
            if (list == null || prefab == null)
            {
                return;
            }
            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
                Log.Info(LogCategory.Net, $"NetworkPrefabs 에 등록: {PlayerPrefabPath}");
            }
        }
    }
}
