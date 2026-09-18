using System.Collections.Generic;
using System.IO;
using Peak.Core;
using Peak.Visual;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Peak.Editor
{
    /// <summary>
    /// Peak > Setup > Rebuild Climb Course — Sandbox_Climb 에 M1 조작감 시험 지형(Docs/202_gameplay.md 10장)을
    /// 메시 콜라이더로 (재)생성한다 (Docs/Prompts/M1-1_climb_course.md).
    /// 실행 순서: Rebuild M0 Scenes (Sandbox_Climb 이 있어야 한다) → 이 메뉴. 두 메뉴는 서로의 오브젝트를 지우지 않는다
    /// (이 메뉴는 [ClimbCourse] 만, Rebuild M0 Scenes 는 Ground·Temp_Ramp* 등 자기 루트만 다룬다).
    /// 멱등: [ClimbCourse] 아래 요소를 이름으로 찾아 값만 다시 맞추고, 메시 에셋은 같은 경로에 덮어써 GUID 를 유지한다.
    /// 서브메시(0 = 걷는 면, 1 = 등반면)는 GameTuning.walkSlope 로 나눈다 — walkSlope 를 튜닝하면 이 메뉴를 다시 실행한다.
    /// 손으로 배치하지 않는다 — 코스를 바꾸려면 <see cref="Course"/> 표를 고치고 다시 실행한다.
    /// </summary>
    public static class ClimbCourseBuilder
    {
        private const string MenuPath = "Peak/Setup/Rebuild Climb Course";

        // ── 경로·이름 ──────────────────────────────────────────────────────
        private const string TuningAssetPath = M0SceneBuilder.ProjectRoot + "/Data/GameTuning.asset";
        private const string SandboxScenePath = M0SceneBuilder.ProjectRoot + "/Scenes/" + SceneNames.SandboxClimb + ".unity";
        private const string MeshDir = M0SceneBuilder.ProjectRoot + "/Art/Meshes/ClimbCourse";
        private const string CourseRootName = "[ClimbCourse]";
        private const string LabelName = "Label";
        /// <summary>M0SceneBuilder 가 만드는 지면 Plane. 코스는 이 머티리얼(URP 기본 Lit)을 공유한다.</summary>
        private const string GroundName = "Ground";
        private const string LabelLayerName = "Default";

        // ── 형태 공통 수치 ───────────────────────────────────────────────────
        /// <summary>ㄷ자 담(오목 코너) 윗면 테두리 폭 m (프롬프트 2장: ≥ 2 m).</summary>
        private const float CourtyardRim = 2f;

        // ── 이름표 (프롬프트 5장) ─────────────────────────────────────────────
        /// <summary>TextMeshPro(3D) 글자 크기. 10 ≈ 줄 높이 1 m — 20 m 밖에서 읽힌다.</summary>
        private const float LabelFontSize = 10f;
        /// <summary>윗면 앞 모서리에서 이름표 중심까지 높이 m.</summary>
        private const float LabelLift = 1.2f;
        private static readonly Vector2 LabelBoxSize = new Vector2(10f, 1.5f);

        // ── 향 (요소 로컬 -Z = 등반면이 보는 쪽. 모든 등반면이 SpawnPoint 쪽을 본다) ──
        /// <summary>북쪽 줄: 등반면이 -Z 를 본다 (로컬 +Z = 월드 +Z 로 깊어짐).</summary>
        private const float FaceMinusZ = 0f;
        /// <summary>서쪽 줄: 등반면이 +X 를 본다 (월드 -X 로 깊어짐, 로컬 +X = 월드 +Z).</summary>
        private const float FacePlusX = -90f;
        /// <summary>동쪽 줄: 등반면이 -X 를 본다 (월드 +X 로 깊어짐, 로컬 +X = 월드 -Z).</summary>
        private const float FaceMinusX = 90f;
        /// <summary>남쪽 줄: 등반면이 +Z 를 본다 (월드 -Z 로 깊어짐, 로컬 +X = 월드 -X).</summary>
        private const float FacePlusZ = 180f;

        private enum Shape
        {
            /// <summary>앞면 경사(발끝 z 0 → 윗변 z run) + 평평한 윗면(깊이 Depth) + 수직 뒷면·옆면. 쐐기·벽 레인·낙하대.</summary>
            Ledge,
            /// <summary>사각뿔대: 네 옆면이 같은 경사, 윗면 Width × Depth.</summary>
            Frustum,
            /// <summary>ㄷ자 담: 안뜰 바닥 Width × Depth(지면), 안쪽 세 면이 같은 경사로 안뜰을 향함, 로컬 -Z 쪽이 열림, 바깥 면 수직.</summary>
            Courtyard,
        }

        /// <summary>
        /// 코스 요소 한 줄. 좌표는 요소 로컬: 원점 = 앞쪽 발끝 선의 중앙(지면), 등반면이 로컬 -Z 를 보고 +Z 로 깊어진다.
        /// Toe = 그 원점의 월드 (x, z). Yaw = Face* 상수.
        /// </summary>
        private readonly struct Element
        {
            public readonly string Name;
            public readonly Shape Shape;
            public readonly float Slope;
            public readonly float Height;
            public readonly float Width;
            public readonly float Depth;
            public readonly Vector2 Toe;
            public readonly float Yaw;
            public readonly string Label;

            public Element(string name, Shape shape, float slope, float height, float width, float depth, float toeX, float toeZ, float yaw, string label)
            {
                Name = name;
                Shape = shape;
                Slope = slope;
                Height = height;
                Width = width;
                Depth = depth;
                Toe = new Vector2(toeX, toeZ);
                Yaw = yaw;
                Label = label;
            }
        }

        /// <summary>
        /// 코스 사양 표 (프롬프트 2장). 경사 = 기운 면의 경사각 °, 높이 m.
        /// 폭·깊이: Ledge = 벽 폭 × 윗면 깊이, Frustum = 윗면 폭 × 깊이, Courtyard = 안뜰 바닥 폭 × 깊이.
        /// 배치: 서쪽 x −14 (경사 경계·낙하대), 북쪽 z 18 (경사·높이 레인), 동쪽 x 14 (볼록), 남쪽 z −16 (오목).
        /// SpawnPoint (0, 1, 0) 반경 6 m 와 Temp_Ramp30/60 (x ±3~7, z 4~10) 은 비우고, 요소 간 간격 ≥ 3 m
        /// (폭 4 m 요소는 중심 7.5 m 간격 = 틈 3.5 m — 정확히 3 m 는 부동소수 오차로 경계에 걸린다).
        /// 이름표가 스폰에서 겹치거나 가리지 않도록: 낙하대는 Boundary_40 과 벌려 남쪽에, 오목 쌍은 낙하대를 가리지 않게 동쪽에, 볼록 쌍은 Concave_60 이름표를 가리지 않게 북쪽에 뒀다.
        /// </summary>
        private static readonly Element[] Course =
        {
            //           이름               형태              경사   높이  폭   깊이  발끝 x  발끝 z  향          이름표
            new Element("Boundary_40",    Shape.Ledge,      40f,  2f,  4f,  3f,   -14f, -10.5f, FacePlusX,  "Boundary 40°"),
            new Element("Boundary_44",    Shape.Ledge,      44f,  2f,  4f,  3f,   -14f,    -3f, FacePlusX,  "Boundary 44°"),
            new Element("Boundary_46",    Shape.Ledge,      46f,  2f,  4f,  3f,   -14f,   4.5f, FacePlusX,  "Boundary 46°"),
            new Element("Boundary_50",    Shape.Ledge,      50f,  2f,  4f,  3f,   -14f,    12f, FacePlusX,  "Boundary 50°"),
            new Element("Slope_46_H10",   Shape.Ledge,      46f, 10f,  4f,  4f, -23.5f,    18f, FaceMinusZ, "46° 10m"),
            new Element("Slope_60_H10",   Shape.Ledge,      60f, 10f,  4f,  4f,   -16f,    18f, FaceMinusZ, "60° 10m"),
            new Element("Slope_75_H10",   Shape.Ledge,      75f, 10f,  4f,  4f,  -8.5f,    18f, FaceMinusZ, "75° 10m"),
            new Element("Height_85_H5",   Shape.Ledge,      85f,  5f,  4f,  4f,     4f,    18f, FaceMinusZ, "85° 5m"),
            new Element("Height_85_H10",  Shape.Ledge,      85f, 10f,  4f,  4f,  11.5f,    18f, FaceMinusZ, "85° 10m"),
            new Element("Height_85_H15",  Shape.Ledge,      85f, 15f,  4f,  4f,    19f,    18f, FaceMinusZ, "85° 15m"),
            new Element("Height_85_H20",  Shape.Ledge,      85f, 20f,  4f,  4f,  26.5f,    18f, FaceMinusZ, "85° 20m"),
            new Element("Convex_60_H8",   Shape.Frustum,    60f,  8f,  4f,  4f,    14f,     8f, FaceMinusX, "Convex 60° 8m"),
            new Element("Convex_85_H8",   Shape.Frustum,    85f,  8f,  4f,  4f,    14f,  -4.5f, FaceMinusX, "Convex 85° 8m"),
            new Element("Concave_60_H8",  Shape.Courtyard,  60f,  8f,  6f,  6f,    17f,   -16f, FacePlusZ,  "Concave 60° 8m"),
            new Element("Concave_85_H8",  Shape.Courtyard,  85f,  8f,  6f,  6f,    -2f,   -16f, FacePlusZ,  "Concave 85° 8m"),
            new Element("Drop_H3",        Shape.Ledge,      30f,  3f,  4f,  4f,   -14f,   -21f, FacePlusX,  "Drop 3m"),
            new Element("Drop_H4",        Shape.Ledge,      30f,  4f,  4f,  4f,   -14f, -31.5f, FacePlusX,  "Drop 4m"),
        };

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log.Warn(LogCategory.Core, "플레이 모드에서는 코스를 재생성할 수 없다");
                return;
            }
            if (!File.Exists(SandboxScenePath))
            {
                Log.Error(LogCategory.Core, $"{SandboxScenePath} 이 없다 — Peak > Setup > Rebuild M0 Scenes 먼저 실행");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            M0SceneBuilder.EnsureFolder(MeshDir);
            M0SceneBuilder.BuildScene(SceneNames.SandboxClimb, BuildCourse);
            PruneStaleMeshes();
            AssetDatabase.SaveAssets();
            Log.Info(LogCategory.Core, $"Rebuild Climb Course 완료: {Course.Length}개 요소 / {MeshDir}");
        }

        // ── 씬 ───────────────────────────────────────────────────────────

        /// <summary>에셋은 씬을 연 뒤 로드한다 (OpenScene 이 참조 없는 에셋을 언로드 — M0SceneBuilder 주석 참고).</summary>
        private static void BuildCourse(Scene scene)
        {
            var tuning = M0SceneBuilder.LoadRequired<GameTuning>(TuningAssetPath);
            var theme = M0SceneBuilder.LoadRequired<VisualTheme>(M0SceneBuilder.ThemeAssetPath);
            if (tuning == null || theme == null)
            {
                return;
            }
            var material = GroundMaterial(scene);
            Vector3 spawn = SpawnPosition(scene);
            int terrainLayer = LayerMask.NameToLayer(Layers.Terrain);
            int labelLayer = LayerMask.NameToLayer(LabelLayerName);

            var root = M0SceneBuilder.EnsureRoot(scene, CourseRootName);
            M0SceneBuilder.SetTransform(root, Vector3.zero, Vector3.zero, Vector3.one);
            PruneStaleChildren(root);

            for (int i = 0; i < Course.Length; i++)
            {
                var spec = Course[i];
                var element = M0SceneBuilder.EnsureChild(root, spec.Name);
                element.transform.SetSiblingIndex(i);
                M0SceneBuilder.SetTransform(element, new Vector3(spec.Toe.x, 0f, spec.Toe.y), new Vector3(0f, spec.Yaw, 0f), Vector3.one);
                element.layer = terrainLayer;
                element.isStatic = true;

                var mesh = WriteMesh(spec, tuning.walkSlope, out Vector3 labelAnchor);
                M0SceneBuilder.EnsureComponent<MeshFilter>(element).sharedMesh = mesh;
                var meshRenderer = M0SceneBuilder.EnsureComponent<MeshRenderer>(element);
                meshRenderer.sharedMaterials = new[] { material, material };
                var meshCollider = M0SceneBuilder.EnsureComponent<MeshCollider>(element);
                meshCollider.convex = false;
                meshCollider.sharedMesh = mesh;
                var tint = M0SceneBuilder.EnsureComponent<SlopeTint>(element);
                M0SceneBuilder.SetRef(tint, "theme", theme);
                tint.Apply();

                EnsureLabel(element, spec, labelAnchor, spawn, labelLayer, theme);
            }
        }

        private static Material GroundMaterial(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == GroundName && go.TryGetComponent<MeshRenderer>(out var ground) && ground.sharedMaterial != null)
                {
                    return ground.sharedMaterial;
                }
            }
            Log.Warn(LogCategory.Core, $"{GroundName} 머티리얼을 찾지 못해 렌더 파이프라인 기본 머티리얼을 쓴다");
            return GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
        }

        private static Vector3 SpawnPosition(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == PlayerSpawner.SpawnPointName)
                {
                    return go.transform.position;
                }
            }
            Log.Warn(LogCategory.Core, $"{PlayerSpawner.SpawnPointName} 가 없다 — 이름표는 원점을 향한다");
            return Vector3.zero;
        }

        /// <summary>표에 없는 [ClimbCourse] 자식(이름이 바뀐 옛 요소)을 지운다 → 자식 = 표의 이름과 정확히 같다.</summary>
        private static void PruneStaleChildren(GameObject root)
        {
            var names = new HashSet<string>();
            foreach (var spec in Course)
            {
                names.Add(spec.Name);
            }
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i).gameObject;
                if (!names.Contains(child.name))
                {
                    Log.Info(LogCategory.Core, $"{CourseRootName}: 표에 없는 {child.name} 삭제");
                    Object.DestroyImmediate(child);
                }
            }
        }

        /// <summary>
        /// 이름표: 월드 공간 TextMeshPro (기본 폰트), 윗면 앞 모서리 위 <see cref="LabelLift"/>, SpawnPoint 쪽을 향함 (수평 회전만).
        /// TextMeshPro 는 transform 의 +Z 를 바라보는 사람에게 읽히므로 +Z = SpawnPoint → 이름표 방향.
        /// </summary>
        private static void EnsureLabel(GameObject element, Element spec, Vector3 localAnchor, Vector3 spawn, int layer, VisualTheme theme)
        {
            var label = M0SceneBuilder.EnsureChild(element, LabelName);
            label.layer = layer;

            Vector3 localPosition = localAnchor + Vector3.up * LabelLift;
            Vector3 toLabel = element.transform.TransformPoint(localPosition) - spawn;
            float worldYaw = Mathf.Atan2(toLabel.x, toLabel.z) * Mathf.Rad2Deg;
            M0SceneBuilder.SetTransform(label, localPosition, new Vector3(0f, Mathf.DeltaAngle(spec.Yaw, worldYaw), 0f), Vector3.one);

            var text = M0SceneBuilder.EnsureComponent<TextMeshPro>(label);
            text.font = TMP_Settings.defaultFontAsset;
            text.text = spec.Label;
            text.fontSize = LabelFontSize;
            text.color = theme.uiBackground;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = LabelBoxSize;
            var textRenderer = label.GetComponent<MeshRenderer>();
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        // ── 메시 ─────────────────────────────────────────────────────────

        /// <summary>요소 메시를 만들어 <see cref="MeshDir"/>/이름.asset 에 쓴다. 있으면 같은 에셋을 비우고 다시 채워 GUID 를 유지한다.</summary>
        private static Mesh WriteMesh(Element spec, float walkSlope, out Vector3 labelAnchor)
        {
            var builder = new SolidBuilder(walkSlope);
            switch (spec.Shape)
            {
                case Shape.Ledge:
                    labelAnchor = BuildLedge(builder, spec);
                    break;
                case Shape.Frustum:
                    labelAnchor = BuildFrustum(builder, spec);
                    break;
                default:
                    labelAnchor = BuildCourtyard(builder, spec);
                    break;
            }

            string path = $"{MeshDir}/{spec.Name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew)
            {
                mesh = new Mesh();
            }
            builder.WriteTo(mesh);
            mesh.name = spec.Name;
            if (isNew)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        /// <summary>앞면 경사의 수평 길이 = 높이 / tan(경사).</summary>
        private static float Run(Element spec) => spec.Height / Mathf.Tan(spec.Slope * Mathf.Deg2Rad);

        /// <summary>쐐기·벽 레인·낙하대. 윗면 앞 모서리 = 경사면 윗변 (같은 정점 좌표 → 틈·턱 0). 반환 = 윗면 앞 모서리 중앙.</summary>
        private static Vector3 BuildLedge(SolidBuilder b, Element spec)
        {
            float w = spec.Width * 0.5f;
            float h = spec.Height;
            float run = Run(spec);
            float back = run + spec.Depth;

            b.AddFace(Vector3.back, new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, h, run), new Vector3(-w, h, run));
            b.AddFace(Vector3.up, new Vector3(-w, h, run), new Vector3(w, h, run), new Vector3(w, h, back), new Vector3(-w, h, back));
            b.AddFace(Vector3.forward, new Vector3(-w, 0f, back), new Vector3(w, 0f, back), new Vector3(w, h, back), new Vector3(-w, h, back));
            b.AddFace(Vector3.left, new Vector3(-w, 0f, 0f), new Vector3(-w, h, run), new Vector3(-w, h, back), new Vector3(-w, 0f, back));
            b.AddFace(Vector3.right, new Vector3(w, 0f, 0f), new Vector3(w, h, run), new Vector3(w, h, back), new Vector3(w, 0f, back));
            return new Vector3(0f, h, run);
        }

        /// <summary>사각뿔대. 바닥 = 윗면 + 사방 run. 반환 = 윗면 앞 모서리 중앙.</summary>
        private static Vector3 BuildFrustum(SolidBuilder b, Element spec)
        {
            float h = spec.Height;
            float run = Run(spec);
            float tx = spec.Width * 0.5f;
            float bx = tx + run;
            float baseDepth = spec.Depth + 2f * run;
            float topBack = run + spec.Depth;

            var b0 = new Vector3(-bx, 0f, 0f);
            var b1 = new Vector3(bx, 0f, 0f);
            var b2 = new Vector3(bx, 0f, baseDepth);
            var b3 = new Vector3(-bx, 0f, baseDepth);
            var t0 = new Vector3(-tx, h, run);
            var t1 = new Vector3(tx, h, run);
            var t2 = new Vector3(tx, h, topBack);
            var t3 = new Vector3(-tx, h, topBack);

            b.AddFace(Vector3.back, b0, b1, t1, t0);
            b.AddFace(Vector3.right, b1, b2, t2, t1);
            b.AddFace(Vector3.forward, b2, b3, t3, t2);
            b.AddFace(Vector3.left, b3, b0, t0, t3);
            b.AddFace(Vector3.up, t0, t1, t2, t3);
            return new Vector3(0f, h, run);
        }

        /// <summary>
        /// ㄷ자 담. 안뜰 바닥(지면) x ±a, z 0~c. 안쪽 세 면이 바닥 테두리에서 경사로 올라가 윗면 안쪽 가장자리 x ±ai, z ci 에 닿는다
        /// → 왼쪽·뒤, 오른쪽·뒤 안쪽 면이 만나는 두 오목 코너. 로컬 -Z(z 0)는 열려 있어 걸어 들어간다.
        /// 윗면·바깥 면은 T 접합이 없도록 공유 정점에서 나눈다.
        /// 반환 = 열린 쪽 위 (양팔 앞 끝을 잇는 선의 중앙, 윗면 높이) — 뒤쪽 테두리 위에 두면 스폰에서 볼 때 앞팔에 가린다.
        /// </summary>
        private static Vector3 BuildCourtyard(SolidBuilder b, Element spec)
        {
            float h = spec.Height;
            float run = Run(spec);
            float a = spec.Width * 0.5f;
            float c = spec.Depth;
            float ai = a + run;
            float ci = c + run;
            float o = ai + CourtyardRim;
            float zb = ci + CourtyardRim;

            // 안쪽 세 면 (등반면, 안뜰을 향함)
            b.AddFace(Vector3.right, new Vector3(-a, 0f, 0f), new Vector3(-a, 0f, c), new Vector3(-ai, h, ci), new Vector3(-ai, h, 0f));
            b.AddFace(Vector3.left, new Vector3(a, 0f, 0f), new Vector3(a, 0f, c), new Vector3(ai, h, ci), new Vector3(ai, h, 0f));
            b.AddFace(Vector3.back, new Vector3(-a, 0f, c), new Vector3(a, 0f, c), new Vector3(ai, h, ci), new Vector3(-ai, h, ci));

            // 윗면 테두리: 양팔 + 뒤 3칸
            AddTopRect(b, h, -o, -ai, 0f, ci);
            AddTopRect(b, h, ai, o, 0f, ci);
            AddTopRect(b, h, -o, -ai, ci, zb);
            AddTopRect(b, h, -ai, ai, ci, zb);
            AddTopRect(b, h, ai, o, ci, zb);

            // 바깥 면 (수직)
            b.AddFace(Vector3.left, new Vector3(-o, 0f, 0f), new Vector3(-o, 0f, ci), new Vector3(-o, h, ci), new Vector3(-o, h, 0f));
            b.AddFace(Vector3.left, new Vector3(-o, 0f, ci), new Vector3(-o, 0f, zb), new Vector3(-o, h, zb), new Vector3(-o, h, ci));
            b.AddFace(Vector3.right, new Vector3(o, 0f, 0f), new Vector3(o, 0f, ci), new Vector3(o, h, ci), new Vector3(o, h, 0f));
            b.AddFace(Vector3.right, new Vector3(o, 0f, ci), new Vector3(o, 0f, zb), new Vector3(o, h, zb), new Vector3(o, h, ci));
            b.AddFace(Vector3.forward, new Vector3(-o, 0f, zb), new Vector3(-ai, 0f, zb), new Vector3(-ai, h, zb), new Vector3(-o, h, zb));
            b.AddFace(Vector3.forward, new Vector3(-ai, 0f, zb), new Vector3(ai, 0f, zb), new Vector3(ai, h, zb), new Vector3(-ai, h, zb));
            b.AddFace(Vector3.forward, new Vector3(ai, 0f, zb), new Vector3(o, 0f, zb), new Vector3(o, h, zb), new Vector3(ai, h, zb));

            // 양팔 앞 끝 (수직, 열린 쪽)
            b.AddFace(Vector3.back, new Vector3(-o, 0f, 0f), new Vector3(-a, 0f, 0f), new Vector3(-ai, h, 0f), new Vector3(-o, h, 0f));
            b.AddFace(Vector3.back, new Vector3(a, 0f, 0f), new Vector3(o, 0f, 0f), new Vector3(o, h, 0f), new Vector3(ai, h, 0f));
            return new Vector3(0f, h, 0f);
        }

        private static void AddTopRect(SolidBuilder b, float h, float x0, float x1, float z0, float z1)
        {
            b.AddFace(Vector3.up, new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x0, h, z1));
        }

        /// <summary>표에 없는 이름의 코스 메시 에셋을 지운다 (요소 이름을 바꿨을 때 옛 메시가 남지 않게).</summary>
        private static void PruneStaleMeshes()
        {
            var names = new HashSet<string>();
            foreach (var spec in Course)
            {
                names.Add(spec.Name);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') == MeshDir && !names.Contains(Path.GetFileNameWithoutExtension(path)))
                {
                    Log.Info(LogCategory.Core, $"표에 없는 코스 메시 삭제: {path}");
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        /// <summary>
        /// 볼록 다각형 면을 모아 메시를 만든다. 면마다 정점을 따로 둬 평평한 법선, 감기는 바깥(outward 쪽)을 향하게 맞춘다.
        /// 서브메시: 법선 경사 &lt; walkSlope → <see cref="SlopeTint.WalkSubmesh"/>, 아니면 <see cref="SlopeTint.WallSubmesh"/>.
        /// 바닥면(지면에 닿는 면)은 만들지 않는다.
        /// </summary>
        private sealed class SolidBuilder
        {
            private const int SubmeshCount = 2;

            private readonly float _walkSlope;
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<int>[] _triangles = { new List<int>(), new List<int>() };

            public SolidBuilder(float walkSlope)
            {
                _walkSlope = walkSlope;
            }

            /// <param name="outward">대략의 바깥 방향. 정점 순서가 반대면 뒤집는다.</param>
            /// <param name="polygon">볼록 다각형 (한 평면, 순서는 둘레를 따라).</param>
            public void AddFace(Vector3 outward, params Vector3[] polygon)
            {
                Vector3 area = Vector3.zero;
                for (int i = 1; i < polygon.Length - 1; i++)
                {
                    area += Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
                }
                if (Vector3.Dot(area, outward) < 0f)
                {
                    System.Array.Reverse(polygon);
                    area = -area;
                }
                Vector3 normal = area.normalized;
                int submesh = Vector3.Angle(normal, Vector3.up) < _walkSlope ? SlopeTint.WalkSubmesh : SlopeTint.WallSubmesh;

                int start = _vertices.Count;
                foreach (var vertex in polygon)
                {
                    _vertices.Add(vertex);
                    _normals.Add(normal);
                }
                var triangles = _triangles[submesh];
                for (int i = 1; i < polygon.Length - 1; i++)
                {
                    // Unity 앞면 = Cross(b - a, c - a) 가 보는 쪽
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
            }

            public void WriteTo(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.subMeshCount = SubmeshCount;
                for (int i = 0; i < SubmeshCount; i++)
                {
                    mesh.SetTriangles(_triangles[i], i);
                }
                mesh.RecalculateBounds();
            }
        }
    }
}
