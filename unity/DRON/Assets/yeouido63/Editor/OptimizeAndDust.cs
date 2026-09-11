// 전체 최적화 + 황사(움직이는 더스트) 적용
//
// 메뉴: Tools/Yeouido 63/최적화 + 황사
//
// 무엇을 왜 바꾸나 — 항공/드론 시점이라는 전제에서 나온다.
//
// [최적화]
//   SSAO Downsample=1, BlurQuality=1
//     풀해상도 AO 를 최고품질 블러로 돌리고 있었다. AO 는 맞닿은 면 사이의
//     좁은 틈을 어둡게 하는 효과인데 항공뷰에선 그런 틈이 화면에서 몇 픽셀도
//     안 된다. 절반 해상도로 내려도 눈으로 구분이 안 되면서 비용은 크게 준다.
//
//   그림자 거리 600 -> 350m
//     600m 는 카메라에서 그만큼 떨어진 곳까지 그림자를 그린다는 뜻이다.
//     같은 4 캐스케이드를 더 긴 거리에 나눠 쓰므로 거리를 줄이면 오히려
//     가까운 곳의 그림자 해상도가 올라간다. 화질과 성능을 같이 얻는다.
//
//   구름 스텝 32 -> 24 / 라이트 스텝 2 유지
//     구름층이 120m 로 얇다(altitudeRange). 레이가 구름을 통과하는 거리가
//     짧아서 스텝을 줄여도 밴딩이 잘 안 보인다.
//
//   MSAA 4 유지
//     잔디가 알파컷아웃이라 MSAA 를 내리면 잎 가장자리가 바로 거칠어진다.
//     여기는 건드리지 않는다.
//
// [황사]
//   이미 들어와 있던 VolumetricFog 렌더러 피처를 켠다(그동안 m_Active: 0).
//   셰이더에 바람 스크롤과 고도 감쇠를 넣어 놨다(VolumetricFog.shader).
//
//   고도 기준은 옥상이다. 옥상 잔디가 월드 Y 279m 근처라 _HeightBase 를
//   지상(0)에 두고 falloff 로 옥상 높이에서 옅어지게 한다 — 그래야 위에서
//   내려다볼 때 도시가 황사에 잠겨 보이고 옥상은 그 위로 나온다.
//
//   씬에 이미 Linear 안개(100~4200m)가 있다. 둘이 겹치면 과하게 뿌예지므로
//   기존 안개 밀도를 낮춘다.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

public static class OptimizeAndDust
{
    const string Flag       = "Temp/yeouido63_opt.flag";
    const string RPAssetPath = "Assets/Settings/PC_RPAsset.asset";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string FogMatPath  = "Assets/yeouido63/Runtime/Fog/M_VolumetricFog.mat";
    const string PostPath    = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[최적화] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/최적화 + 황사")]
    public static void Run()
    {
        Optimize();
        SetupDust();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[최적화] 완료.");
    }

    // ---------------------------------------------------------------- 최적화

