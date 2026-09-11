// 먼지 / 잔디 / 구름의 바람을 한 방향·한 속도로 맞춘다
//
// 메뉴: Tools/Yeouido 63/바람 동기화
//
// 왜 필요한가
//   셋이 각자 다른 방향으로 움직이면 같은 바람이 아니라 서로 무관한
//   현상 셋으로 보인다. 특히 잔디가 먼지보다 느리면 "먼지만 빠르게
//   지나가는" 어색한 그림이 된다.
//
//   기준값은 SetupDustParticles 에 있다. 먼지가 바람의 원본이고
//   나머지가 거기 맞춘다.
//
// 단위가 서로 다르다 — 여기가 핵심
//   먼지  : m/s. 그대로 속도다.
//   잔디  : _WindSpeed 는 "초당 진동수"지 속도가 아니다. 위상항이
//           sin(t*S + d*F) 라 등위상선은 t*S + d*F = const, 즉 물결이
//           지면을 훑는 속도는 S/F [m/s] 다. 그래서 S = 풍속 * F 로 준다.
//           S 만 올리면 빨리 떨기만 하고 물결은 그대로다.
//   구름  : globalSpeed 는 배율이라 물리 단위가 없다. m/s 로 환산할
//           근거가 없어서 비례만 맞춘다.
//
// 방향
//   WindAngleDeg 한 곳에서 정한다. 구름의 globalOrientation 은 도 단위인데
//   기준축이 달라(나침반식) 그대로 넣으면 안 된다 — 아래 주석 참고.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

public static class SyncWind
{
    const string Flag     = "Temp/yeouido63_syncwind.flag";
    const string GrassMat = "Assets/yeouido63/Runtime/Textures/Grass/M_GrassBlades.mat";
    const string FogMat   = "Assets/yeouido63/Runtime/Fog/M_VolumetricFog.mat";
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";

    // 바람 방향 — XZ 평면 각도. 0 도면 순수 +X, 90 도면 순수 +Z 다.
    //
    //     0도 = +X      90도 = +Z     180도 = -X     270도 = -Z
    //
    //   지금까지 19 -> 45 -> 30 을 거쳤다. 셋 다 +Z 성분이 있어서 화면
    //   안쪽으로 밀려 들어갔는데, 요청은 -Z 로 흐르는 것이라 270 도다.
    //   순수 -Z 라 X 성분이 0 이고, 카메라 쪽으로 곧장 밀려 나온다.
    //
    //   여기 한 값만 바꾸면 먼지/잔디/안개/구름이 모두 따라온다.
    //   눈으로 보며 고르려면 Tools/Yeouido 63/바람 방향 조절 을 써라.
    public const float WindAngleDeg = 270f;

    // 실제로 쓰이는 각도.
    //
    //   기본은 위 const 지만 WindAnglePicker 가 이걸 덮어써서 리컴파일
    //   없이 방향을 돌린다. const 는 컴파일 시점에 박히므로 런타임에
    //   바꿀 수가 없어서 변수를 따로 뒀다.
    //
    //   SessionState 에 넣는다. 정적 필드만 쓰면 스크립트가 리로드될 때
    //   초기화돼서, 각도를 바꿔 놓고 코드를 한 번 건드리면 조용히 기본값으로
    //   돌아간다 — 왜 되돌아갔는지 찾기 어려운 종류의 버그다.
    const string AngleKey = "yeouido63.windAngle";
    public static float Angle
    {
        get => SessionState.GetFloat(AngleKey, WindAngleDeg);
        set => SessionState.SetFloat(AngleKey, value);
    }

