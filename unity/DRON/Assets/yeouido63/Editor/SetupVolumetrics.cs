// 볼류메트릭 구름 + 포그 셋업
//
// 메뉴: Tools/Yeouido 63/볼류메트릭 셋업
//
// 무엇을 하나
//   1) PC_Renderer 에 VolumetricCloudsURP 렌더러 피처를 추가한다
//   2) 씬의 Global Volume 프로파일에 Volumetric Clouds 오버라이드를 넣고 켠다
//   3) 포그용 3D 노이즈 텍스처를 굽는다 (없으면)
//   4) 포그 머티리얼을 만들어 Full Screen Pass 피처로 붙인다
//
// 구름 — 출처와 라이선스
//   jiaozi158/UnityVolumetricCloudsURP (MIT). HDRP 의 볼류메트릭 구름을 URP 로
//   포팅한 것. 레포는 URP 14 기준이지만 RecordRenderGraph 경로가 이미 있어
//   URP 17(Unity 6)에서도 돈다.
//
// 이 씬에 맞춘 값 — 왜
//   카메라가 (-2856, 282, -2371) 에서 63빌딩을 내려다본다. 빌딩 높이가 250m 라
//   구름 바닥을 기본값 1200m 그대로 두면 빌딩 한참 위에 뜬다. 그게 맞다 —
//   실제 적운 운저가 그 정도다. 다만 항공 시점이라 구름이 화면을 덮지 않게
//   densityMultiplier 를 낮춰 잡았다.
//
// 포그는 기본 끔
//   씬에 이미 Linear 포그(100~4200m)가 켜져 있다. 볼류메트릭 포그까지 같이
//   켜면 이중으로 끼어 과해진다. 피처는 붙여 두되 비활성으로 둔다 —
//   쓰려면 씬 포그를 줄이고 인스펙터에서 켜라.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupVolumetrics
{
    const string Flag       = "Temp/yeouido63_volumetric.flag";
    const string FogDir     = "Assets/yeouido63/Runtime/Fog";
    const string NoisePath  = FogDir + "/FogNoise3D.asset";
    const string FogMatPath = FogDir + "/M_VolumetricFog.mat";
    const string FogShader  = "Yeouido63/VolumetricFog";
    const int    NoiseSize  = 32;

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[볼류메트릭] 자동 실행 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/볼류메트릭 셋업")]
    public static void Run()
    {
        var log = new List<string>();

        var rendererData = FindPcRenderer();
        if (rendererData == null) { Debug.LogError("[볼류메트릭] PC_Renderer 를 못 찾았다."); return; }

        AddCloudsFeature(rendererData, log);
        MakeNoiseTexture(log);
        AddFogFeature(rendererData, log);
        AddCloudsVolumeOverride(log);

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[볼류메트릭] 셋업 완료\n  " + string.Join("\n  ", log));
    }

    static UniversalRendererData FindPcRenderer()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.Contains("PC_Renderer"))
                return AssetDatabase.LoadAssetAtPath<UniversalRendererData>(p);
        }
        return null;
    }

    // 이름으로 타입을 찾는다. 패키지가 없을 때 컴파일이 깨지지 않게
    // 리플렉션으로 접근한다 — 이 스크립트는 VolumetricClouds asmdef 를
    // 참조하지 않는다(참조하면 에셋을 지웠을 때 프로젝트 전체가 깨진다).
    static System.Type FindType(string name)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);
            if (t != null) return t;
        }
        return null;
    }

    static void AddCloudsFeature(UniversalRendererData data, List<string> log)
    {
        var type = FindType("VolumetricCloudsURP");
        if (type == null) { log.Add("구름: VolumetricCloudsURP 타입 없음 — 에셋이 임포트됐는지 확인해라"); return; }

        if (data.rendererFeatures.Any(f => f != null && f.GetType() == type))
        { log.Add("구름: 렌더러 피처 이미 있음"); return; }

        var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
        feature.name = "VolumetricCloudsURP";
        data.rendererFeatures.Add(feature);
        AssetDatabase.AddObjectToAsset(feature, data);
        // 내부 리스트를 다시 만들게 한다. 이걸 안 하면 에디터를 재시작해야 반영된다.
        data.SetDirty();
        log.Add("구름: 렌더러 피처 추가함");
    }

    static void AddFogFeature(UniversalRendererData data, List<string> log)
    {
        var sh = Shader.Find(FogShader);
        if (sh == null) { log.Add("포그: 셰이더를 못 찾음 — " + FogShader); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(FogMatPath);
        if (mat == null)
        {
            mat = new Material(sh) { name = "M_VolumetricFog" };
            Directory.CreateDirectory(FogDir);
            AssetDatabase.CreateAsset(mat, FogMatPath);
        }
        mat.shader = sh;
        var noise = AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);
        if (noise != null) mat.SetTexture("_FogNoise", noise);
        // 항공 시점 기준값. 씬 포그(4200m)와 겹치지 않게 짧게 잡았다.
        mat.SetFloat("_MaxDistance", 2500f);
        mat.SetFloat("_StepSize", 12f);
        mat.SetFloat("_DensityMultiplier", 0.6f);
        mat.SetFloat("_DensityThreshold", 0.35f);
        mat.SetFloat("_NoiseTiling", 0.35f);
        mat.SetFloat("_LightScattering", 0.35f);
        mat.SetColor("_Color", new Color(0.62f, 0.68f, 0.76f, 1f));
        EditorUtility.SetDirty(mat);

        var type = FindType("UnityEngine.Rendering.Universal.FullScreenPassRendererFeature");
        if (type == null) { log.Add("포그: FullScreenPassRendererFeature 타입 없음"); return; }

        var existing = data.rendererFeatures.FirstOrDefault(f => f != null && f.name == "VolumetricFog");
        if (existing == null)
        {
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "VolumetricFog";
            // 씬 포그와 이중으로 끼는 걸 막으려고 꺼 둔 채로 붙인다
            feature.SetActive(false);
            var so = new SerializedObject(feature);
            so.FindProperty("passMaterial").objectReferenceValue = mat;
            var inj = so.FindProperty("injectionPoint");
            if (inj != null) inj.enumValueIndex = 1;      // BeforeRenderingPostProcessing
            var req = so.FindProperty("requirements");
            if (req != null) req.intValue = (int)ScriptableRenderPassInput.Depth;
            so.ApplyModifiedPropertiesWithoutUndo();

            data.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, data);
            data.SetDirty();
            log.Add("포그: Full Screen Pass 피처 추가함 (기본 비활성 — 씬 포그와 겹침)");
        }
        else log.Add("포그: 피처 이미 있음");
    }

    // 포그용 3D 노이즈. 워리 노이즈를 4채널에 서로 다른 주파수로 굽는다.
    // 셰이더가 dot(noise, noise) 로 밀도를 만들기 때문에 채널이 다양해야
    // 뭉치지 않는다.
    static void MakeNoiseTexture(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath) != null)
        { log.Add("포그: 노이즈 3D 이미 있음"); return; }

        int N = NoiseSize;
        var tex = new Texture3D(N, N, N, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "FogNoise3D" };

        var cols = new Color32[N * N * N];
        int[] cells = { 4, 6, 8, 11 };           // 채널별 셀 수
        var rnd = new System.Random(63);
        var pts = new List<Vector3[]>();
        foreach (int c in cells)
        {
            var arr = new Vector3[c * c * c];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
            pts.Add(arr);
        }

        for (int z = 0; z < N; z++)
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            var p = new Vector3(x / (float)N, y / (float)N, z / (float)N);
            var v = new float[4];
            for (int ch = 0; ch < 4; ch++)
            {
                int c = cells[ch];
                float best = 1e9f;
                var cell = new Vector3Int(Mathf.FloorToInt(p.x * c), Mathf.FloorToInt(p.y * c), Mathf.FloorToInt(p.z * c));
                // 이웃 셀까지 훑어야 경계에서 이음매가 안 생긴다
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ((cell.x + dx) % c + c) % c;
                    int cy = ((cell.y + dy) % c + c) % c;
                    int cz = ((cell.z + dz) % c + c) % c;
                    Vector3 fp = pts[ch][(cz * c + cy) * c + cx];
                    Vector3 wp = new Vector3((cell.x + dx + fp.x) / c, (cell.y + dy + fp.y) / c, (cell.z + dz + fp.z) / c);
                    Vector3 d = p - wp;
                    // 타일링되게 랩어라운드 거리
                    d.x -= Mathf.Round(d.x); d.y -= Mathf.Round(d.y); d.z -= Mathf.Round(d.z);
                    best = Mathf.Min(best, d.sqrMagnitude);
                }
                // 워리는 "가까울수록 0" 이라 뒤집어야 구름처럼 뭉친다
                v[ch] = Mathf.Clamp01(1f - Mathf.Sqrt(best) * cells[ch] * 0.9f);
            }
            cols[(z * N + y) * N + x] = new Color(v[0], v[1], v[2], v[3]);
        }

        tex.SetPixels32(cols);
        tex.Apply(true);
        Directory.CreateDirectory(FogDir);
        AssetDatabase.CreateAsset(tex, NoisePath);
        log.Add($"포그: 노이즈 3D 구움 ({N}^3, 워리 4채널)");
    }

    static void AddCloudsVolumeOverride(List<string> log)
    {
        var type = FindType("VolumetricClouds");
        if (type == null) { log.Add("구름: VolumetricClouds 볼륨 타입 없음"); return; }

        var vol = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)
                        .FirstOrDefault(v => v.isGlobal && v.sharedProfile != null);
        if (vol == null) { log.Add("구름: 씬에 Global Volume 이 없다"); return; }

        var profile = vol.sharedProfile;
        var comp = profile.components.FirstOrDefault(c => c.GetType() == type);
        if (comp == null)
        {
            comp = (VolumeComponent)ScriptableObject.CreateInstance(type);
            comp.name = type.Name;
            profile.components.Add(comp);
            AssetDatabase.AddObjectToAsset(comp, profile);
            log.Add("구름: 볼륨 오버라이드 추가함 -> " + AssetDatabase.GetAssetPath(profile));
        }
        else log.Add("구름: 볼륨 오버라이드 이미 있음");

        // state 를 켜야 실제로 그려진다 (Setup.md 3단계)
        var so = new SerializedObject(comp);
        SetBool(so, "state", true);
        // 항공 시점이라 기본 밀도면 화면을 덮는다. 옅게.
        SetFloat(so, "densityMultiplier", 0.22f);
        SetBool(so, "localClouds", false);          // 지평선까지 도는 원거리 구름
        SetFloat(so, "globalSpeed", 2.5f);          // 아주 느리게 흐르도록
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(comp);
        EditorUtility.SetDirty(profile);
        log.Add("구름: state=Enabled, density=0.22, speed=2.5");
    }

    static void SetBool(SerializedObject so, string name, bool v)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        var val = p.FindPropertyRelative("m_Value");
        var ov  = p.FindPropertyRelative("m_OverrideState");
        if (val != null) val.boolValue = v;
        if (ov != null) ov.boolValue = true;
    }

    static void SetFloat(SerializedObject so, string name, float v)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        var val = p.FindPropertyRelative("m_Value");
        var ov  = p.FindPropertyRelative("m_OverrideState");
        if (val != null) val.floatValue = v;
        if (ov != null) ov.boolValue = true;
    }
}