    static void Optimize()
    {
        // --- URP 파이프라인 에셋 (SerializedObject 로 만져야 한다.
        //     UniversalRenderPipelineAsset 의 대부분이 private 필드라 C# API 가 없다)
        var rp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(RPAssetPath);
        if (rp == null) { Debug.LogError("[최적화] RP 에셋 없음: " + RPAssetPath); }
        else
        {
            var so = new SerializedObject(rp);
            SetNumber(so, "m_ShadowDistance", 350f, "그림자 거리");
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(rp);
        }

        // --- SSAO (렌더러 피처)
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(RendererPath))
        {
            if (o == null) continue;
            var so = new SerializedObject(o);
            var settings = so.FindProperty("m_Settings");
            if (settings == null) continue;

            var ds = settings.FindPropertyRelative("Downsample");
            var bq = settings.FindPropertyRelative("BlurQuality");
            if (ds == null && bq == null) continue;

            // Downsample 은 URP 버전에 따라 bool 이기도 int 이기도 하다.
            // 타입을 보고 맞춰 넣는다 — 틀린 쪽에 쓰면 조용히 무시된다.
            if (ds != null)
            {
                if (ds.propertyType == SerializedPropertyType.Boolean) ds.boolValue = true;
                else if (ds.propertyType == SerializedPropertyType.Integer) ds.intValue = 1;
                Debug.Log("[최적화] SSAO Downsample -> 켬(절반 해상도)");
            }
            if (bq != null && bq.propertyType == SerializedPropertyType.Integer)
            {
                Debug.Log($"[최적화] SSAO BlurQuality: {bq.intValue} -> 1(Medium)");
                bq.intValue = 1;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(o);
        }

        // --- 구름 레이마칭 스텝
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post != null)
        {
            foreach (var comp in post.components)
            {
                if (comp == null || comp.GetType().Name != "VolumetricClouds") continue;
                var so = new SerializedObject(comp);
                SetParam(so, "numPrimarySteps", 24, "구름 1차 스텝");
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(comp);
            }
            EditorUtility.SetDirty(post);
        }
    }

    // VolumeParameter 는 { m_OverrideState, m_Value } 구조다.
    // 값만 바꾸면 오버라이드가 꺼져 있어 적용되지 않으므로 둘 다 세운다.
    static void SetParam(SerializedObject so, string name, int value, string label)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning($"[최적화] {label}: 프로퍼티 {name} 없음"); return; }
        var ov = p.FindPropertyRelative("m_OverrideState");
        var v  = p.FindPropertyRelative("m_Value");
        if (ov != null) ov.boolValue = true;
        if (v != null)
        {
            Debug.Log($"[최적화] {label}: {v.intValue} -> {value}");
            v.intValue = value;
        }
    }

    // 직렬화 타입이 int 인지 float 인지 버전마다 달라서 보고 맞춘다.
    // 틀린 쪽에 쓰면 예외 없이 조용히 무시되므로 못 찾으면 경고를 남긴다.
    static void SetNumber(SerializedObject so, string name, float value, string label)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning($"[최적화] {label}: {name} 프로퍼티 없음"); return; }

        if (p.propertyType == SerializedPropertyType.Float)
        {
            Debug.Log($"[최적화] {label}: {p.floatValue} -> {value}");
            p.floatValue = value;
        }
        else if (p.propertyType == SerializedPropertyType.Integer)
        {
            Debug.Log($"[최적화] {label}: {p.intValue} -> {(int)value}");
            p.intValue = (int)value;
        }
        else Debug.LogWarning($"[최적화] {label}: {name} 타입이 예상 밖({p.propertyType})");
    }

    // ---------------------------------------------------------------- 황사

    static void SetupDust()
    {
        // --- 황사 머티리얼
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FogMatPath);
        if (mat == null) { Debug.LogError("[황사] 포그 머티리얼 없음: " + FogMatPath); return; }

        // 누런 모래빛. 순수 노랑이면 만화처럼 보여서 채도를 낮춘 베이지로 간다.
        mat.SetColor("_Color", new Color(0.76f, 0.66f, 0.47f, 1f));

        // 태양 쪽이 뿌옇게 빛나는 게 황사의 핵심 인상이다.
        if (mat.HasProperty("_LightContribution"))
            mat.SetColor("_LightContribution", new Color(1.15f, 1.00f, 0.72f, 1f));
        if (mat.HasProperty("_LightScattering")) mat.SetFloat("_LightScattering", 0.55f);

        // 거리/스텝 — 63빌딩 주변 도시가 들어오는 범위.
        // StepSize 를 키우면 싸지지만 밴딩이 생긴다. 18m 가 타협점.
        if (mat.HasProperty("_MaxDistance"))       mat.SetFloat("_MaxDistance", 2600f);
        if (mat.HasProperty("_StepSize"))          mat.SetFloat("_StepSize", 18f);

        // 1.35 / 0.32 로 처음 넣었더니 너무 짙었다. 40% 덜어낸다.
        //   밀도 1.35 -> 0.81 (0.6 배)
        //   임계값 0.32 -> 0.46 — 옅은 부분을 아예 잘라내야 "덜어낸" 느낌이 난다.
        //     밀도만 낮추면 전체가 균일하게 흐려질 뿐 뿌연 인상은 그대로다.
        if (mat.HasProperty("_DensityMultiplier")) mat.SetFloat("_DensityMultiplier", 0.81f);
        if (mat.HasProperty("_DensityThreshold"))  mat.SetFloat("_DensityThreshold", 0.46f);
        if (mat.HasProperty("_NoiseTiling"))       mat.SetFloat("_NoiseTiling", 0.55f);
        if (mat.HasProperty("_NoiseOffset"))       mat.SetFloat("_NoiseOffset", 1f);

        // 바람 — 옥상 잔디가 흔들리는 방향과 대충 맞춰 둔다.
        if (mat.HasProperty("_WindDir"))   mat.SetVector("_WindDir", new Vector4(1f, 0f, 0.35f, 0f));
        if (mat.HasProperty("_WindSpeed")) mat.SetFloat("_WindSpeed", 7f);

        // 고도 감쇠 — 지상에서 짙고 옥상(Y 279m) 위로는 옅어진다.
        // 0.0042 로는 옥상에서 0.31 배라 여전히 뿌옇게 보였다. 0.0062 로 올리면
        // exp(-279 * 0.0062) = 0.18 — 옥상 높이에서 5 분의 1 이 된다.
        // 카메라가 옥상 근처에 있으므로 이 값이 체감에 가장 크게 작용한다.
        if (mat.HasProperty("_HeightFalloff")) mat.SetFloat("_HeightFalloff", 0.0062f);
        if (mat.HasProperty("_HeightBase"))    mat.SetFloat("_HeightBase", 0f);

        // 3D 노이즈가 안 물려 있으면 안개가 균일한 판이 된다
        if (mat.HasProperty("_FogNoise") && mat.GetTexture("_FogNoise") == null)
        {
            var n = AssetDatabase.LoadAssetAtPath<Texture>("Assets/yeouido63/Runtime/Fog/FogNoise3D.asset");
            if (n != null) { mat.SetTexture("_FogNoise", n); Debug.Log("[황사] 3D 노이즈 연결"); }
            else Debug.LogWarning("[황사] FogNoise3D.asset 을 못 찾았다 — 안개가 균일해진다.");
        }
        EditorUtility.SetDirty(mat);

        // --- 렌더러 피처 켜기
        bool turnedOn = false;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(RendererPath))
        {
            if (o == null || o.name != "VolumetricFog") continue;
            var so = new SerializedObject(o);
            var active = so.FindProperty("m_Active");
            if (active != null) { active.boolValue = true; turnedOn = true; }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(o);
        }
        Debug.Log(turnedOn ? "[황사] VolumetricFog 렌더러 피처 켬"
                           : "[황사] VolumetricFog 피처를 못 찾았다 — SetupVolumetrics 를 먼저 돌려라.");

        // --- 기존 Linear 안개와 겹치지 않게 낮춘다
        if (RenderSettings.fog)
        {
            float beforeEnd   = RenderSettings.fogEndDistance;
            float beforeStart = RenderSettings.fogStartDistance;

            // 볼류메트릭 황사가 거리감을 담당하므로 기존 Linear 안개는 물러난다.
            // Mathf.Max 를 쓰면 재실행할 때마다 값이 커지기만 해서 되돌릴 수
            // 없다. 고정값을 그대로 넣는다.
            RenderSettings.fogEndDistance   = 7000f;
            RenderSettings.fogStartDistance = 900f;   // 가까운 건물이 뿌예지지 않게
            RenderSettings.fogColor = new Color(0.78f, 0.71f, 0.56f, 1f);   // 황사와 같은 계열

            Debug.Log($"[황사] Linear 안개 start {beforeStart} -> 900 / end {beforeEnd} -> 7000, 색 맞춤");
        }
    }
}