    public static void ApplyAngle(float deg)
    {
        Angle = deg;
        Run();
    }

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[바람] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/바람 동기화")]
    public static void Run()
    {
        float rad = Angle * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        float speed = SetupDustParticles.WindSpeed;              // m/s

        // --- 잔디 ---
        var grass = AssetDatabase.LoadAssetAtPath<Material>(GrassMat);
        if (grass == null) Debug.LogError("[바람] 잔디 머티리얼을 못 찾았다: " + GrassMat);
        else
        {
            grass.SetVector("_WindDir", new Vector4(dir.x, 0f, dir.y, 0f));

            // 물결 속도 = _WindSpeed / _WindFreq 이므로 역산한다.
            //
            // _WindFreq 는 결의 촘촘함. 파장 = 2pi/F 다.
            //   0.22 는 파장 28.6 m 로 옥상 폭과 거의 같아 잔디가 통째로
            //   눕고 결이 안 보였다. 0.62 면 파장 10.1 m 로 화면에 세 번쯤
            //   들어와 바람이 훑고 가는 게 읽힌다.
            const float Freq = 0.62f;
            float freq = Freq;
            grass.SetFloat("_WindFreq", freq);
            float s = speed * freq;

            // 셰이더 슬라이더 상한. 넘으면 잘려서 조용히 느려지므로
            // 그때는 freq 를 낮춰 S/F 비를 지킨다.
            // (상한을 5 -> 20 으로 올렸다. F=0.62 면 S=9.17 이라 5 로는 못 낸다.)
            const float SpeedMax = 20f;
            if (s > SpeedMax)
            {
                freq = SpeedMax / speed;
                s = SpeedMax;
                grass.SetFloat("_WindFreq", freq);
                Debug.Log($"[바람] 잔디 속도가 상한을 넘어 _WindFreq 를 {freq:F4} 로 낮춰 비를 맞췄다.");
            }
            grass.SetFloat("_WindSpeed", s);
            EditorUtility.SetDirty(grass);
            Debug.Log($"[바람] 잔디: dir=({dir.x:F3},{dir.y:F3}) _WindSpeed={s:F2} _WindFreq={freq:F3} " +
                      $"-> 물결 {s / freq:F2} m/s");
        }

        // --- 볼류메트릭 안개 ---
        var fog = AssetDatabase.LoadAssetAtPath<Material>(FogMat);
        if (fog != null)
        {
            fog.SetVector("_WindDir", new Vector4(dir.x, 0f, dir.y, 0f));
            // 안개는 노이즈를 흘리는 속도라 m/s 가 그대로 통한다.
            // 다만 덩어리째 흐르면 눈에 띄므로 먼지보다 느리게 둔다 —
            // 실제로도 큰 기단은 알갱이보다 천천히 움직이는 것처럼 보인다.
            fog.SetFloat("_WindSpeed", speed * 0.5f);

            // 고도 감쇠 — 하늘의 "아지랑이" 를 잡는다.
            //
            //   0.0062 로 두면 구름 고도(250~370m)에서도 밀도배율이
            //   0.21~0.10 이라 완전히 0 이 아니다. _MaxDistance 가 1200m 라
            //   레이가 거기까지 닿고, 노이즈가 바람에 흐르니 하늘이 계속
            //   일렁인다 — 그게 구름 근처에서 보이던 아지랑이다.
            //
            //   0.020 이면 20m 에서 0.70 을 유지해 옥상 주변 공기는 그대로
            //   두면서, 250m 에서 0.007 로 떨어져 하늘이 잠잠해진다.
            //   HeightBase 도 카메라 높이에 맞춰 2 로 올린다. 0 이면 감쇠가
            //   지면부터 시작해 눈높이 안개가 필요 이상으로 옅어진다.
            const float HeightFalloff = 0.020f, HeightBase = 2f;
            fog.SetFloat("_HeightFalloff", HeightFalloff);
            fog.SetFloat("_HeightBase", HeightBase);

            EditorUtility.SetDirty(fog);
            Debug.Log($"[바람] 안개: dir {Angle}도, _WindSpeed={speed * 0.5f:F2}, " +
                      $"HeightFalloff={HeightFalloff} (구름 고도 아지랑이 억제)");
        }

        // --- 구름 ---
        SyncClouds(speed);

        AssetDatabase.SaveAssets();
        Debug.Log($"[바람] 완료. 기준 풍속 {speed:F2} m/s, 방향 {Angle}도.");
    }

    static void SyncClouds(float speed)
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[바람] 포스트 프로파일을 못 찾았다: " + PostPath); return; }

