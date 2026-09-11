// 옥상 리플렉션 프로브 — 유리에 주변 환경을 굽는다
//
// 메뉴: Tools/Yeouido 63/리플렉션 프로브 굽기
//
// 왜 필요한가
//   씬의 반사 소스가 Skybox 하나뿐이었다(m_DefaultReflectionMode: 0).
//   Skybox 반사는 "무한히 먼 하늘"이라 주변 지오메트리를 전혀 모른다.
//   그래서 유리(glase)를 Smoothness 0.96 / Metallic 0.55 짜리 거울로 만들어
//   놔도 하늘만 비쳐서 그냥 하늘색 판때기로 보인다.
//
//   프로브를 옥상에 박고 구우면 옥상 바닥·타워·난간·주변 건물이 큐브맵으로
//   구워져 유리에 박힌다. 반사형 외장 유리에선 이게 리얼리즘의 나머지 절반이다.
//
// 왜 Baked 인가
//   런타임 비용이 0 이다. 이 씬은 정적이라(움직이는 건 잔디와 구름뿐) 실시간
//   프로브를 돌릴 이유가 없다. 잔디는 어차피 반사에 잡혀도 안 보이는 크기고,
//   구름은 Skybox 쪽에서 처리된다.
//
// 배치
//   옥상 유리(Win)의 바운즈 중심 위에 놓고, 박스 크기를 옥상 전체 + 여유로
//   잡는다. 프로브는 월드 축 정렬이라 옥상이 -91.9도 회전해 있어도 상관없다 —
//   박스만 충분히 크면 된다.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

public static class SetupReflectionProbe
{
    const string ProbeName = "ReflectionProbe_RoofTop";
    const string Flag = "Temp/yeouido63_probe.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Setup(); Bake(); }
            catch (System.Exception e) { Debug.LogError("[프로브] 자동 실행 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/리플렉션 프로브 굽기")]
    public static void Setup()
    {
        var roofRoot = GameObject.Find("63_RoofTop_ALL");
        if (roofRoot == null)
        {
            roofRoot = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                             .FirstOrDefault(g => g.name.Contains("RoofTop_ALL"));
        }
        if (roofRoot == null) { Debug.LogError("[프로브] 63_RoofTop_ALL 을 못 찾았다."); return; }

        // 옥상 전체를 감싸는 바운즈를 렌더러에서 모은다
        var rends = roofRoot.GetComponentsInChildren<MeshRenderer>(true);
        if (rends.Length == 0) { Debug.LogError("[프로브] 렌더러가 없다."); return; }

        var bounds = rends[0].bounds;
        foreach (var r in rends) bounds.Encapsulate(r.bounds);

        // 유리가 붙은 오브젝트(Win)를 찾아 그 위에 프로브를 놓는다.
        // 반사가 가장 중요한 게 유리라 거기를 중심으로 잡는 게 맞다.
        var win = rends.FirstOrDefault(r => r.gameObject.name == "Win");
        Vector3 pos = win != null
            ? win.bounds.center + Vector3.up * (win.bounds.extents.y + 6f)
            : new Vector3(bounds.center.x, bounds.max.y - 10f, bounds.center.z);

        var existing = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)
                             .FirstOrDefault(p => p.gameObject.name == ProbeName);
        GameObject go;
        ReflectionProbe probe;
        if (existing != null) { probe = existing; go = existing.gameObject; }
        else
        {
            go = new GameObject(ProbeName);
            go.transform.SetParent(roofRoot.transform.parent, true);
            probe = go.AddComponent<ReflectionProbe>();
            Undo.RegisterCreatedObjectUndo(go, "리플렉션 프로브");
        }

        go.transform.position = pos;

        probe.mode = ReflectionProbeMode.Baked;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.resolution = 512;                 // 유리가 거울급이라 256 이면 뭉갠다
        probe.hdr = true;
        probe.shadowDistance = 600f;            // 파이프라인 그림자 거리와 맞춤
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.cullingMask = ~0;
        probe.importance = 1;
        probe.intensity = 1f;
        probe.boxProjection = true;             // 박스 투영 — 평면 유리에 원근이 맞게 비친다

        // 박스는 **옥상 구조물만** 감싼다.
        //   처음엔 GetComponentsInChildren 로 모은 전체 바운즈를 썼는데
        //   1702 x 1643m 가 나왔다 — 63_RoofTop_ALL 밑에 주변 건물(BLD_63SQUARE,
        //   78 x 254 x 71m)까지 들어 있어서다. boxProjection 박스가 그렇게 크면
        //   유리에 비치는 상의 원근이 어긋난다. 박스는 반사를 "투영할 방"이지
        //   "담을 수 있는 최대 영역"이 아니다.
        //   그래서 옥상 상부(RoofTop / Tower / Win)만으로 다시 잡는다.
        var roofOnly = rends.Where(r => r.gameObject.name == "RoofTop"
                                     || r.gameObject.name == "Tower"
                                     || r.gameObject.name == "Win").ToArray();
        Bounds box;
        if (roofOnly.Length > 0)
        {
            box = roofOnly[0].bounds;
            foreach (var r in roofOnly) box.Encapsulate(r.bounds);
        }
        else box = bounds;

        probe.size = box.size + new Vector3(24f, 24f, 24f);
        probe.center = box.center - go.transform.position;

        EditorUtility.SetDirty(probe);

        // 씬에 저장한 뒤 굽는다. 굽기는 비동기라 Lightmapping 에 맡긴다.
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        Debug.Log($"[프로브] 배치 완료 pos={pos} size={probe.size} res={probe.resolution}\n" +
                  "  이제 Window > Rendering > Lighting 에서 Generate Lighting 을 눌러 구워라.\n" +
                  "  (또는 메뉴: Tools/Yeouido 63/라이팅 굽기)");
    }

    [MenuItem("Tools/Yeouido 63/라이팅 굽기")]
    public static void Bake()
    {
        // 이 씬은 실시간 조명 위주라 라이트맵은 필요 없다. 프로브만 굽는다.
        Lightmapping.BakeAsync();
        Debug.Log("[프로브] 굽기 시작. 진행률은 에디터 우하단에 뜬다.");
    }
}

