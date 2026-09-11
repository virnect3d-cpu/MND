// 옥상(63_RoofTop_ALL) 계층 덤프 — 잔디 깔 영역을 찾기 위한 조사용
//
// 메뉴: Tools/Yeouido 63/옥상 구조 덤프
//
// 왜 필요한가
//   63_BILLD_ALL.fbx 는 70MB 바이너리라 외부에서 노드 이름을 못 읽는다.
//   잔디를 엉뚱한 좌표에 깔지 않으려면 Unity 한테 직접 물어보는 수밖에 없다.
//   각 메쉬의 월드 바운즈 + 위쪽을 향한 평평한 면의 넓이를 뽑아서
//   "옥상 바닥 후보"를 넓이 순으로 정렬해 준다.
//
// 결과는 Console 과 Desktop/rooftop_dump.txt 양쪽에 쓴다.
// (Console 은 길면 잘려서 파일로도 남긴다)

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class DumpRoofTop
{
    // 덤프 전용 플래그. Yeouido63AutoRun 의 플래그와 반드시 달라야 한다 —
    // 그쪽은 SetupYeouido63.Setup() 을 돌려 **씬을 새로 만들어버리기** 때문에
    // 지금 작업 중인 씬이 날아간다.
    const string Flag = "Temp/yeouido63_dump.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Dump(); }
            catch (System.Exception e) { Debug.LogError("[Dump] 자동 실행 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/옥상 구조 덤프")]
    public static void Dump()
    {
        var root = GameObject.Find("63_RoofTop_ALL");
        if (root == null)
        {
            // 이름이 바뀌었을 수도 있으니 씬 전체에서 RoofTop 이 들어간 걸 찾는다
            root = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                         .FirstOrDefault(g => g.name.Contains("RoofTop") && g.transform.parent == null)
                   ?? Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                         .FirstOrDefault(g => g.name.Contains("RoofTop"));
        }
        if (root == null) { Debug.LogError("[Dump] 63_RoofTop_ALL 을 못 찾았다"); return; }

        var sb = new StringBuilder();
        sb.AppendLine($"ROOT = {Path(root.transform)}");
        sb.AppendLine($"  world pos {root.transform.position}  rot {root.transform.eulerAngles}  scale {root.transform.lossyScale}");
        sb.AppendLine();

        var rends = root.GetComponentsInChildren<MeshRenderer>(true);
        sb.AppendLine($"MeshRenderer 총 {rends.Length}개");
        sb.AppendLine();

        // 위를 향한 평평한 면의 넓이 = 잔디 깔 수 있는 후보
        var rows = new List<(float upArea, float ytop, MeshRenderer mr, string mats)>();
        foreach (var mr in rends)
        {
            var mf = mr.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            float upArea = 0f; float ytop = float.NegativeInfinity;
            if (mesh != null && mesh.isReadable)
            {
                var v = mesh.vertices; var tr = mesh.triangles; var t = mr.transform;
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(v[tr[i]]), b = t.TransformPoint(v[tr[i + 1]]), c = t.TransformPoint(v[tr[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float area = n.magnitude * 0.5f;
                    if (area <= 0f) continue;
                    if (Vector3.Dot(n.normalized, Vector3.up) > 0.94f)   // 거의 수평이고 위를 봄
                    {
                        upArea += area;
                        ytop = Mathf.Max(ytop, a.y);
                    }
                }
            }
            var names = string.Join(",", mr.sharedMaterials.Select(m => m == null ? "null" : m.name));
            rows.Add((upArea, ytop, mr, names));
        }

        sb.AppendLine("=== 위를 향한 평평한 면 넓이 순 (잔디 후보) ===");
        foreach (var r in rows.OrderByDescending(x => x.upArea).Take(25))
        {
            var b = r.mr.bounds;
            sb.AppendLine($"[upArea {r.upArea,10:F1} m2] y_top {r.ytop,8:F2}  {Path(r.mr.transform)}");
            sb.AppendLine($"        bounds center {b.center} size {b.size}");
            sb.AppendLine($"        mats: {r.mats}");
        }

        sb.AppendLine();
        sb.AppendLine("=== 전체 목록 (바운즈) ===");
        foreach (var mr in rends)
        {
            var b = mr.bounds;
            var mf = mr.GetComponent<MeshFilter>();
            int vtx = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount : -1;
            bool readable = (mf != null && mf.sharedMesh != null) && mf.sharedMesh.isReadable;
            sb.AppendLine($"{Path(mr.transform)}  vtx={vtx} readable={readable}");
            sb.AppendLine($"    center {b.center}  size {b.size}");
            sb.AppendLine($"    mats: {string.Join(",", mr.sharedMaterials.Select(m => m == null ? "null" : m.name))}");
        }

        var outPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "/rooftop_dump.txt";
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log(sb.ToString());
        Debug.Log($"[Dump] 파일로도 저장 -> {outPath}");
    }

    static string Path(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
