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

        // 회색 먼지.
        //   처음엔 누런 모래빛(0.76, 0.66, 0.47)으로 갔는데 화면이 전체적으로
        //   세피아 필터를 씌운 것처럼 보였다. 도시 미세먼지 쪽에 가깝게
        //   중성 회색으로 뺀다. 완전 무채색이면 죽어 보여서 아주 살짝만
        //   푸른기를 남긴다 — 대기 산란이 짧은 파장을 더 남기기 때문이다.
        mat.SetColor("_Color", new Color(0.63f, 0.64f, 0.66f, 1f));

        // 태양 쪽이 뿌옇게 빛나는 게 황사의 핵심 인상이다.
        //
        //   값이 작아 보이지만 henyey_greenstein 은 전방 산란 각도에서 수십까지
        //   튄다(_LightScattering 이 클수록 더). 1.15 를 넣었다가 화면이 하얗게
        //   날아갔다. 0.35 정도가 태양 주변만 은은하게 밝아지는 지점이다.
        //   회색 먼지에 맞춰 산란광도 중성으로 뺀다. 여기만 누런 채로 두면
        //   태양 쪽만 노랗게 떠서 안개 색과 어긋난다.
        if (mat.HasProperty("_LightContribution"))
            mat.SetColor("_LightContribution", new Color(0.32f, 0.32f, 0.33f, 1f));

        // 위상함수 첨예도. 0.55 는 태양 쪽 피크가 날카로워 쉽게 과포화된다.
        if (mat.HasProperty("_LightScattering")) mat.SetFloat("_LightScattering", 0.35f);

        // 거리/스텝 — 여기가 비용을 결정한다.
        //
        //   레이마칭 비용 = 픽셀 수 x 스텝 수이고, 스텝 수는 대략
        //   _MaxDistance / _StepSize 다. 2600/18 = 144 스텝은 전체 화면에
        //   깔기엔 무겁다.
        //
        //   1200m 로 줄인다. 이 거리면 63빌딩 주변 블록까지는 황사가 덮이고
        //   그 너머는 Linear 안개(더 싸다)가 이어받는다. 두 안개의 색을
        //   맞춰 놨으므로 경계가 눈에 띄지 않는다.
        //   스텝은 22m 로 키워 스텝 수를 144 -> 55 로 떨어뜨린다. 밀도가
        //   낮아서(소광계수 0.0008/m) 이 정도 간격에선 밴딩이 안 보인다.
        if (mat.HasProperty("_MaxDistance"))       mat.SetFloat("_MaxDistance", 1200f);
        if (mat.HasProperty("_StepSize"))          mat.SetFloat("_StepSize", 22f);

        // 농도.
        //
        //   1.35 -> 0.81 -> 0.34 로 계속 낮췄는데도 화면이 베이지 단색으로
        //   덮였다. 농도 문제가 아니라 **단위가 틀린 것**이었다.
        //   density 는 투과율 exp(-density * 거리[m]) 에 들어가므로 미터당
        //   소광계수인데, 0.14 만 돼도 7m 마다 빛이 1/e 로 준다. 2.6km 를
        //   행군하면 투과율이 0 이 될 수밖에 없다.
        //   셰이더에서 1/400 로 환산하도록 고쳤다(get_density 주석 참고).
        //
        //   그 환산 위에서 고른 값이다. _MaxDistance 를 2600 -> 1200 으로
        //   줄이면서 같은 두께를 내려고 0.6 -> 0.9 로 올렸다. 투과율은
        //     300m(옥상)  0.69  — 또렷하게 보인다
        //     800m        0.38
        //     1200m       0.23  — 여기서 Linear 안개가 이어받는다
        //   원하던 "옥상은 보이고 멀리는 뿌연" 그림이 이 구간에서 나온다.
        //   색을 회색으로 뺀 뒤 0.9 로는 원경이 너무 맑아졌다(하늘이 그냥
        //   파랗게 보였다). 누런색일 때는 색 자체가 눈에 띄어 같은 농도라도
        //   짙어 보였던 것. 1.15 로 올려 뿌연 느낌을 되살린다.
        //   투과율 300m 0.63 / 800m 0.29 / 1200m 0.16
        if (mat.HasProperty("_DensityMultiplier")) mat.SetFloat("_DensityMultiplier", 1.15f);
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
            // 카메라가 지상(Y 1.75m)이라 63빌딩을 올려다보는 구도다. 옥상까지의
            // 거리가 300m 를 훌쩍 넘으므로 start 가 900m 라도 안개가 옥상에
            // 걸린다. 1800m 로 밀어 건물 전체가 안개 밖에 있게 한다.
            RenderSettings.fogEndDistance   = 7000f;
            RenderSettings.fogStartDistance = 1800f;
            // 볼류메트릭 쪽과 같은 회색 계열. 둘이 다르면 1200m 경계에서
            // 색이 바뀌는 게 보인다.
            RenderSettings.fogColor = new Color(0.66f, 0.67f, 0.69f, 1f);

            Debug.Log($"[황사] Linear 안개 start {beforeStart} -> 900 / end {beforeEnd} -> 7000, 색 맞춤");
        }
    }
}
