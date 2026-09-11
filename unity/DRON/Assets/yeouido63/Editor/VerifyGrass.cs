// 잔디 배치 검증 — 잔디가 실제로 옥상 위에 있는지 본다
//
// 메뉴: Tools/Yeouido 63/잔디 배치 검증
//
// 왜 필요한가
//   심기 로그의 "몇 포기 심음" 만으로는 제자리인지 알 수 없다. 실제로
//   PlantHelipadCenter 가 수천 미터 떨어진 곳에 심은 적이 있다.
//
// 무엇을 기준으로 보나
//   두 잔디 루트끼리 비교하면 안 된다 — 사용자가 GRASS_Helipad 를 손으로 옮겨
//   놨고 그 정점은 옮기기 전 좌표로 구워져 있어서, 두 루트의 정점 좌표계가
//   서로 다르다. 비교 기준으로 쓸 수 없다.
//   대신 **옥상 메쉬(RoofTop)의 월드 바운즈**를 절대 기준으로 쓴다.
//   잔디는 옥상 위에 있어야 하므로 잔디의 월드 바운즈가 옥상의 XZ 안에 들어가고
//   Y 가 옥상 윗면 근처면 맞는 것이다.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class VerifyGrass
{
    const string Flag = "Temp/yeouido63_verify.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Verify(); }
            catch (System.Exception e) { Debug.LogError("[검증] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/잔디 배치 검증")]
    public static void Verify()
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        var roofGo = all.FirstOrDefault(g => g.name == "RoofTop" && g.GetComponent<MeshRenderer>() != null);
        if (roofGo == null) { Debug.LogError("[검증] RoofTop 을 못 찾았다."); return; }
        var roof = roofGo.GetComponent<MeshRenderer>().bounds;
        Debug.Log($"[검증] 옥상 월드바운즈 center={roof.center} size={roof.size} 윗면Y={roof.max.y:F2}");

        Check("GRASS_Helipad",       all, roof);
        Check("GRASS_HelipadCenter", all, roof);
    }

    static void Check(string name, GameObject[] all, Bounds roof)
    {
        var go = all.FirstOrDefault(g => g.name == name);
        if (go == null) { Debug.LogWarning($"[검증] {name} 없음"); return; }
        var rs = go.GetComponentsInChildren<MeshRenderer>(true);
        if (rs.Length == 0) { Debug.LogWarning($"[검증] {name} 렌더러 없음"); return; }

        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);

        bool xzIn = b.min.x >= roof.min.x - 1f && b.max.x <= roof.max.x + 1f
                 && b.min.z >= roof.min.z - 1f && b.max.z <= roof.max.z + 1f;
        float dy = b.min.y - roof.max.y;      // 잔디 밑둥과 옥상 윗면의 차이
        bool yOk = Mathf.Abs(dy) < 5f;

        Debug.Log($"[검증] {name} 월드바운즈 center={b.center} size={b.size} / " +
                  $"XZ 옥상 안={xzIn} / 옥상윗면과 Y차이={dy:F2}m");

        if (xzIn && yOk) Debug.Log($"[검증] {name} 통과 — 옥상 위에 있다.");
        else Debug.LogWarning($"[검증] {name} 실패 — 옥상을 벗어났다.");
    }
}