        foreach (var comp in post.components)
        {
            if (comp.GetType().Name != "VolumetricClouds") continue;

            var so = new SerializedObject(comp);

            // 방향. globalOrientation 은 도 단위인데 기준축이 우리 XZ 각도와
            // 다르다 — 북(+Z)에서 시계방향으로 재는 나침반식이다.
            // 우리 각도는 +X 에서 +Z 로 잰 값이라 나침반으로는 90-각도다.
            // 이 변환을 빼먹으면 구름만 엉뚱한 데로 흐른다.
            //
            // 0~360 으로 감아 준다. 이 파라미터는 ClampedFloatParameter(0,360)
            // 이라 음수면 0 으로 잘린다 — 각도가 90 을 넘으면
            // 90-각도가 음수가 되므로 감지 않으면 구름만 엉뚱한 데로 흐른다.
            float compass = Mathf.Repeat(90f - Angle, 360f);
            SetParam(so, "globalOrientation", compass);

            // 속도. 배율이라 m/s 로 환산할 근거가 없다.
            //
            //   1.2 는 너무 느렸다. 구름은 멀리 있어서 같은 속도라도
            //   화면에서 움직이는 각도가 훨씬 작다 — 고도 250 m 에 떠 있고
            //   수 km 밖까지 뻗어 있으니, 지면의 잔디와 같은 배율로 두면
            //   거의 멈춘 것처럼 보인다. 거리 보정 삼아 4 배로 올린다.
            //   (1.2 -> 4.8)
            //
            //   속도를 올릴 수 있는 건 사실상 이것뿐이다. 아래 두 배율은
            //   상한이 1.0 이라(VolumetricCloudsVolume.cs 참고) 거기서
            //   더 빠르게 만들 수 없다. globalSpeed 가 전체를 곱한다.
            //   FloatParameter 라 상한이 없어서 여기는 얼마든 올릴 수 있다.
            //   (1.2 -> 4.8 -> 7.5)
            const float GlobalSpeed = 7.5f;
            SetParam(so, "globalSpeed", GlobalSpeed);

            // 자전 — 구름이 흐르기만 하고 모양이 그대로면 판때기가
            // 미끄러지는 것처럼 보인다. 형상 노이즈를 같이 굴려야
            // 뭉쳤다 풀어지며 떠간다.
            //
            //   둘 다 ClampedFloatParameter(0, 1) 이라 최대가 1.0 이다.
            //   2.4 / 3.0 을 넣으면 조용히 1.0 으로 잘린다 — 값은 적히는데
            //   화면은 안 바뀌어서 적용된 줄 알기 쉽다. 상한으로 둔다.
            const float ShapeSpeed = 1.0f, ErosionSpeed = 1.0f;
            SetParam(so, "shapeSpeedMultiplier", ShapeSpeed);
            SetParam(so, "erosionSpeedMultiplier", ErosionSpeed);

            // 고도 — 값은 250/120 으로 적혀 있는데 override 가 꺼져 있어서
            // 적용이 안 되고 기본값으로 돌고 있었다. 프로파일에 숫자가
            // 보인다고 쓰이는 게 아니다.
            SetParam(so, "bottomAltitude", 250f);
            SetParam(so, "altitudeRange", 120f);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(comp);
            EditorUtility.SetDirty(post);
            // 값을 문자열에 박아 두지 않는다. 예전엔 globalSpeed 를 4.8 로
            // 바꾸고도 로그에는 "1.2" 가 찍혀서, 적용이 안 된 줄 알고
            // 리컴파일을 세 번이나 다시 돌렸다.
            Debug.Log($"[바람] 구름: orientation={compass}도, globalSpeed={GlobalSpeed}, " +
                      $"shapeSpeed={ShapeSpeed}, erosionSpeed={ErosionSpeed}");
            return;
        }
        Debug.LogWarning("[바람] 프로파일에 VolumetricClouds 가 없다.");
    }

    // URP 볼륨 파라미터는 {m_OverrideState, m_Value} 구조다.
    // m_Value 만 바꾸고 override 를 안 켜면 프로파일에 값은 적히는데
    // 화면에는 아무 변화가 없다 — 눈으로 확인하기 전엔 성공한 줄 안다.
    static void SetParam(SerializedObject so, string name, float value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning($"[바람] 구름 파라미터 없음: {name}"); return; }

        var ov = p.FindPropertyRelative("m_OverrideState");
        var v  = p.FindPropertyRelative("m_Value");
        if (ov == null || v == null) { Debug.LogWarning($"[바람] 구조가 다름: {name}"); return; }

        ov.boolValue = true;
        if (v.propertyType == SerializedPropertyType.Float) v.floatValue = value;
        else if (v.propertyType == SerializedPropertyType.Integer) v.intValue = Mathf.RoundToInt(value);
        else { Debug.LogWarning($"[바람] 타입이 숫자가 아님: {name}"); return; }
    }
}


