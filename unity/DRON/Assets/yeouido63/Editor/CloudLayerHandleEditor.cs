// CloudLayerHandle 인스펙터
//
// 기본 인스펙터로도 되지만 두 가지가 아쉽다.
//   1. 값을 바꿔도 "지금 프로파일에 뭐가 들어갔나" 를 알 수 없다.
//   2. 프로파일을 손으로 고친 경우 핸들과 어긋나는데 맞출 방법이 없다.
// 그래서 현재 상태 요약과 되읽기 버튼을 붙였다.

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CloudLayerHandle))]
public class CloudLayerHandleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var h = (CloudLayerHandle)target;

        EditorGUILayout.Space();

        if (h.profile == null)
        {
            EditorGUILayout.HelpBox(
                "프로파일이 없다. 같은 오브젝트에 Volume 을 붙이거나 위에서 직접 지정해라.",
                MessageType.Warning);
            return;
        }

        var p = h.transform.position;

        string drift;
        if (h.driftSpeed <= 0f)
        {
            drift = "드리프트 꺼짐 — 제자리에서 뭉개지기만 한다";
        }
        else
        {
            var d = h.driftDirection.sqrMagnitude > 1e-6f
                ? h.driftDirection.normalized : Vector2.right;
            // 방향을 말로 풀어 준다. 숫자만 보면 어느 쪽인지 안 읽힌다.
            string axis = Mathf.Abs(d.x) >= Mathf.Abs(d.y)
                ? (d.x >= 0f ? "+X" : "-X")
                : (d.y >= 0f ? "+Z" : "-Z");
            drift = $"드리프트 {h.driftSpeed:F1} m/s → {axis} ({d.x:F2}, {d.y:F2})";
        }

        EditorGUILayout.HelpBox(
            $"구름층 {p.y:F0} ~ {p.y + h.thickness:F0} m\n" +
            $"기준 오프셋 ({p.x:F0}, {p.z:F0}) m\n" +
            $"{drift}\n" +
            $"짙기 {h.density:F2} · 굴러가는 속도 {h.speed:F1}",
            MessageType.Info);

        if (h.driftSpeed > 0f && h.speed > 4f)
        {
            EditorGUILayout.HelpBox(
                "드리프트를 켠 상태에서 '굴러가는 속도'가 높으면 흐르는 방향이 " +
                "모양 변화에 묻힌다. 2~3 정도가 방향이 또렷하다.",
                MessageType.Warning);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("프로파일에 적용"))
            {
                h.Apply();
                EditorUtility.SetDirty(h.profile);
                AssetDatabase.SaveAssets();
            }

            if (GUILayout.Button("프로파일에서 되읽기"))
            {
                Undo.RecordObject(h.transform, "Pull Cloud Layer");
                Undo.RecordObject(h, "Pull Cloud Layer");
                h.Pull();
                EditorUtility.SetDirty(h);
            }
        }

        EditorGUILayout.HelpBox(
            "값은 Volume 프로파일 에셋에 저장된다. 플레이 모드에서 만진 것도 남는다.",
            MessageType.None);
    }

    // 씬 뷰에서 고도를 직접 끌 수 있게 한다. Transform 툴로도 되지만
    // 세로 전용 핸들이 있으면 실수로 XZ 가 따라가는 일이 없다.
    void OnSceneGUI()
    {
        var h = (CloudLayerHandle)target;
        if (!h.driveAltitude) return;

        var p = h.transform.position;

        EditorGUI.BeginChangeCheck();
        Handles.color = new Color(1f, 0.85f, 0.4f, 1f);
        float size = HandleUtility.GetHandleSize(p) * 0.6f;
        var moved = Handles.Slider(p, Vector3.up, size, Handles.ArrowHandleCap, 0f);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(h.transform, "Move Cloud Layer");
            h.transform.position = new Vector3(p.x, Mathf.Max(moved.y, 0.01f), p.z);
            h.Apply();
        }

        Handles.Label(p + Vector3.up * h.thickness, $"구름 {p.y:F0} m");
    }
}
