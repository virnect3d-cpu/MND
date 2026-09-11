// 옥상 헬리패드 잔디 심기 (바람에 흔들리는 잔디 쿼드)
//
// 메뉴: Tools/Yeouido 63/옥상 잔디 심기  /  옥상 잔디 제거
//
// 어디에 심나
//   옥상 바닥(RoofTop)의 UV 아틀라스에서 헬리패드 잔디판이 **이미 초록색으로
//   칠해져 있다.** 그래서 잔디 영역을 좌표로 추측할 필요가 없다. BaseColor 에서
//   초록 픽셀을 뽑아 만든 배치 마스크(Grass_Placement_Mask.png)를 그대로 쓴다.
//     - 헬리패드 H 마킹(중심 픽셀 447,2222 / 반지름 286)은 마스크에서 제외됨.
//       착륙 구역에 풀이 자라면 안 된다.
//     - 대각선 통로가 얇게 새어 들어온 건 채움률(bbox 대비 0.05)로 걸러냈다.
//
// 어떻게 심나
//   RoofTop 메쉬의 삼각형을 돌면서
//     1) 위를 향한(법선 dot up > 0.94) 삼각형만 후보로 삼고
//     2) 그 삼각형 안에 점을 뿌린 뒤
//     3) 그 점의 UV 를 마스크에서 샘플 -> 흰색일 때만 잔디를 놓는다
//   UV 로 판정하기 때문에 메쉬가 회전(-91.9도)해 있어도, 옥상이 고도 280m 에
//   떠 있어도 정확히 초록 영역에만 박힌다. Unity Terrain 을 쓰지 않는 이유도
//   이것 — Terrain 은 회전이 안 되고 월드 축에 정렬된 하이트맵이라 여기 못 쓴다.
//
// 왜 쿼드 교차(cross-quad) 인가
//   잔디 한 포기를 3장의 쿼드를 60도씩 돌려 세운 것으로 만든다. 어느 각도에서
//   봐도 비어 보이지 않으면서 삼각형 수가 적다. 항공 카메라라 실제로는 거의
//   위에서 내려다보므로 이걸로 충분하다.
//
// 바람
//   정점 컬러 A 에 "밑둥=0, 끝=1" 가중치를 구워 두고 셰이더가 그걸로 흔든다.
//   (GrassWind.shader) 밑둥이 안 움직여야 뽑혀 날아가는 것처럼 안 보인다.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class PlantRooftopGrass
{
    // 외부에서 유니티를 조작할 수단이 없을 때 쓰는 자동 실행 플래그.
    // Yeouido63AutoRun 의 플래그(yeouido63_run.flag)와 반드시 달라야 한다 —
    // 그쪽은 씬을 새로 만들어버린다.
    const string Flag = "Temp/yeouido63_grass.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); _silent = true; Plant(); }
            catch (System.Exception e) { Debug.LogError("[잔디] 자동 실행 실패: " + e); }
            finally { _silent = false; }
        };
    }

    const string GrassRoot   = "GRASS_Helipad";
    const string TexDir      = "Assets/yeouido63/Runtime/Textures/Grass";
    const string MaskPath    = TexDir + "/Grass_Placement_Mask.png";
    const string MatPath     = TexDir + "/M_GrassBlades.mat";
    const string MeshPath    = TexDir + "/GrassClump.asset";
    const string ShaderName  = "Yeouido63/GrassWind";

    // 잔디 한 포기당 쿼드 3장, 포기 밀도/크기
    const float Density      = 14f;     // 1 m^2 당 포기 수
    const float BladeH       = 0.45f;   // 포기 높이(m)
    const float BladeW       = 0.34f;   // 포기 폭(m)
    const float HeightJitter = 0.35f;   // 높이 랜덤 +-35%
    const int   MaxClumps    = 60000;   // 안전 상한

    // 자동 실행 중엔 모달 다이얼로그를 띄우면 에디터가 멈춘다
    static bool _silent;
    static void Say(string title, string msg)
    {
        if (_silent) Debug.Log($"[{title}] {msg}");
        else EditorUtility.DisplayDialog(title, msg, "확인");
    }

    [MenuItem("Tools/Yeouido 63/옥상 잔디 심기")]
    public static void Plant()
    {
        var roof = FindRoofTop();
        if (roof == null) { Say("잔디", "RoofTop 메쉬를 못 찾았다."); return; }

        var mf = roof.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null) { Debug.LogError("[잔디] MeshFilter 없음"); return; }
        if (!mesh.isReadable)
        {
            Say("잔디", "RoofTop 메쉬가 Read/Write 꺼져 있다.\n\n" +
                "63_BILLD_ALL.fbx 임포터에서 Read/Write Enabled 를 켜고 다시 실행해라.\n" +
                "(메뉴: Tools/Yeouido 63/FBX 읽기 켜기)");
            return;
        }

        var mask = LoadMask();
        if (mask == null) return;

        Remove();   // 이전 것 지우고 새로 심는다

        var clump = BuildClumpMesh();
        var mat   = BuildMaterial();

        var verts = mesh.vertices;
        var uvs   = mesh.uv;
        var tris  = mesh.triangles;
        var tf    = roof.transform;

        if (uvs == null || uvs.Length != verts.Length)
        { Debug.LogError("[잔디] UV 가 없다. 마스크 판정 불가."); return; }

        var root = new GameObject(GrassRoot);
        root.transform.SetParent(roof.transform.parent, false);
        Undo.RegisterCreatedObjectUndo(root, "잔디 심기");

        var rnd = new System.Random(63);
        var mats = new List<Matrix4x4>();
        int placed = 0, rejected = 0;

        for (int i = 0; i + 2 < tris.Length && placed < MaxClumps; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            Vector3 a = tf.TransformPoint(verts[i0]);
            Vector3 b = tf.TransformPoint(verts[i1]);
            Vector3 c = tf.TransformPoint(verts[i2]);

            Vector3 n = Vector3.Cross(b - a, c - a);
            float area2 = n.magnitude;
            if (area2 <= 1e-6f) continue;
            if (Vector3.Dot(n / area2, Vector3.up) < 0.94f) continue;   // 수평면만

            float area = area2 * 0.5f;
            float fn = area * Density;
            int count = Mathf.FloorToInt(fn);
            if (rnd.NextDouble() < (fn - count)) count++;               // 소수부는 확률로

            for (int k = 0; k < count && placed < MaxClumps; k++)
            {
                // 삼각형 내부 균일 샘플
                float r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble();
                float s = Mathf.Sqrt(r1);
                float u = 1f - s, v = s * (1f - r2), w = s * r2;

                Vector2 uv = uvs[i0] * u + uvs[i1] * v + uvs[i2] * w;
                if (!SampleMask(mask, uv)) { rejected++; continue; }    // 초록 영역 밖

                Vector3 p = a * u + b * v + c * w;
                float sc = 1f + ((float)rnd.NextDouble() * 2f - 1f) * HeightJitter;
                var rot = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                mats.Add(Matrix4x4.TRS(p, rot, new Vector3(1f, sc, 1f)));
                placed++;
            }
        }

        if (placed == 0)
        {
            Object.DestroyImmediate(root);
            Say("잔디", $"심을 곳을 못 찾았다.\n수평 삼각형은 있었지만 마스크(초록 영역) 안에 들어온 점이 없다.\n" +
                $"거부 {rejected}개");
            return;
        }

        // 1000 포기씩 묶어 한 메쉬로 굽는다. 드로우콜을 줄이려는 것.
        const int per = 1000;
        int batches = 0;
        for (int s = 0; s < mats.Count; s += per)
        {
            int cnt = Mathf.Min(per, mats.Count - s);
            var cis = new CombineInstance[cnt];
            for (int j = 0; j < cnt; j++) cis[j] = new CombineInstance { mesh = clump, transform = mats[s + j] };
            var merged = new Mesh { name = $"GrassBatch_{batches}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            merged.CombineMeshes(cis, true, true);
            merged.RecalculateBounds();

            var go = new GameObject($"GrassBatch_{batches}");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = merged;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;  // 풀 그림자는 비싸고 안 보인다
            batches++;
        }

        EditorSceneManagerSave();
        Debug.Log($"[잔디] {placed} 포기 / {batches} 배치. 마스크 밖이라 거부 {rejected}개");
        Say("잔디", $"잔디 {placed} 포기 심음 ({batches} 배치)\n헬리패드 H 는 비워 뒀다.");
    }

    [MenuItem("Tools/Yeouido 63/옥상 잔디 제거")]
    public static void Remove()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (go.name == GrassRoot) Undo.DestroyObjectImmediate(go);
    }

    // FBX 의 Read/Write 를 켠다. 메쉬 정점을 읽어야 잔디를 심을 수 있다.
    [MenuItem("Tools/Yeouido 63/FBX 읽기 켜기")]
    public static void EnableRead()
    {
        var roof = FindRoofTop();
        var path = roof != null ? AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(roof) ?? (Object)roof) : null;
        if (string.IsNullOrEmpty(path))
            foreach (var g in AssetDatabase.FindAssets("63_BILLD_ALL t:Model"))
                path = AssetDatabase.GUIDToAssetPath(g);
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) { Debug.LogError("[잔디] ModelImporter 를 못 찾았다: " + path); return; }
        if (imp.isReadable) { Debug.Log("[잔디] 이미 켜져 있다: " + path); return; }
        imp.isReadable = true;
        imp.SaveAndReimport();
        Debug.Log("[잔디] Read/Write 켬 -> " + path + " (70MB FBX 라 재임포트에 시간이 걸린다)");
    }

    static GameObject FindRoofTop()
    {
        foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (g.name == "RoofTop" && g.GetComponent<MeshFilter>() != null) return g;
        return null;
    }

    static Color[] _px; static int _mw, _mh;
    static Color[] LoadMask()
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (t == null) { Debug.LogError("[잔디] 배치 마스크가 없다: " + MaskPath); return null; }
        var ti = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
        if (ti != null && (!ti.isReadable || ti.textureCompression != TextureImporterCompression.Uncompressed))
        {
            ti.isReadable = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;   // 압축되면 마스크 경계가 뭉개진다
            ti.sRGBTexture = false;
            ti.SaveAndReimport();
            t = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        }
        _px = t.GetPixels(); _mw = t.width; _mh = t.height;
        return _px;
    }

    static bool SampleMask(Color[] px, Vector2 uv)
    {
        // UV 는 0~1 밖으로 나갈 수 있다(타일링). 마스크는 아틀라스라 clamp 가 맞다.
        float u = Mathf.Clamp01(uv.x), v = Mathf.Clamp01(uv.y);
        int x = Mathf.Clamp((int)(u * (_mw - 1)), 0, _mw - 1);
        int y = Mathf.Clamp((int)(v * (_mh - 1)), 0, _mh - 1);
        return px[y * _mw + x].r > 0.5f;
    }

    // 쿼드 3장을 60도씩 돌려 세운 잔디 한 포기
    static Mesh BuildClumpMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing != null) return existing;

        var verts = new List<Vector3>(); var uvs = new List<Vector2>();
        var cols  = new List<Color>();   var tris = new List<int>();
        var norms = new List<Vector3>();

        for (int q = 0; q < 3; q++)
        {
            float ang = q * 60f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 nrm = Vector3.up;                 // 위를 향하게 해야 항공뷰에서 밝게 받는다
            int b = verts.Count;
            float hw = BladeW * 0.5f;

            verts.Add(-dir * hw);                       uvs.Add(new Vector2(0, 0)); cols.Add(new Color(1,1,1,0));
            verts.Add( dir * hw);                       uvs.Add(new Vector2(1, 0)); cols.Add(new Color(1,1,1,0));
            verts.Add( dir * hw + Vector3.up * BladeH); uvs.Add(new Vector2(1, 1)); cols.Add(new Color(1,1,1,1));
            verts.Add(-dir * hw + Vector3.up * BladeH); uvs.Add(new Vector2(0, 1)); cols.Add(new Color(1,1,1,1));
            for (int j = 0; j < 4; j++) norms.Add(nrm);

            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            // 뒷면도 — 양면으로 보이게
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        var m = new Mesh { name = "GrassClump" };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetColors(cols);
        m.SetNormals(norms);  m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        Directory.CreateDirectory(TexDir);
        AssetDatabase.CreateAsset(m, MeshPath);
        return m;
    }

    static Material BuildMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        var sh = Shader.Find(ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, MatPath); }
        m.shader = sh;

        var col = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Grass003_Color.png");
        if (col != null) m.SetTexture("_BaseMap", col);
        if (m.HasProperty("_WindStrength")) m.SetFloat("_WindStrength", 0.12f);
        if (m.HasProperty("_WindSpeed"))    m.SetFloat("_WindSpeed", 1.3f);
        if (m.HasProperty("_Smoothness"))   m.SetFloat("_Smoothness", 0.18f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void EditorSceneManagerSave()
    {
        var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(s);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
        AssetDatabase.SaveAssets();
    }
}
