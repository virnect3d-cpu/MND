// 볼류메트릭 구름을 씬 오브젝트로 잡는다
//
// 왜 필요한가
//   이 구름은 Volume 프로파일 안의 오버라이드일 뿐이라 하이어라키에
//   잡히는 게 없다. 고도를 바꾸려면 Post 에셋을 열고 VolumetricClouds
//   섹션을 펼쳐 bottomAltitude 를 찾아야 하는데, 씬 뷰를 보면서 만지기엔
//   동선이 나쁘다. 무엇보다 "구름이 지금 어디 떠 있나" 를 볼 방법이 없다.
//
//   이 컴포넌트는 GameObject 하나를 구름의 손잡이로 만든다. Transform 을
//   끌면 고도와 수평 오프셋이 따라가고, 씬 뷰에 구름층이 상자로 그려진다.
//
// Transform 이 실제로 무엇에 연결되나 — 껍데기가 아니다
//   position.y  -> bottomAltitude  (구름층 바닥 고도, m)
//   position.xz -> shapeOffset.xz  (구름 무늬를 수평으로 민다, m)
//   rotation.y  -> globalOrientation (바람 방위. 나침반식이라 변환이 필요)
//
//   즉 오브젝트를 위로 끌면 구름이 실제로 올라간다. 옆으로 끌면 같은
//   구름이 흘러간 것처럼 무늬가 밀린다.
//
// 드리프트 — 구름 덩어리를 통째로 흘려보낸다
//   globalSpeed 는 구름이 "제자리에서 뭉개지고 굴러가는" 속도라, 아무리
//   올려도 덩어리가 옆으로 지나가지는 않는다. 판 전체를 미는 건
//   shapeOffset 뿐이다. 그래서 시간에 따라 오프셋을 누적한다.
//
//   Animation 클립과 같이 써도 안 싸우도록 쓰는 곳을 갈라 놨다.
//     Animation -> transform.position  (기준점을 옮긴다)
//     드리프트  -> _drift 내부 누적값
//     최종      -> shapeOffset = position.xz + _drift
//   서로 덮어쓰지 않고 더해지므로 "마지막에 쓴 쪽이 이기는" 문제가 없다.
//   클립으로 기준점을 잡고 드리프트로 흐름을 얹으면 된다.
//
// 왜 프로파일을 직접 쓰나
//   VolumeProfile 은 에셋이라 여기 쓴 값이 디스크에 남는다. 플레이 모드에서
//   만진 값도 그대로 남는다는 뜻이다 — Volume 오버라이드의 원래 성질이라
//   피할 수 없고, 실제로 이 프로젝트는 지금까지 그렇게 써 왔다.
//   대신 오버라이드 스위치를 반드시 같이 켠다. 값만 쓰고 스위치가 꺼져
//   있으면 조용히 무시되는 게 이 시스템에서 제일 흔한 함정이다.

