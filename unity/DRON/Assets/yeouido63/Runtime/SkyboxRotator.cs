// HDRI 스카이박스를 Y 축으로 천천히 돌린다
//
// 왜 필요한가
//   볼류메트릭 구름의 globalSpeed 를 올려도 움직임이 잘 안 보인다.
//   구름이 고도 250 m 에 수 km 밖까지 뻗어 있어서, 같은 속도라도 화면에서
//   변하는 각도가 아주 작기 때문이다. 반면 배경 HDRI 는 화면 전체를 덮고
//   있어서 조금만 돌아도 눈에 들어온다.
//
// 왜 RenderSettings.skybox 를 바꾸지 않나 — 한 번 크게 당했다
//   처음엔 원본 머티리얼을 복제해 HideAndDontSave 인스턴스를 만들고
//   RenderSettings.skybox 를 그걸로 갈아끼웠다. 원본 .mat 이 회전값으로
//   더러워지는 걸 막으려는 의도였는데, 결과는 하늘이 통째로 사라지는
//   것이었다.
//
//   HideAndDontSave 오브젝트는 스크립트 리로드(도메인 리로드) 때 파괴된다.
//   그때 RenderSettings.skybox 는 파괴된 인스턴스를 계속 가리키고 있어서
//   null 이 되고, 그 상태로 씬이 저장되면 m_SkyboxMaterial: {fileID: 0} 이
//   디스크에 박힌다. 되돌릴 훅(OnDisable)이 불리기 전에 참조가 끊기므로
//   방어가 안 된다.
//
//   그래서 지금은 RenderSettings.skybox 를 절대 건드리지 않는다. 원본
//   머티리얼의 _Rotation 만 돌리고, 꺼질 때 원래 각도로 되돌린다.
//   회전값이 .mat 에 남을 수는 있지만 그건 숫자 하나라 복구가 쉽고,
//   하늘이 사라지는 것과는 비교가 안 된다.

using UnityEngine;

[ExecuteAlways]
[AddComponentMenu("Yeouido 63/Skybox Rotator")]
public class SkyboxRotator : MonoBehaviour
{
    [Tooltip("초당 회전 각도. 0.35 면 한 바퀴에 약 17 분.")]
    [Range(0f, 5f)]
    public float degreesPerSecond = 0.35f;

    [Tooltip("에디터에서 플레이 중이 아닐 때도 돌린다")]
    public bool rotateInEditMode = true;

    static readonly int RotationID = Shader.PropertyToID("_Rotation");

    Material _sky;               // 원본. 교체하지 않는다.
    float _angle;
    float _restoreTo;            // 꺼질 때 되돌릴 각도
    bool  _hooked;
    float _lastTime;

    void OnEnable()
    {
        _sky = RenderSettings.skybox;
        if (_sky == null || !_sky.HasProperty(RotationID))
        {
            Debug.LogWarning("[하늘] 스카이박스에 _Rotation 이 없다. 회전을 끈다.");
            enabled = false;
            return;
        }

        _restoreTo = _sky.GetFloat(RotationID);
        _angle = _restoreTo;
        _lastTime = Time.realtimeSinceStartup;
        _hooked = true;
    }

    void OnDisable()
    {
        // 원래 각도로 되돌린다. 안 되돌리면 껐다 켤 때마다 각도가 누적돼
        // .mat 이 계속 바뀌고 git diff 에 뜬다.
        if (_hooked && _sky != null) _sky.SetFloat(RotationID, _restoreTo);
        _hooked = false;
    }

    void Update()
    {
        if (!_hooked || _sky == null) return;
        if (!Application.isPlaying && !rotateInEditMode) return;

        // 에디터 비플레이 모드에서는 deltaTime 이 0 에 가깝거나 불규칙하다.
        // 그대로 쓰면 씬 뷰를 건드릴 때만 찔끔 움직인다.
        float dt = Application.isPlaying
            ? Time.deltaTime
            : Mathf.Clamp(Time.realtimeSinceStartup - _lastTime, 0f, 0.1f);
        _lastTime = Time.realtimeSinceStartup;

        _angle = Mathf.Repeat(_angle + degreesPerSecond * dt, 360f);
        _sky.SetFloat(RotationID, _angle);
    }
}
