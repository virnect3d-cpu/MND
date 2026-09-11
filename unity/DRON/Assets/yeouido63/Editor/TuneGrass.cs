// 심어 놓은 잔디의 잎 크기/색만 조정 — 배치는 건드리지 않는다
//
// 메뉴: Tools/Yeouido 63/잔디 잎 크기·색 조정
//
// 왜 따로 만들었나
//   PlantRooftopGrass 를 다시 돌리면 Remove() 후 재배치라 **손으로 잡은 위치가
//   날아간다.** 배치는 그대로 두고 굽혀 놓은 메쉬의 정점만 줄이는 게 안전하다.
//
// 어떻게 줄이나
//   GrassBatch_* 메쉬는 포기 수천 개를 CombineMeshes 로 구운 것이라 포기별
//   원점 정보가 없다. 대신 잔디 쿼드의 성질을 이용한다 —
//   정점 컬러 A 가 밑둥 0 / 끝 1 이다. 즉 A=0 인 정점은 지면에 붙어 있고
//   A=1 인 정점이 잎 끝이다. 그래서
//     - 밑둥(A=0) 정점들의 평균을 그 포기의 기준점으로 삼고
//     - 모든 정점을 그 기준점 쪽으로 당긴다
//   이러면 포기가 지면에 붙은 채로 작아진다. 단순히 전체를 스케일하면
//   잔디가 공중에 뜨거나 바닥을 뚫는다.
//
//   포기 구분은 삼각형 연결성으로 한다(한 포기 = 쿼드 3장 = 12정점).
//
// 색
//   옥상 UV 아틀라스의 헬리패드 잔디판 색을 그대로 쓴다. 텍스처 위에 잔디가
//   서는 구조라 둘이 다르면 경계가 뜬다.
//   아틀라스를 밝은 연두로 다시 칠하면서 (122,136,69) -> (149,185,87) 이 됐고,
//   여기 GrassTint 도 같이 옮겼다. 아틀라스를 또 건드리면 이 값도 같이 고쳐라.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class TuneGrass
{
    const string Flag = "Temp/yeouido63_tunegrass.flag";

    // 이미 한 번 0.5 배로 줄였다. 다시 돌려도 더 줄지 않게 1 로 둔다.
    // 더 줄이고 싶으면 이 값을 바꿔서 재실행하면 된다.
    const float Scale = 1.0f;

    // 잎 머티리얼의 틴트.
    //
    // 틴트는 _BaseMap 에 곱해진다. Grass003_Color 자체를 밝은 연두로 다시
    // 칠했으므로(평균 65,73,30 -> 161,206,77) 여기서 또 색을 얹으면 이중으로
    // 물든다. 그래서 틴트는 거의 흰색에 가깝게 두고 아주 약한 연두만 남긴다.
    // 색을 더 바꾸고 싶으면 틴트가 아니라 Grass003_Color 를 고치는 게 맞다 —
    // 그래야 명암 디테일이 같이 따라온다.
    static readonly Color GrassTint = new Color(0.96f, 1.00f, 0.88f, 1f);

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[잔디튠] 실패: " + e); }
        };
    }

    // 잔디판을 칠한 옥상 아틀라스. 디스크에서 직접 고치므로 강제 리임포트가 필요하다.
    const string Atlas = "Assets/yeouido63/Runtime/Models/63_RoofTop/Roof_Top_blinn2_BaseColor.png";

    [MenuItem("Tools/Yeouido 63/잔디 잎 크기·색 조정")]
    public static void Run()
    {
        // 아틀라스를 에디터 밖(파이썬)에서 덮어썼다. 에셋 파이프라인은 파일 변경을
        // 포커스 시점에만 감지하므로 여기서 직접 밀어 넣는다. 이걸 빼면 잎 색만
        // 바뀌고 바닥 텍스처는 옛 올리브색 그대로라 경계가 뜬다.
        if (File.Exists(Atlas))
        {
            AssetDatabase.ImportAsset(Atlas, ImportAssetOptions.ForceUpdate);
            Debug.Log("[잔디튠] 옥상 아틀라스 리임포트 완료: " + Atlas);
        }
        else Debug.LogWarning("[잔디튠] 아틀라스를 못 찾았다: " + Atlas);

        var root = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                         .FirstOrDefault(g => g.name == "GRASS_Helipad");
        if (root == null) { Debug.LogError("[잔디튠] GRASS_Helipad 를 못 찾았다."); return; }

        int meshes = 0, clumps = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = mf.sharedMesh;
            if (m == null) continue;
            clumps += ShrinkInPlace(m);
            meshes++;
        }

        // 색은 머티리얼 한 곳만 고치면 된다
        var mr = root.GetComponentInChildren<MeshRenderer>(true);
        var mat = mr != null ? mr.sharedMaterial : null;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", GrassTint);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", GrassTint);
            EditorUtility.SetDirty(mat);
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[잔디튠] 메쉬 {meshes}개 / 포기 {clumps}개 처리(스케일 {Scale}배). " +
                  $"색 -> RGB({GrassTint.r * 255f:F0},{GrassTint.g * 255f:F0},{GrassTint.b * 255f:F0})");
    }

    // 포기 단위로 밑둥을 고정한 채 축소한다. 반환값 = 처리한 포기 수
    static int ShrinkInPlace(Mesh mesh)
    {
        var verts = mesh.vertices;
        var cols  = mesh.colors;
        if (verts.Length == 0) return 0;
        if (cols == null || cols.Length != verts.Length)
        {
            Debug.LogWarning($"[잔디튠] {mesh.name}: 정점 컬러가 없다. 밑둥 판정 불가 — 건너뜀");
            return 0;
        }

        // 한 포기 = 쿼드 3장 = 12정점. CombineMeshes 는 순서를 유지하므로
        // 12개씩 끊으면 포기 단위가 된다.
        const int VertsPerClump = 12;
        if (verts.Length % VertsPerClump != 0)
        {
            Debug.LogWarning($"[잔디튠] {mesh.name}: 정점 수 {verts.Length} 가 12의 배수가 아니다 — 건너뜀");
            return 0;
        }

        int n = verts.Length / VertsPerClump;
        for (int c = 0; c < n; c++)
        {
            int b = c * VertsPerClump;

            // 밑둥(정점컬러 A 가 0 에 가까운 것)들의 평균 = 이 포기의 지면 기준점
            Vector3 anchor = Vector3.zero; int cnt = 0;
            for (int i = b; i < b + VertsPerClump; i++)
                if (cols[i].a < 0.5f) { anchor += verts[i]; cnt++; }
            if (cnt == 0) continue;
            anchor /= cnt;

            for (int i = b; i < b + VertsPerClump; i++)
                verts[i] = anchor + (verts[i] - anchor) * Scale;
        }

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return n;
    }
}