using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[AddComponentMenu("Yeouido 63/Cloud Layer Handle")]
public class CloudLayerHandle : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("VolumetricClouds 오버라이드가 든 프로파일. 비우면 같은 오브젝트의 Volume 에서 찾는다.")]
    public VolumeProfile profile;

    [Header("Transform 연동")]
    [Tooltip("Y 위치를 구름층 바닥 고도로 쓴다")]
    public bool driveAltitude = true;

    [Tooltip("XZ 위치를 구름 무늬 오프셋으로 쓴다")]
    public bool driveOffset = true;

    [Tooltip("Y 회전을 바람 방위로 쓴다. 바람 동기화와 충돌하니 평소엔 꺼 둔다.")]
    public bool driveOrientation = false;

    [Header("구름층")]
    [Tooltip("구름층 두께(m). 바닥 고도에서 이만큼 위까지가 구름이다.")]
    [Min(100f)]
    public float thickness = 120f;

    [Tooltip("짙기. 0 이면 안 보이고 1 이면 꽉 찬다.")]
    [Range(0f, 1f)]
    public float density = 0.14f;

    [Header("드리프트 — 덩어리째 흐르기")]
    [Tooltip("구름 덩어리가 흘러가는 속도(m/s). 0 이면 제자리에서 뭉개지기만 한다. " +
             "shapeOffset 은 원근 감쇠가 없어서 지상 바람만큼 주면 너무 빠르다.")]
    [Min(0f)]
    public float driftSpeed = 0.4f;

    [Tooltip("흐르는 방향(XZ). 정규화해서 쓴다. (1,0)=+X, (0,-1)=-Z")]
    public Vector2 driftDirection = new Vector2(1f, 0f);

    [Tooltip("에디터에서 플레이 중이 아닐 때도 흐른다")]
    public bool driftInEditMode = true;

    [Header("흐름 — 제자리에서 굴러가기")]
    [Tooltip("전체 속도 배율. 상한이 없다. 이동과는 무관하고 모양이 변하는 속도다. " +
             "아래 두 값을 곱하므로 셋 중 하나만 낮추면 체감이 잘 안 바뀐다.")]
    [Min(0f)]
    public float speed = 0.3f;

    [Tooltip("형상이 굴러가는 속도. 상한 1 이다.")]
    [Range(0f, 1f)]
    public float shapeSpeed = 0.35f;

    [Tooltip("가장자리가 헐리는 속도. 상한 1 이다.")]
    [Range(0f, 1f)]
    public float erosionSpeed = 0.25f;

    [Header("씬 뷰")]
    [Tooltip("구름층을 상자로 그린다")]
    public bool drawGizmo = true;

    [Tooltip("기즈모 상자의 가로 폭(m). 표시용이라 구름 크기와는 무관하다.")]
    [Min(10f)]
    public float gizmoExtent = 400f;

    // 리플렉션으로 잡는다. VolumetricCloudsVolume 은 별도 asmdef 라
    // 여기서 타입으로 참조하면 어셈블리 의존이 생긴다. 패키지를 빼면
    // 컴파일이 통째로 깨지므로, 없으면 조용히 놀도록 이름으로 찾는다.
    const string CloudTypeName = "VolumetricClouds";

    VolumeComponent _clouds;

    // 드리프트 누적값. transform.position 과 따로 들고 있어야
    // Animation 클립이 position 을 써도 흐름이 지워지지 않는다.
    Vector2 _drift;
    float   _lastTime;

    void OnEnable()
    {
        Resolve();
        _lastTime = Time.realtimeSinceStartup;
        Apply();
#if UNITY_EDITOR
        // 에디터 비플레이 모드는 화면에 뭔가 바뀔 때만 Update 를 돌린다.
        // 그대로 두면 드리프트가 띄엄띄엄 움직이고, 창이 비포커스면
        // 거의 멈춘다. 매 에디터 틱마다 플레이어 루프를 돌려 달라고 건다.
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

#if UNITY_EDITOR
    void OnDisable()
    {
        UnityEditor.EditorApplication.update -= EditorTick;
    }

    void EditorTick()
    {
        if (this == null) { UnityEditor.EditorApplication.update -= EditorTick; return; }
        if (!isActiveAndEnabled || Application.isPlaying) return;
        if (driftSpeed <= 0f || !driftInEditMode) return;
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }
#endif

    void OnValidate() { Resolve(); Apply(); }

    void Update()
    {
        bool moved = false;

        if (driftSpeed > 0f && (Application.isPlaying || driftInEditMode))
        {
            // 에디터 비플레이 모드에서는 deltaTime 이 0 에 가깝거나 불규칙하다.
            // 그대로 쓰면 씬 뷰를 건드릴 때만 찔끔 움직인다.
            //
            // 상한을 0.1 로 잡았더니 실측 12 m/s 가 0.8 m/s 로 나왔다.
            // 에디터가 비포커스면 Update 간격이 0.1 초를 훌쩍 넘는데,
            // 초과분이 전부 버려져서 속도가 7% 로 떨어진 것이다.
            // 0.5 로 올려 대부분의 간격을 그대로 쓴다. 상한 자체는 남겨야
            // 한다 — 컴파일이나 임포트로 몇 초씩 멈춘 뒤 그만큼을 한 번에
            // 반영하면 구름이 순간이동한다.
            float dt = Application.isPlaying
                ? Time.deltaTime
                : Mathf.Clamp(Time.realtimeSinceStartup - _lastTime, 0f, 0.5f);
            _lastTime = Time.realtimeSinceStartup;

            var dir = driftDirection.sqrMagnitude > 1e-6f
                ? driftDirection.normalized
                : Vector2.right;

            _drift += dir * driftSpeed * dt;

            // 무한히 키우지 않는다. 노이즈가 반복되는 주기보다 훨씬 큰
            // 값에서 감아 주면 눈에 안 띄면서 float 정밀도가 안 깨진다.
            // 10만 m 쯤 가면 소수점 아래가 뭉개져 구름이 계단처럼 튄다.
            const float Wrap = 100000f;
            _drift.x = Mathf.Repeat(_drift.x, Wrap);
            _drift.y = Mathf.Repeat(_drift.y, Wrap);

            moved = true;
        }
        else
        {
            _lastTime = Time.realtimeSinceStartup;
        }

        // Transform 은 인스펙터 밖(씬 뷰 드래그, Animation)에서도 바뀌므로
        // OnValidate 만으로는 못 따라간다.
        if (transform.hasChanged) { moved = true; transform.hasChanged = false; }

        if (moved) Apply();
    }

    void Resolve()
    {
        if (profile == null)
        {
            var vol = GetComponent<Volume>();
            if (vol != null) profile = vol.sharedProfile;
        }
        _clouds = null;
        if (profile == null) return;

        foreach (var c in profile.components)
            if (c != null && c.GetType().Name == CloudTypeName) { _clouds = c; break; }
    }

    /// <summary>인스펙터/Transform 값을 프로파일에 밀어 넣는다.</summary>
    public void Apply()
    {
        if (_clouds == null) return;

        var p = transform.position;

        if (driveAltitude)
        {
            // bottomAltitude 는 MinFloatParameter(min 0.01) 라 0 이하면 잘린다.
            // 오브젝트를 지면 아래로 끌어도 구름이 사라지지 않게 막아 준다.
            Set("bottomAltitude", Mathf.Max(p.y, 0.01f));
        }

        if (driveOffset)
        {
            // 기준점(Transform) + 드리프트 누적. 더하기라 Animation 이
            // position 을 움직여도 흐름이 지워지지 않는다.
            //
            // 세로 성분은 고도가 담당하므로 0 으로 둔다. 여기에 y 를 넣으면
            // 고도와 이중으로 걸려 오브젝트를 올릴 때 두 배로 움직인다.
            SetVec3("shapeOffset", new Vector3(p.x + _drift.x, 0f, p.z + _drift.y));
        }

        if (driveOrientation)
        {
            // globalOrientation 은 북(+Z)에서 시계방향으로 재는 나침반식이고
            // Transform 의 Y 오일러각은 +Z 에서 +X 로 도는 같은 방향이라
            // 값 자체는 그대로 통한다. 다만 0~360 으로 감아야 한다 —
            // ClampedFloatParameter(0,360) 이라 음수는 0 으로 잘린다.
            Set("globalOrientation", Mathf.Repeat(transform.eulerAngles.y, 360f));
        }

        Set("altitudeRange", Mathf.Max(thickness, 100f));   // MinFloatParameter(min 100)
        Set("densityMultiplier", density);
        Set("globalSpeed", speed);
        Set("shapeSpeedMultiplier", shapeSpeed);
        Set("erosionSpeedMultiplier", erosionSpeed);
    }

    /// <summary>프로파일의 현재 값을 인스펙터/Transform 으로 되읽는다.</summary>
    public void Pull()
    {
        Resolve();
        if (_clouds == null) return;

        var p = transform.position;
        if (TryGet("bottomAltitude", out float alt)) p.y = alt;

        // 프로파일에 든 오프셋은 기준점 + 드리프트다. 그대로 위치에 넣으면
        // 흘러간 만큼이 기준점으로 굳어서, 되읽기를 누를 때마다 구름이
        // 점점 멀어진다. 드리프트를 빼고 기준점만 되돌린다.
        //
        // 단 스크립트 리로드 직후에는 _drift 가 0 이라 뺄 값이 없다.
        // 그때 누적분을 그대로 받으면 기준점이 수천 m 로 튄다.
        // 드리프트를 쓰는 중이면 되읽기 자체를 건너뛴다 — 수평 기준점은
        // 어차피 노이즈를 어디서 자르느냐일 뿐이라 잃을 정보가 없다.
        bool drifting = driftSpeed > 0f;
        if (!drifting && TryGetVec3("shapeOffset", out var off))
        {
            p.x = off.x - _drift.x;
            p.z = off.z - _drift.y;
        }
        transform.position = p;

        if (TryGet("altitudeRange", out float r))      thickness    = r;
        if (TryGet("densityMultiplier", out float d))  density      = d;
        if (TryGet("globalSpeed", out float s))        speed        = s;
        if (TryGet("shapeSpeedMultiplier", out float ss))   shapeSpeed   = ss;
        if (TryGet("erosionSpeedMultiplier", out float es)) erosionSpeed = es;
    }

    // --- 파라미터 접근 -------------------------------------------------
    //
    // VolumeParameter<T> 는 value 와 overrideState 를 따로 들고 있다.
    // overrideState 가 꺼진 채 value 만 쓰면 렌더러가 그 값을 아예 안 읽는다.
    // 그래서 쓸 때마다 둘 다 세팅한다.

    FieldInfo Field(string name) => _clouds.GetType().GetField(name);

    void Set(string name, float v)
    {
        var f = Field(name);
        if (f == null) return;
        if (f.GetValue(_clouds) is VolumeParameter<float> param)
        {
            param.value = v;
            param.overrideState = true;
        }
    }

    void SetVec3(string name, Vector3 v)
    {
        var f = Field(name);
        if (f == null) return;
        if (f.GetValue(_clouds) is VolumeParameter<Vector3> param)
        {
            param.value = v;
            param.overrideState = true;
        }
    }

    bool TryGet(string name, out float v)
    {
        v = 0f;
        var f = Field(name);
        if (f == null) return false;
        if (f.GetValue(_clouds) is VolumeParameter<float> param) { v = param.value; return true; }
        return false;
    }

    bool TryGetVec3(string name, out Vector3 v)
    {
        v = Vector3.zero;
        var f = Field(name);
        if (f == null) return false;
        if (f.GetValue(_clouds) is VolumeParameter<Vector3> param) { v = param.value; return true; }
        return false;
    }

    // --- 씬 뷰 ---------------------------------------------------------

    void OnDrawGizmos()
    {
        if (!drawGizmo) return;

        var p = transform.position;
        float t = Mathf.Max(thickness, 100f);
        var center = new Vector3(p.x, p.y + t * 0.5f, p.z);
        var size   = new Vector3(gizmoExtent, t, gizmoExtent);

        // 짙기에 맞춰 투명도를 준다. 숫자를 안 봐도 대충 감이 온다.
        Gizmos.color = new Color(0.6f, 0.75f, 0.95f, Mathf.Lerp(0.06f, 0.3f, density));
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.6f, 0.75f, 0.95f, 0.9f);
        Gizmos.DrawWireCube(center, size);

        // 바닥 고도를 명확히 — 핸들이 가리키는 게 바닥이라는 걸 보이게 한다.
        Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(p, new Vector3(gizmoExtent, 0f, gizmoExtent));
    }
}
