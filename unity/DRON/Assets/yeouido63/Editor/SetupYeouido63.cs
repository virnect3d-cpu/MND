// 여의도 63빌딩 씬 셋업 (Blender -> Unity URP)
//
// 메뉴: Tools/Yeouido 63/씬 셋업
//
// 텍스처를 왜 이렇게 붙이나
//   처음엔 도시 전체를 4096 아틀라스 한 장으로 구웠는데, 3.2 km 를 4096 에 펴면
//   텍셀 밀도가 0.8 m/px 라 아파트 파사드가 뭉개진다. 그래서 벽면은 **원본 타일
//   텍스처를 미터 UV 로 반복**시킨다. UVMap 이 미터 단위(u 0~940)라 Unity 의
//   tiling 을 1/타일크기(m) 로 주면 블렌더와 같은 스케일이 나온다.
//
//   BUILDINGS 는 wtile(파사드 인덱스)별로 서브메쉬 10개로 갈라 뒀다.
//     0~5  아파트 사진 6종      (UVMap, 미터 타일링)
//     6,7  노출콘크리트/타일붙임 (UVMap, 미터 타일링)
//     8    지붕 - 정사영상       (UVRoof, 0~1 평면투영 -> TERRAIN 과 같은 텍스처)
//     9    옥상 캡 - 콘크리트    (UVMap, 미터 타일링)
//
// roughness -> smoothness
//   Blender=roughness, URP=smoothness(1-roughness). 채널 규약도 달라서
//   `_MetallicGlossMap` 의 RGB=metallic / A=smoothness 로 미리 합쳐 뒀다.
//
// Blender 에서 가져온 수치
//   태양 고도 45.7 / 방위 298.8 (2K.hdr 에서 자동 검출), 세기 1.4
//   스카이박스 회전 270 (Blender u=0.5 는 -X, Unity Panoramic 은 +Z)
//   카메라 수동 지정 위치 (-2856, 282, -2371) / 회전 (-0.19, 54.807, 0.768)
//         수직 FOV 25.4, far 30000, 포그 끔
//   축 Blender(x,y,z) -> Unity(x,z,y)

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupYeouido63
{
    // 이 에셋은 두 군데 중 아무 데나 살 수 있다.
    //   1) Packages/com.virnect.yeouido63/   임베디드 UPM (이 프로젝트 기본)
    //   2) Assets/Yeouido63/                 .unitypackage 로 임포트한 쪽
    // 둘 다 되게 하려고 경로를 박지 않고 FBX 를 찾아서 루트를 거꾸로 짚는다.
    // 임베디드 패키지는 읽기 전용이 아니라 CreateAsset / 씬 저장이 그대로 먹는다.
    const string FbxName   = "yeouido_63.fbx";
    const string FbxTail   = "/Runtime/Models/" + FbxName;
    const string PkgDefault= "Packages/com.virnect.yeouido63";

    static string _pkg;
    static string Pkg        => _pkg ?? (_pkg = ResolvePkg());
    static string Root       => Pkg + "/Runtime";
    static string FbxPath    => Root + "/Models/" + FbxName;
    static string BakeDir    => Root + "/Textures";            // 구운 것 (지형/63빌딩/바다)
    static string TileDir    => Root + "/Textures/Tiles";      // 타일 텍스처 (파사드/옥상)
    static string MatDir     => Root + "/Materials";
    static string HdrPath    => Root + "/HDRI/2K.hdr";
    static string SkyMatPath => Root + "/HDRI/Sky_Yeouido.mat";
    static string SceneDir   => Pkg + "/Scenes";
    static string ScenePath  => SceneDir + "/Yeouido63.unity";

    // FBX 이름으로 검색해서 `.../Runtime/Models/yeouido_63.fbx` 로 끝나는 놈의
    // 앞부분을 루트로 잡는다. 못 찾으면 임베디드 기본 경로로 떨어진다.
    static string ResolvePkg()
    {
        foreach (var guid in AssetDatabase.FindAssets("yeouido_63"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(FbxTail)) return p.Substring(0, p.Length - FbxTail.Length);
        }
        return PkgDefault;
    }

    const float SunElevation = 45.7f;
    const float SunAzimuth   = 298.8f;
    const float SkyRotation  = 270f;

    class Def
    {
        public string Tex;          // 텍스처 파일명 (확장자 포함)
        public string Dir;          // 어느 폴더에서
        public Vector2 Tiling;      // UV 가 미터라 1/크기(m). 0,0 이면 타일링 안 함
        public float Metallic, Smoothness;
        public string MetalSmoothTex;   // 있으면 맵으로 덮어씀
        public string NormalTex;
        public Color? BaseColor;        // 지정하면 이 색을 쓴다 (Tex 없을 때 특히)
    }

    // 타일링 값 = 1 / 타일이 덮는 실제 크기(m). apt_meta.json 에서 나온 값.
    static readonly Dictionary<string, Def> Mats = new Dictionary<string, Def>
    {
        { "M_APT0", new Def { Tex="apt_0.png", Dir=TileDir, Tiling=new Vector2(0.08375f,0.11111f), Smoothness=0.30f } },
        { "M_APT1", new Def { Tex="apt_1.png", Dir=TileDir, Tiling=new Vector2(0.07117f,0.04167f), Smoothness=0.30f } },
        { "M_APT2", new Def { Tex="apt_2.png", Dir=TileDir, Tiling=new Vector2(0.10267f,0.06667f), Smoothness=0.30f } },
        { "M_APT3", new Def { Tex="apt_3.png", Dir=TileDir, Tiling=new Vector2(0.04212f,0.05556f), Smoothness=0.30f } },
        { "M_APT4", new Def { Tex="apt_4.png", Dir=TileDir, Tiling=new Vector2(0.03525f,0.05556f), Smoothness=0.30f } },
        { "M_APT5", new Def { Tex="apt_5.png", Dir=TileDir, Tiling=new Vector2(0.02237f,0.06667f), Smoothness=0.30f } },
        { "M_FACADE_CONCRETE", new Def { Tex="facade_concrete.png",    Dir=TileDir, Tiling=new Vector2(0.098f,0.082f), Smoothness=0.18f } },
        { "M_FACADE_TILE",     new Def { Tex="facade_tile_mosaic.png", Dir=TileDir, Tiling=new Vector2(0.111f,0.087f), Smoothness=0.35f } },
        // 지붕은 정사영상 평면투영(UVRoof 0~1). 지형과 같은 텍스처를 쓴다.
        { "M_ROOF_ORTHO", new Def { Tex="TERRAIN_basecolor.png", Dir=BakeDir, Tiling=Vector2.one, Smoothness=0.10f } },
        { "M_ROOFCAP",    new Def { Tex="roof_floor.png", Dir=TileDir, Tiling=new Vector2(0.25f,0.25f), Smoothness=0.12f } },
        { "M_TERRAIN",    new Def { Tex="TERRAIN_basecolor.png", Dir=BakeDir, Tiling=Vector2.one,
                                    MetalSmoothTex="TERRAIN_metallicsmooth.png" } },
        // 바다는 베이크 아틀라스를 쓰면 안 된다. SEA 의 UV 는 미터/240 이라 0~19.5 인데
        // 아틀라스를 물리면 그게 19번 반복돼 체크무늬가 뜬다. 블렌더처럼 단색 바디 +
        // 물결 노말(타일 240 m)로 간다. UV 가 이미 /240 이라 tiling 은 1,1 이면 맞는다.
        { "M_SEA", new Def { Tex=null, Dir=BakeDir, Tiling=Vector2.one,
                             BaseColor=new Color(0.0147f, 0.1812f, 0.3054f, 1f),
                             Metallic=0f, Smoothness=0.94f,
                             NormalTex="SEA_normal.png" } },
        { "M_63SQUARE",   new Def { Tex="BLD_63SQUARE_basecolor.png", Dir=BakeDir, Tiling=Vector2.one,
                                    Metallic=1f, MetalSmoothTex="BLD_63SQUARE_metallicsmooth.png",
                                    NormalTex="BLD_63SQUARE_normal.png" } },
        // ROADLINES 는 원본 메쉬에 UV 가 없다. 도로 중앙선이라 단색으로 충분하다.
        { "M_ROADLINES",  new Def { Tex=null, Dir=null, Smoothness=0.22f } },
    };

    static Vector3 B2U(float x, float y, float z) => new Vector3(x, z, y);

    [MenuItem("Tools/Yeouido 63/씬 셋업")]
    public static void Setup()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
        {
            EditorUtility.DisplayDialog("Yeouido 63", "FBX 를 못 찾았어요:\n" + FbxPath, "확인");
            return;
        }

        ConfigureImporter();
        ConfigureTextures(BakeDir);
        ConfigureTextures(TileDir);
        var mats = BuildMaterials();
        var sky = MakeSkybox();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 256;
        // 포그는 아트 방향상 끔. 다시 켜려면 아래 3줄을 살리고 fog=true 로.
        RenderSettings.fog = false;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.00035f;
        RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.74f);

        var lightGo = new GameObject("Sun (HDRI 정렬)");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(SunElevation, SunAzimuth + 180f, 0f);
        light.color = new Color(1.0f, 0.96f, 0.90f);
        light.intensity = 1.4f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.85f;
        light.shadowBias = 0.08f;
        light.shadowNormalBias = 0.6f;
        light.shadowNearPlane = 1f;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        go.name = "Yeouido63_City";
        go.transform.position = Vector3.zero;

        // FBX 의 재질 슬롯 이름(M_*)을 그대로 우리 재질에 매핑한다.
        int applied = 0, missing = 0;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var arr = mr.sharedMaterials;
            for (int i = 0; i < arr.Length; i++)
            {
                var key = arr[i] != null ? StripSuffix(arr[i].name) : GuessByObject(mr.gameObject.name, i);
                if (key != null && mats.TryGetValue(key, out var m)) { arr[i] = m; applied++; }
                else { Debug.LogWarning($"[Yeouido63] 재질 매핑 실패: {mr.gameObject.name}[{i}] = {key}"); missing++; }
            }
            mr.sharedMaterials = arr;
        }

        var camGo = new GameObject("CAM_63_Air");
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.tag = "MainCamera";
        // 유니티 뷰포트에서 직접 잡은 값. Blender 카메라를 변환해 쓰던 걸 대체.
        var pos = new Vector3(-2856f, 282f, -2371f);
        camGo.transform.position = pos;
        camGo.transform.rotation = Quaternion.Euler(-0.19f, 54.807f, 0.768f);
        cam.fieldOfView = 25.4f;
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 30000f;
        // 게임뷰에 하늘이 나오려면 이게 Skybox 여야 한다. URP 도 이 값을 그대로 본다.
        cam.clearFlags = CameraClearFlags.Skybox;
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

        Directory.CreateDirectory(SceneDir);
        var volGo = new GameObject("Global Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, SceneDir + "/Yeouido63_Post.asset");
        var tm = profile.Add<Tonemapping>(true);
        tm.mode.overrideState = true; tm.mode.value = TonemappingMode.Neutral;
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.overrideState = true; ca.postExposure.value = -0.45f;
        vol.sharedProfile = profile;

        // 환경광은 스카이박스를 바꿔도 자동 갱신이 안 될 때가 있다. 명시적으로 굽는다.
        DynamicGI.UpdateEnvironment();

        // 씬뷰에서도 하늘이 보이게. 이건 씬 데이터가 아니라 **씬뷰 개인 설정**이라
        // 씬을 저장해도 안 따라간다. 그래서 여기서 켜 준다.
        foreach (SceneView sv in SceneView.sceneViews)
        {
            var st = sv.sceneViewState;
            st.showSkybox = true;
            st.showFog = true;
            st.showFlares = true;
            st.showImageEffects = true;
            st.alwaysRefresh = false;
            sv.sceneViewState = st;
            sv.LookAt(new Vector3(-1200f, 120f, -900f), Quaternion.Euler(10f, 54.8f, 0f), 2200f);
            sv.Repaint();
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Yeouido63] 완료 -> {ScenePath} / 재질 적용 {applied}, 실패 {missing}");
        EditorUtility.DisplayDialog("Yeouido 63",
            $"씬 셋업 완료\n{ScenePath}\n재질 적용 {applied}개 (실패 {missing})", "확인");
    }

    // Unity 가 임포트하며 붙이는 " (Instance)" 같은 꼬리표를 뗀다
    static string StripSuffix(string n)
    {
        int i = n.IndexOf(" (");
        return i > 0 ? n.Substring(0, i) : n;
    }

    static string GuessByObject(string obj, int slot)
    {
        switch (obj)
        {
            case "TERRAIN": return "M_TERRAIN";
            case "SEA": return "M_SEA";
            case "ROADLINES": return "M_ROADLINES";
            case "BLD_63SQUARE": return "M_63SQUARE";
            default: return null;
        }
    }

    static void ConfigureImporter()
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) return;
        imp.globalScale = 1f;
        imp.useFileScale = true;
        imp.importCameras = false;
        imp.importLights = false;
        imp.importAnimation = false;
        imp.importBlendShapes = false;
        // 재질 슬롯 이름은 필요하지만 실제 재질은 우리가 만든다
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        imp.meshCompression = ModelImporterMeshCompression.Off;
        imp.isReadable = false;
        imp.indexFormat = ModelImporterIndexFormat.UInt32;   // BUILDINGS 17만 정점
        imp.SaveAndReimport();
    }

    static void ConfigureTextures(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            bool linear = p.Contains("_metallicsmooth") || p.Contains("_normal") || p.Contains("_roughness");
            bool normal = p.Contains("_normal");
            bool changed = false;
            if (ti.sRGBTexture == linear) { ti.sRGBTexture = !linear; changed = true; }
            var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != want) { ti.textureType = want; changed = true; }
            // metallicsmooth 는 알파에 smoothness 가 들어 있다. 알파를 버리면 광택이 날아간다.
            if (p.Contains("_metallicsmooth") && ti.alphaSource != TextureImporterAlphaSource.FromInput)
            { ti.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (ti.maxTextureSize < 4096) { ti.maxTextureSize = 4096; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }

    static Dictionary<string, Material> BuildMaterials()
    {
        Directory.CreateDirectory(MatDir);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var outp = new Dictionary<string, Material>();

        foreach (var kv in Mats)
        {
            var path = $"{MatDir}/{kv.Key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            var d = kv.Value;

            m.SetColor("_BaseColor", d.BaseColor ?? Color.white);
            if (d.Tex != null)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{d.Dir}/{d.Tex}");
                if (t != null)
                {
                    m.SetTexture("_BaseMap", t);
                    if (d.Tiling != Vector2.zero) m.SetTextureScale("_BaseMap", d.Tiling);
                }
                else Debug.LogWarning($"[Yeouido63] 텍스처 없음: {d.Dir}/{d.Tex}");
            }
            else if (d.BaseColor == null) m.SetColor("_BaseColor", new Color(0.86f, 0.86f, 0.84f));

            m.SetFloat("_Metallic", d.Metallic);
            m.SetFloat("_Smoothness", d.Smoothness);
            if (d.MetalSmoothTex != null)
            {
                var ms = AssetDatabase.LoadAssetAtPath<Texture2D>($"{d.Dir}/{d.MetalSmoothTex}");
                if (ms != null)
                {
                    m.SetTexture("_MetallicGlossMap", ms);
                    m.SetFloat("_Metallic", 1f);
                    m.SetFloat("_Smoothness", 1f);
                    m.SetFloat("_SmoothnessTextureChannel", 0f);   // Metallic Alpha
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }
            if (d.NormalTex != null)
            {
                var nm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{d.Dir}/{d.NormalTex}");
                if (nm != null)
                {
                    m.SetTexture("_BumpMap", nm);
                    if (d.Tiling != Vector2.zero) m.SetTextureScale("_BumpMap", d.Tiling);
                    m.SetFloat("_BumpScale", 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
            }
            EditorUtility.SetDirty(m);
            outp[kv.Key] = m;
        }
        AssetDatabase.SaveAssets();
        return outp;
    }

    static Material MakeSkybox()
    {
        var ti = AssetImporter.GetAtPath(HdrPath) as TextureImporter;
        if (ti != null && ti.textureShape != TextureImporterShape.Texture2D)
        {
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }
        var m = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (m == null)
        {
            m = new Material(Shader.Find("Skybox/Panoramic")) { name = "Sky_Yeouido" };
            AssetDatabase.CreateAsset(m, SkyMatPath);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture>(HdrPath);
        if (tex != null) m.SetTexture("_MainTex", tex);
        m.SetFloat("_Rotation", SkyRotation);
        m.SetFloat("_Exposure", 1.0f);
        m.SetFloat("_Mapping", 1f);
        m.SetFloat("_ImageType", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
