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
//   XZ 평면 45 도. 구름의 globalOrientation 은 도(degree) 단위이고
//   기준축이 달라서 그대로 45 를 넣으면 안 된다 — 아래 주석 참고.

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

    // 바람 방향 — XZ 평면에서 45 도
    const float WindAngleDeg = 45f;

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
        float rad = WindAngleDeg * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));   // (0.707, 0.707)
        float speed = SetupDustParticles.WindSpeed;              // m/s

        // --- 잔디 ---
        var grass = AssetDatabase.LoadAssetAtPath<Material>(GrassMat);
        if (grass == null) Debug.LogError("[바람] 잔디 머티리얼을 못 찾았다: " + GrassMat);
        else
        {
            grass.SetVector("_WindDir", new Vector4(dir.x, 0f, dir.y, 0f));

            // 물결 속도 = _WindSpeed / _WindFreq 이므로 역산한다.
            // _WindFreq 는 결의 촘촘함이라 그대로 두고 속도만 맞춘다.
            float freq = grass.HasProperty("_WindFreq") ? grass.GetFloat("_WindFreq") : 0.22f;
            float s = speed * freq;

            // 셰이더 슬라이더 상한이 5 다. 넘으면 잘려서 조용히 느려지므로
            // 그때는 freq 를 낮춰 S/F 비를 지킨다.
            if (s > 5f)
            {
                freq = 5f / speed;
                s = 5f;
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
            EditorUtility.SetDirty(fog);
            Debug.Log($"[바람] 안개: dir 45도, _WindSpeed={speed * 0.5f:F2}");
        }

        // --- 구름 ---
        SyncClouds(speed);

        AssetDatabase.SaveAssets();
        Debug.Log($"[바람] 완료. 기준 풍속 {speed:F2} m/s, 방향 {WindAngleDeg}도.");
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
            // 우리 45 도는 +X 에서 +Z 로 잰 값이라, 나침반으로는 90-45=45.
            // 마침 45 도에서는 두 표기가 같은 값이 되지만, 각도를 바꿀 때
            // 이 변환을 빼먹으면 구름만 엉뚱한 데로 흐른다.
            float compass = 90f - WindAngleDeg;
            SetParam(so, "globalOrientation", compass);

            // 속도. 배율이라 m/s 로 환산할 근거가 없다.
            // 기존 1.2 가 먼지 이전 풍속(14.8 m/s)에서 자연스러웠으니
            // 같은 비로 둔다. 방향만 돌리는 변경이라 속도는 유지가 맞다.
            SetParam(so, "globalSpeed", 1.2f);

            // 자전 — 구름이 흐르기만 하고 모양이 그대로면 판때기가
            // 미끄러지는 것처럼 보인다. 형상 노이즈를 같이 굴려야
            // 뭉쳤다 풀어지며 떠간다.
            SetParam(so, "shapeSpeedMultiplier", 1.0f);
            SetParam(so, "erosionSpeedMultiplier", 0.6f);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(comp);
            EditorUtility.SetDirty(post);
            Debug.Log($"[바람] 구름: orientation={compass}도, globalSpeed=1.2, " +
                      $"shapeSpeed=1.0, erosionSpeed=0.6");
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
