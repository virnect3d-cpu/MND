// 바람 방향을 슬라이더로 돌려 본다
//
// 메뉴: Tools/Yeouido 63/바람 방향 조절
//
// 왜 필요한가
//   SyncWind.WindAngleDeg 는 const 라 각도를 바꾸려면 코드를 고치고
//   리컴파일해야 한다. 방향은 눈으로 보며 정하는 값이라 그 왕복이 너무
//   느리다 — 실제로 19 -> 45 -> 30 을 오가며 매번 컴파일을 기다렸다.
//
//   여기서 슬라이더를 돌리면 먼지/잔디/안개/구름에 즉시 적용된다.
//   마음에 드는 각도를 찾으면 "코드에 굽기" 로 SyncWind 의 기본값까지
//   바꿔 준다. 안 구우면 다음에 SyncWind 를 돌릴 때 되돌아간다.
//
// 각도 규약
//   XZ 평면에서 +X 를 0 도로 재고 +Z 방향으로 증가한다.
//     0도 = +X 로만 흐름 (화면 가로지름)
//    90도 = +Z 로만 흐름 (화면 안쪽으로)
//   180도를 넘기면 반대 방향으로 흐른다.

using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class WindAnglePicker : EditorWindow
{
    const string SyncPath = "Assets/yeouido63/Editor/SyncWind.cs";

    float _angle = SyncWind.Angle;
    bool  _live  = true;

    [MenuItem("Tools/Yeouido 63/바람 방향 조절")]
    static void Open()
    {
        var w = GetWindow<WindAnglePicker>(true, "바람 방향");
        w.minSize = new Vector2(360, 260);
        w.maxSize = new Vector2(360, 260);
    }

    void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("바람이 흐르는 방향", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "0도 = +X 로만 흐름 (화면을 가로지름)\n" +
            "90도 = +Z 로만 흐름 (화면 안쪽으로)\n" +
            "먼지 / 잔디 / 안개 / 구름이 함께 움직인다.",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        _angle = EditorGUILayout.Slider("각도", _angle, 0f, 360f);
        bool changed = EditorGUI.EndChangeCheck();

        // X:Z 비중을 같이 보여 준다. 각도만으로는 체감이 안 온다 —
        // 45 도가 "Z 로 간다" 고 느껴진 이유가 X:Z 가 1:1 이라서였다.
        float rad = _angle * Mathf.Deg2Rad;
        float cx = Mathf.Cos(rad), cz = Mathf.Sin(rad);
        EditorGUILayout.LabelField($"X 성분 {cx:F3}   Z 성분 {cz:F3}");
        EditorGUILayout.LabelField(
            Mathf.Abs(cz) < 1e-3f ? "X 축 전용"
          : Mathf.Abs(cx) < 1e-3f ? "Z 축 전용"
          : $"X 가 Z 의 {Mathf.Abs(cx / cz):F2} 배");

        EditorGUILayout.Space(4);
        _live = EditorGUILayout.ToggleLeft("슬라이더를 움직이면 바로 적용", _live);
        if (changed && _live) Apply();

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("지금 적용", GUILayout.Height(26))) Apply();
            if (GUILayout.Button("화면 캡처", GUILayout.Height(26)))
                CaptureGameView.Capture();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("자주 쓰는 각도", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var (deg, label) in new[] {
                (0f, "0\n→X"), (30f, "30\n현재"), (90f, "90\n→Z"),
                (180f, "180\n←X"), (270f, "270\n←Z") })
            {
                if (GUILayout.Button(label, GUILayout.Height(34)))
                { _angle = deg; Apply(); }
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "적용만 하면 이번 세션에서만 유지된다. SyncWind 를 다시 돌리면\n" +
            "코드에 적힌 기본값으로 돌아간다.", MessageType.Info);
        if (GUILayout.Button($"이 각도({_angle:F0}도)를 코드에 굽기", GUILayout.Height(26)))
            Bake();
    }

    void Apply()
    {
        SyncWind.ApplyAngle(_angle);
        // 먼지는 방출 속도가 파티클 시스템에 구워져 있어서 다시 심어야 한다.
        // 잔디/안개/구름은 머티리얼·볼륨 값이라 SyncWind 만으로 바뀐다.
        SetupDustParticles.Setup();
        SceneView.RepaintAll();
    }

    // SyncWind.cs 의 const 기본값을 고쳐 둔다.
    //   const 라 런타임에는 못 바꾸지만, 소스를 고쳐 두면 다음 컴파일부터
    //   그 값이 기본이 된다. 정규식으로 그 한 줄만 바꾼다.
    void Bake()
    {
        if (!File.Exists(SyncPath))
        { Debug.LogError("[바람] SyncWind.cs 를 못 찾았다: " + SyncPath); return; }

        string src = File.ReadAllText(SyncPath);
        string pat = @"(public const float WindAngleDeg = )[-\d.]+f;";
        if (!Regex.IsMatch(src, pat))
        { Debug.LogError("[바람] WindAngleDeg 선언을 못 찾았다. 코드가 바뀌었나?"); return; }

        src = Regex.Replace(src, pat, $"${{1}}{_angle:0.###}f;");
        File.WriteAllText(SyncPath, src);
        AssetDatabase.ImportAsset(SyncPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[바람] SyncWind.cs 기본값을 {_angle:F1}도로 구웠다. 컴파일 후 적용된다.");
    }
}
