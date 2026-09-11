// 황사 먼지 파티클 — 3 층 레이어
//
// 메뉴: Tools/Yeouido 63/먼지 파티클 심기  /  먼지 파티클 제거
//
// 왜 필요한가
//   볼류메트릭 안개(VolumetricFog)는 "뿌연 공기"를 만든다. 거리에 따라
//   대비가 죽는 효과라 공간감은 주지만, 눈앞을 스쳐 지나가는 알갱이는 없다.
//   황사의 체감은 그 알갱이에서 나온다.
//
// 왜 3 층인가
//   단일 시스템으로는 "알갱이" 와 "자욱함" 을 동시에 못 낸다. 알갱이가
//   보이게 키우면 눈송이가 되고, 자욱하게 만들려고 알파를 올리면 판때기가
//   된다 — 실제로 두 번 다 겪었다. 역할을 나눈다.
//
//     HAZE  큰 반투명 시트. 아주 느리고 거의 안 보인다. 공기의 두께 담당.
//     MID   주력. 바람에 실려 흐르는 알갱이. 지금까지 튜닝한 그 층이다.
//     GRIT  카메라 바로 앞 소수의 빠른 알갱이. 렌즈에 스치는 티끌.
//
//   층마다 크기/속도/알파가 다르므로 시차(parallax)가 생겨 깊이가 읽힌다.
//   같은 값이면 아무리 개수를 늘려도 한 장의 평면으로 보인다.
//
// 왜 카메라 자식으로 붙이나
//   먼지를 씬 전체에 뿌리면 카메라가 어디로 가든 보이게 하려고 수십만
//   파티클이 필요하다. 카메라에 붙여 작은 박스 안에서만 돌리면 몇천 개로
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
//
//   Soft Particles 를 켠다. 빌보드가 난간이나 바닥과 만나는 선에서 종이를
//   오려 붙인 것처럼 잘리는데, 뒤에 있는 불투명 면이 가까울수록 알파를
//   낮춰 그 경계를 지운다. URP 에셋의 Depth Texture 가 필요하다
//   (PC_RPAsset 은 켜져 있다 — 볼류메트릭 안개가 이미 쓰는 중).

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
    const string SoftPath = TexDir + "/DustSoft.png";
    const string MatPath  = TexDir + "/M_DustParticle.mat";
    const string HazeMat  = TexDir + "/M_DustHaze.mat";

    // 속력 범위는 여기서 정하고, 방향은 SyncWind.WindAngleDeg 하나로
    // 결정한다. 예전엔 여기에 45 도 성분을 숫자로 박아 뒀는데, 그러면
    // SyncWind 에서 각도를 바꿔도 먼지만 옛 방향에 남는다.
    public const float SpeedMin = 9.3f;
    public const float SpeedMax = 20.2f;

    // 대표 풍속 (중간값, m/s). 잔디/안개/구름이 여기 맞춘다.
    public static float WindSpeed => (SpeedMin + SpeedMax) * 0.5f;

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

        // soft fade 거리 — 뒤 불투명 면이 이 거리 안에 들어오면 알파를 낮춘다.
        //   알갱이는 0.03~0.075m 라 0.6m 로 두면 자기 크기의 20 배 범위에서
        //   페이드가 걸려, 바닥 근처 입자가 통째로 사라진다. 0.25m 면
        //   교차선만 지우고 나머지는 남는다.
        //   haze 는 6~16m 짜리 시트라 넓게(3.5m) 잡아야 벽과 만나는 선이
        //   부드럽게 풀린다.
        var matGrit = BuildMaterial(MatPath, TexPath, soft: 0.25f);
        var matHaze = BuildMaterial(HazeMat, SoftPath, soft: 3.5f);
        if (matGrit == null || matHaze == null)
        {
            Debug.LogError("[먼지] 머티리얼 준비 실패. 파티클을 심지 않는다.");
            return;
        }

        var root = new GameObject(RootName);
        root.transform.SetParent(cam.transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        Undo.RegisterCreatedObjectUndo(root, "먼지 파티클");

        int total = 0;
        total += BuildHaze(root, matHaze);
        total += BuildMid(root, matGrit);
        total += BuildGrit(root, matGrit);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[먼지] 카메라({cam.name}) 자식으로 3 층 {total}개 심음. " +
                  $"Soft Particles 켬, 바람 {SyncWind.WindAngleDeg}도 {WindSpeed:F1} m/s.");
    }

    // ── 1층 HAZE ────────────────────────────────────────────────────────
    // 큰 반투명 시트. 개별 입자로 인식되면 안 된다 — 알파를 아주 낮게 두고
    // 크기를 키워 여러 장이 겹치면서 공기의 두께로만 읽히게 한다.
    //
    // 이 층이 없으면 알갱이만 날아다니고 그 사이가 맑아서, 먼지가 "낀"
    // 게 아니라 "지나가는" 것처럼 보인다.
    static int BuildHaze(GameObject root, Material mat)
    {
        const int Count = 90;
        var go = NewSystem(root, "DUST_Haze", out var ps);

        var main = ps.main;
        main.duration = 12f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        // 크게. 알갱이로 보이면 안 되므로 알파를 0.05 수준으로 눌러 둔다.
        main.startSize = new ParticleSystem.MinMaxCurve(6f, 16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.76f, 0.76f, 0.77f, 0.075f),
            new Color(0.62f, 0.62f, 0.65f, 0.035f));
        main.maxParticles = Count;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.prewarm = true;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = Count / 9f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(70f, 26f, 70f);
        shape.position = new Vector3(0f, 0f, 12f);

        // 느리게 흐른다. 큰 덩어리가 빠르면 시선을 끌어 정체가 드러난다.
        WindVelocity(ps, 0.16f, 0.30f, -0.5f, 0.7f);

        // 천천히 돈다. 큰 시트가 고정돼 있으면 같은 무늬가 겹쳐 보인다.
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);

        FadeInOut(ps, 0.25f, 0.7f);
        SizeGrow(ps, 0.85f, 1.25f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        Finish(rend, mat, -30);   // 가장 뒤에 그린다
        return Count;
    }

    // ── 2층 MID ─────────────────────────────────────────────────────────
    // 주력. 바람에 실려 흐르는 알갱이다. 크기·속도·난류는 앞서 화면을 보며
    // 맞춘 값이라 유지한다. 여기에 크기 커브와 소프트 파티클만 더한다.
    static int BuildMid(GameObject root, Material mat)
    {
        const int Count = 6000;
        var go = NewSystem(root, "DUST_Mid", out var ps);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.56f, 2.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 1.1f);

        // 크기 — 여기가 "먼지냐 눈이냐" 를 가른다.
        //
        //   0.025~0.085m 는 안 보였고(5m 에서 3~10px), 0.09~0.26m 로 키웠더니
        //   눈송이가 됐다(5m 에서 11~32px). 720p/FOV60 기준으로
        //     3~6px  = 알갱이로 읽힌다
        //     10px+  = 눈송이로 보인다
        //   0.03~0.075m 면 5m 에서 3.7~9.4px 라 알갱이 영역이다.
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        // 색 — 누런 황사가 아니라 회색 먼지.
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.74f, 0.74f, 0.75f, 0.85f),
            new Color(0.56f, 0.56f, 0.59f, 0.55f));
        main.maxParticles = Count;
        main.gravityModifier = 0.008f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.prewarm = true;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 2100f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        var box = new Vector3(30f, 11f, 30f);
        shape.scale = box;
        shape.position = new Vector3(-box.x * 0.25f, 0f, box.z * 0.35f);

        //   범위를 넓게(0.55~1.15 배) 흩뿌리는 이유 — 각 축을 같은 비율로
        //   추첨하면 개체 각도가 기준값 근처로 몰려 폭이 39 도밖에 안 됐고,
        //   전부 나란히 흘러 흐름이 한 줄 직선으로 보였다. 범위를 벌리면
        //   평균 방향은 유지되면서 개체마다 각도가 흩어진다.
        WindVelocity(ps, 0.55f, 1.15f, -2.4f, 3.4f);

        // 난류 세기는 바람 속력과의 "비"로 봐야 한다. 절대값만 보면
        // 1.9 가 작지 않아 보이지만 바람이 14.8 m/s 라 12.8% 에 불과했고,
        // 궤적이 직선에서 ±7도밖에 안 꺾여 그냥 직선으로 보였다.
        // 3.2~5.6 이면 ±13~21도라 눈에 휘어지는 게 읽힌다.
        //
        // damping 을 끈다. 켜 두면 노이즈가 속도에 비례해 감쇠되는데,
        // 바람이 빠를수록 난류가 죽어서 빠른 입자일수록 더 직선이 된다.
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(3.2f, 5.6f);
        noise.frequency = 0.42f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(1.4f);
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.damping = false;
        noise.octaveCount = 2;
        noise.octaveMultiplier = 0.55f;
        noise.octaveScale = 2.2f;

        FadeInOut(ps, 0.18f, 0.75f);

        // 크기 커브 — 작게 시작해 중간에 커지고 다시 줄어든다.
        //   알파만 페이드하면 "같은 크기 점이 밝아졌다 어두워지는" 것으로
        //   보인다. 크기가 같이 변해야 멀리서 다가왔다 멀어지는 것처럼 읽힌다.
        //
        //   0.55~1.15 로 잡았다가 먼지가 거의 사라졌다. startSize 가 이미
        //   3.7~9.4px 인데 거기 0.55 를 곱하면 2.1~5.1px 가 되고, 작은 쪽은
        //   1px 미만으로 내려가 렌더링에서 빠진다. 실제로 하늘 영역 알갱이가
        //   수천 개에서 279 개로 줄었다.
        //   커브는 1.0 을 기준으로 ±15% 만 흔든다. 크기 변화는 "느껴지는"
        //   정도면 충분하고, 알갱이가 사라지면 아무 의미가 없다.
        SizeGrow(ps, 0.9f, 1.15f);

        // 회전은 끈다. 아래 Stretch 모드가 입자를 속도 방향으로 정렬하므로
        // 회전값이 무시된다. 켜 두면 인스펙터에서 동작하는 것처럼 보여 헷갈린다.
        var midRot = ps.rotationOverLifetime;
        midRot.enabled = false;

        var rend = go.GetComponent<ParticleSystemRenderer>();

        // 스트레치 빌보드 — 진행 방향으로 늘어난다.
        //   늘임은 절대 길이(m)로 붙는데 입자는 0.03m 다. 예전 값
        //   0.06 x 19m/s = 1.14m 꼬리는 알갱이의 38 배라 빗줄기가 됐다.
        //   0.022 면 0.42m — 알갱이 대비 6~14 배로 "날리는 결" 정도다.
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.022f;
        rend.lengthScale = 1.2f;
        rend.cameraVelocityScale = 0f;
        Finish(rend, mat, 0);
        return Count;
    }

    // ── 3층 GRIT ────────────────────────────────────────────────────────
    // 카메라 코앞을 스치는 소수의 큰 알갱이. 렌즈에 붙을 듯 지나가는 티끌이다.
    //
    // 개수가 적어야 한다. 가까워서 화면을 크게 덮으므로 많으면 시야를
    // 가리고 오버드로도 급증한다. 60 개로 충분히 읽힌다.
    static int BuildGrit(GameObject root, Material mat)
    {
        const int Count = 60;
        var go = NewSystem(root, "DUST_Grit", out var ps);

        var main = ps.main;
        main.duration = 3f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        // 가까우니 월드 크기는 작아도 화면에서는 크게 잡힌다.
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.045f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.78f, 0.78f, 0.79f, 0.7f),
            new Color(0.58f, 0.58f, 0.61f, 0.4f));
        main.maxParticles = Count;
        main.gravityModifier = 0.01f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.prewarm = true;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = Count / 0.7f;

        // 카메라 바로 앞 얇은 판에서만 뿌린다.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(6f, 3.5f, 2.5f);
        shape.position = new Vector3(-1.5f, 0f, 1.6f);

        // 더 빠르게. 가까운 것이 빨리 지나가야 시차로 깊이가 읽힌다.
        WindVelocity(ps, 1.1f, 1.6f, -2f, 2.5f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(4f, 7f);
        noise.frequency = 0.7f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(2f);
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.damping = false;

        FadeInOut(ps, 0.2f, 0.7f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.03f;
        rend.lengthScale = 1.4f;
        rend.cameraVelocityScale = 0f;
        Finish(rend, mat, 30);   // 가장 앞에 그린다
        return Count;
    }

    // ── 공통 ────────────────────────────────────────────────────────────

    static GameObject NewSystem(GameObject root, string name, out ParticleSystem ps)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        ps = go.AddComponent<ParticleSystem>();
        return go;
    }

    // 바람 방향은 SyncWind 의 각도를 따른다. 축별 성분은 그 각도의
    // cos/sin 에 속력 범위를 곱해서 만든다. 배율로 층마다 세기를 바꾼다.
    static void WindVelocity(ParticleSystem ps, float lo, float hi, float yMin, float yMax)
    {
        float rad = SyncWind.WindAngleDeg * Mathf.Deg2Rad;
        float cx = Mathf.Cos(rad), cz = Mathf.Sin(rad);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(SpeedMin * cx * lo, SpeedMax * cx * hi);
        vel.y = new ParticleSystem.MinMaxCurve(yMin, yMax);
        vel.z = new ParticleSystem.MinMaxCurve(SpeedMin * cz * lo, SpeedMax * cz * hi);
    }

    // 수명 양끝에서 서서히 나타나고 사라지게 — 안 그러면 팝 하고 튄다
    static void FadeInOut(ParticleSystem ps, float inT, float outT)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, inT),
                new GradientAlphaKey(1f, outT),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    // 작게 시작 -> 중간에 최대 -> 다시 줄어듦.
    //   sizeMultiplier 를 1 로 두고 커브 자체에 값을 넣는다. 커브가 0~1 을
    //   벗어나면 Unity 가 자동으로 multiplier 를 뽑아내면서 모양이 뭉개진다.
    static void SizeGrow(ParticleSystem ps, float start, float peak)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var c = new AnimationCurve(
            new Keyframe(0f, start),
            new Keyframe(0.45f, peak),
            new Keyframe(1f, start * 0.8f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, c);
    }

    static void Finish(ParticleSystemRenderer rend, Material mat, int fudge)
    {
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        // 층 순서를 고정한다. 안 그러면 카메라가 돌 때마다 정렬이 뒤바뀌어
        // haze 가 알갱이 앞으로 튀어나온다.
        rend.sortingFudge = fudge;
    }

    [MenuItem("Tools/Yeouido 63/먼지 파티클 제거")]
    public static void Remove()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (go.name == RootName) Undo.DestroyObjectImmediate(go);
    }

    // 가운데가 밝고 가장자리로 갈수록 투명해지는 원. 외부 에셋을 받지 않고
    // 코드로 굽는다 — 먼지 한 알에 텍스처 파일을 끌어올 이유가 없다.
    //
    //   pow 로 감쇠 곡선을 바꾼다. 알갱이(2.0)는 중심을 조여 또렷하게,
    //   haze(0.75)는 넓게 퍼뜨려 테두리가 안 보이게 한다. 같은 텍스처를
    //   쓰면 haze 가 "큰 점" 으로 보여 정체가 드러난다.
    static Texture2D BuildTexture(string path, float power, int size)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;

        // 얼룩 — 균일한 원은 아무리 작아도 "완벽한 점" 이라 인공적이다.
        // 저주파 노이즈를 곱해 알갱이마다 모양이 조금씩 다르게 만든다.
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            // smoothstep 으로 부드럽게 떨어뜨린다. 선형이면 테두리가 보인다.
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d));
            a = Mathf.Pow(a, power);

            // 평균이 1 근처가 되게 잡는다. 0.45*n + 0.72 로 두면 평균이
            // 0.945 라 전체가 살짝 옅어지는데, 소프트 파티클 페이드까지
            // 겹치면 그 손실이 눈에 띈다.
            float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.5f + 0.75f;
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * n));
        }
        tex.SetPixels(px);
        tex.Apply(true);

        Directory.CreateDirectory(TexDir);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material BuildMaterial(string matPath, string texPath, float soft)
    {
        bool isHaze = texPath == SoftPath;
        var tex = BuildTexture(texPath, isHaze ? 0.75f : 2.0f, isHaze ? 128 : 64);

        // URP 의 파티클 전용 셰이더.
        //
        //   못 찾으면 중단한다. 예전에 Unlit 으로 폴백하게 뒀는데, 셰이더를
        //   못 찾은 상태에서 머티리얼이 만들어지면 화면이 통째로 마젠타가
        //   된다. 조용히 잘못된 걸 만드느니 로그를 남기고 멈추는 게 낫다.
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
        {
            Debug.LogError("[먼지] URP 파티클 셰이더를 못 찾았다. 머티리얼을 만들지 않는다.");
            return null;
        }

        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null)
        {
            m = new Material(sh);
            Directory.CreateDirectory(TexDir);
            AssetDatabase.CreateAsset(m, matPath);
            // 같은 프레임에 렌더러가 참조하므로 바로 디스크에 반영한다.
            // 이게 없으면 에셋이 아직 없는 상태로 sharedMaterial 에 물려
            // 다음 리로드에서 참조가 끊긴다 — 화면이 마젠타가 된다.
            AssetDatabase.SaveAssets();
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

        // Soft Particles — 빌보드가 불투명 면과 만나는 선에서 종이를 오려
        // 붙인 것처럼 잘리는 걸 없앤다. 뒤 면이 fade 거리 안으로 들어오면
        // 알파를 낮춘다.
        //
        //   _SoftParticleFadeParams 를 같이 써야 한다. 키워드와 Enabled
        //   플래그만 켜고 이 벡터를 안 채우면 near=far=0 이라 셰이더가
        //   0 으로 나누고, 페이드가 전혀 안 걸린다 — 인스펙터에는 켜진
        //   것처럼 보여서 됐다고 착각하기 쉽다.
        //   URP 규약: (near, 1/(far-near), 0, 0)
        m.SetFloat("_SoftParticlesEnabled", 1f);
        m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
        m.SetFloat("_SoftParticlesFarFadeDistance", soft);
        m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / Mathf.Max(soft, 1e-4f), 0f, 0f));
        m.EnableKeyword("_SOFTPARTICLES_ON");

        // 카메라 페이드 — 근평면을 뚫고 들어온 입자가 화면을 통째로
        // 덮는 걸 막는다. 3 층 구조에서 GRIT 이 카메라 코앞을 지나므로
        // 이게 없으면 가끔 화면 전체가 희뿌옇게 번쩍인다.
        const float NearFade = 0.15f, FarFade = 0.85f;
        m.SetFloat("_CameraFadingEnabled", 1f);
        m.SetFloat("_CameraNearFadeDistance", NearFade);
        m.SetFloat("_CameraFarFadeDistance", FarFade);
        m.SetVector("_CameraFadeParams", new Vector4(NearFade, 1f / (FarFade - NearFade), 0f, 0f));
        m.EnableKeyword("_FADING_ON");

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", Color.white);

        EditorUtility.SetDirty(m);
        return m;
    }
}
