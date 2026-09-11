// HDRI 스카이박스를 Y 축으로 천천히 돌린다
//
// 왜 필요한가
//   볼류메트릭 구름의 globalSpeed 를 7.5 까지 올려도 움직임이 잘 안 보인다.
//   구름이 고도 250 m 에 수 km 밖까지 뻗어 있어서, 같은 속도라도 화면에서
//   변하는 각도가 아주 작기 때문이다. 반면 배경 HDRI 는 화면 전체를 덮고
//   있어서 1 도만 돌아도 눈에 들어온다.
//
//   그래서 "구름을 더 빠르게" 대신 "하늘 전체를 살살 돌린다". 실제로
//   하늘이 도는 게 아니라 시야가 도는 것처럼 읽혀서 자전처럼 보인다.
//
// 속도를 어떻게 잡나
//   너무 빠르면 지구가 팽이처럼 도는 게 들통난다. 0.35 도/초면 한 바퀴에
//   약 17 분이라, 몇 초만 보면 모르지만 계속 보면 확실히 움직인다.
//
// 머티리얼을 직접 건드리는 문제
//   RenderSettings.skybox 는 에셋을 그대로 참조한다. 여기서 _Rotation 을
//   쓰면 플레이 중 값이 .mat 파일에 눌어붙어 git diff 에 남는다 —
//   구름 머티리얼에서 이미 겪은 일이다. 그래서 인스턴스를 따로 만들어
//   쓰고, 꺼질 때 원래 값으로 되돌린다.

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

    Material _instance;      // 에셋 대신 쓰는 복제본
    Material _source;        // 원본 (복제 대상 추적용)
    float _angle;
    float _originalRotation;

    void OnEnable()
    {
        var sky = RenderSettings.skybox;
        if (sky == null || !sky.HasProperty(RotationID))
        {
            enabled = false;
            Debug.LogWarning("[하늘] 스카이박스에 _Rotation 이 없다. 회전을 끈다.");
            return;
        }

        _source = sky;
        _originalRotation = sky.GetFloat(RotationID);
        _angle = _originalRotation;

        // 에셋을 직접 쓰지 않는다. 안 그러면 회전값이 .mat 에 저장돼
        // 매번 git diff 에 뜬다.
        _instance = new Material(sky) { name = sky.name + " (rotating)" };
        _instance.hideFlags = HideFlags.HideAndDontSave;
        RenderSettings.skybox = _instance;
    }

    void OnDisable()
    {
        // 원본으로 되돌린다. 안 되돌리면 에디터에 복제본이 물려 있다가
        // 리로드 때 사라져서 하늘이 통째로 없어진다.
        if (_source != null) RenderSettings.skybox = _source;
        if (_instance != null)
        {
            if (Application.isPlaying) Destroy(_instance);
            else DestroyImmediate(_instance);
            _instance = null;
        }
    }

    void Update()
    {
        if (_instance == null) return;
        if (!Application.isPlaying && !rotateInEditMode) return;

        // 에디터 비플레이 모드에서는 deltaTime 이 0 에 가깝거나 불규칙하다.
        // 그대로 쓰면 씬 뷰를 건드릴 때만 찔끔 움직인다. 실제 경과 시간을
        // 쓰면 일정하게 돈다.
        float dt = Application.isPlaying
            ? Time.deltaTime
            : Mathf.Min(0.1f, Time.realtimeSinceStartup - _lastTime);
        _lastTime = Time.realtimeSinceStartup;

        _angle = Mathf.Repeat(_angle + degreesPerSecond * dt, 360f);
        _instance.SetFloat(RotationID, _angle);
    }

    float _lastTime;
}
