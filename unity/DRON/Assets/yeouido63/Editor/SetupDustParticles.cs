// 황사 먼지 파티클 — 눈앞에 날리는 알갱이
//
// 메뉴: Tools/Yeouido 63/먼지 파티클 심기  /  먼지 파티클 제거
//
// 왜 필요한가
//   볼류메트릭 안개(VolumetricFog)는 "뿌연 공기"를 만든다. 거리에 따라
//   대비가 죽는 효과라 공간감은 주지만, 눈앞을 스쳐 지나가는 알갱이는 없다.
//   황사의 체감은 그 알갱이에서 나온다.
//
// 왜 카메라 자식으로 붙이나
//   먼지를 씬 전체에 뿌리면 카메라가 어디로 가든 보이게 하려고 수십만
//   파티클이 필요하다. 카메라에 붙여 작은 박스 안에서만 돌리면 몇백 개로
//   같은 인상을 낸다. 어차피 먼지는 개체를 식별하는 대상이 아니라
//   "공기 중에 뭔가 떠 있다" 는 신호라 위치의 절대성이 필요 없다.
//
//   Simulation Space 는 World 로 둔다. Local 로 두면 먼지가 카메라를 그대로
//   따라다녀서 화면에 붙어 있는 것처럼 보인다 — 유리에 먼지가 낀 꼴이다.
//   World 면 카메라가 움직일 때 먼지 사이를 지나가는 느낌이 난다.
//
// 렌더링
//   Additive 가 아니라 Alpha Blend 를 쓴다. 황사는 빛나는 게 아니라
//   빛을 가리는 입자다. Additive 로 하면 반딧불이가 된다.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class SetupDustParticles
{
    const string Flag     = "Temp/yeouido63_dustfx.flag";
    const string RootName = "DUST_Particles";
    const string TexDir   = "Assets/yeouido63/Runtime/Textures/Dust";
    const string TexPath  = TexDir + "/DustParticle.png";
    const string MatPath  = TexDir + "/M_DustParticle.mat";

    // 카메라를 둘러싼 박스. 이 안에서만 먼지가 돈다.
    static readonly Vector3 BoxSize = new Vector3(34f, 18f, 34f);

    const int   MaxParticles = 420;    // 이 정도면 화면이 비어 보이지 않는다
    const float Rate         = 90f;    // 초당 방출
    const float LifeTime     = 5.5f;

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Setup(); }
            catch (System.Exception e) { Debug.LogError("[먼지] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/먼지 파티클 심기")]
    public static void Setup()
    {
        var cam = Camera.main;
        if (cam == null)
            cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                        .FirstOrDefault(c => c.enabled && c.targetTexture == null);
        if (cam == null) { Debug.LogError("[먼지] 카메라를 못 찾았다."); return; }

        Remove();

        var mat = BuildMaterial();

        var go = new GameObject(RootName);
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        Undo.RegisterCreatedObjectUndo(go, "먼지 파티클");

        var ps = go.AddComponent<ParticleSystem>();

        // ParticleSystem 은 모듈이 struct 라 지역변수에 받아 쓴다.
        // (프로퍼티에 직접 대입이 안 된다)
        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(LifeTime * 0.6f, LifeTime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 1.1f);
        // 크기를 아주 작게 — 알갱이지 눈송이가 아니다
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.085f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.80f, 0.70f, 0.50f, 0.30f),
            new Color(0.88f, 0.79f, 0.60f, 0.16f));
        main.maxParticles = MaxParticles;
        main.gravityModifier = 0.008f;              // 거의 안 떨어진다. 공기에 떠 있는 것
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.prewarm = true;                        // 시작하자마자 화면이 차 있게

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = Rate;

        // 카메라를 감싸는 박스에서 방출한다.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = BoxSize;
        shape.position = Vector3.zero;

        // 바람 — 볼류메트릭 안개와 같은 방향(+X, 약간 +Z)으로 흘려야
        // 둘이 따로 노는 것처럼 보이지 않는다.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.25f, 0.35f);
        vel.z = new ParticleSystem.MinMaxCurve(0.4f, 1.3f);

        // 흩날림 — 직선으로만 가면 비 오는 것처럼 보인다
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.35f);
        noise.frequency = 0.28f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.35f);
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.damping = true;

        // 수명 양끝에서 서서히 나타나고 사라지게 — 안 그러면 팝 하고 튄다
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f,    0f),
                new GradientAlphaKey(1f,    0.18f),
                new GradientAlphaKey(1f,    0.75f),
                new GradientAlphaKey(0f,    1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 천천히 도는 것만으로도 살아 있는 느낌이 난다
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.sortingFudge = 0f;

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[먼지] 카메라({cam.name}) 자식으로 파티클 {MaxParticles}개 심음. " +
                  $"박스 {BoxSize}, World 시뮬레이션.");
    }

    [MenuItem("Tools/Yeouido 63/먼지 파티클 제거")]
    public static void Remove()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (go.name == RootName) Undo.DestroyObjectImmediate(go);
    }

    // 가운데가 밝고 가장자리로 갈수록 투명해지는 원. 외부 에셋을 받지 않고
    // 코드로 굽는다 — 먼지 한 알에 텍스처 파일을 끌어올 이유가 없다.
    static Texture2D BuildTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (existing != null) return existing;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            // smoothstep 으로 부드럽게 떨어뜨린다. 선형이면 테두리가 보인다.
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d));
            a *= a;                                   // 중심을 더 조이기
            px[y * S + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply(true);

        Directory.CreateDirectory(TexDir);
        File.WriteAllBytes(TexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceUpdate);

        var ti = AssetImporter.GetAtPath(TexPath) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }

    static Material BuildMaterial()
    {
        var tex = BuildTexture();

        var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        // URP 의 파티클 전용 셰이더. 없으면 Unlit 으로 떨어진다.
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (m == null)
        {
            m = new Material(sh);
            Directory.CreateDirectory(TexDir);
            AssetDatabase.CreateAsset(m, MatPath);
        }
        m.shader = sh;

        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }

        // Alpha Blend — 황사는 빛을 가리는 입자지 빛나는 입자가 아니다.
        // Additive 로 두면 어두운 배경에서 반딧불이처럼 뜬다.
        m.SetFloat("_Surface", 1f);                       // Transparent
        m.SetFloat("_Blend", 0f);                         // Alpha
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", Color.white);

        EditorUtility.SetDirty(m);
        return m;
    }
}
