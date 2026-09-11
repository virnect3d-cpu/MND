// 헬리패드 H 마킹 안쪽에만 잔디를 추가로 심는다
//
// 메뉴: Tools/Yeouido 63/H 안쪽 잔디 심기  /  H 안쪽 잔디 제거
//
// 왜 따로 만들었나
//   원래 배치 마스크(Grass_Placement_Mask.png)는 H 착륙 마킹을 일부러 도려내고
//   만들었다. 그런데 이 씬은 실제 헬기 운용이 아니라 비주얼이 목적이라
//   가운데가 뻥 뚫려 보였다. 그래서 그 구멍을 메웠는데 —
//   PlantRooftopGrass 를 다시 돌리면 Remove() 후 전체 재배치라
//   **손으로 잡아 놓은 기존 잔디 위치가 날아간다.**
//   그래서 기존 GRASS_Helipad 는 건드리지 않고, 새로 열린 영역에만
//   별도 루트로 심는다.
//
// 어디가 "새로 열린 영역" 인가
//   마스크(Grass_Placement_Mask.png)를 두 번 고쳤다 —
//     1) H 원 안쪽 구멍을 메웠다(잔디를 채우려고)
//     2) 그 안에서 흰색 마킹(H 글씨 + 이중 테두리 링)은 다시 뺐다.
//        글씨 위에 풀이 자라면 마킹이 안 보인다.
//   그래서 "원 안 && 마스크 흰색" 이면 새로 열린 영역이다.
//   원 판정을 따로 두는 이유는 기존 잔디(GRASS_Helipad)와 겹치지 않게
//   추가분을 원 안쪽으로만 한정하기 위해서다.
//
//   H 원: 중심 (447, 2222), 반지름 330 — 메운 영역의 bbox 를 직접 재서 얻었다.
//
// 크기
//   기존 잔디는 TuneGrass 로 0.5 배 축소돼 있다. 추가분도 같은 크기여야
//   경계에서 튀지 않으므로 BladeH/BladeW 를 처음부터 절반으로 만든다.
//
// 위치
//   정점을 월드 좌표로 굽기 때문에 루트 트랜스폼은 반드시 항등이어야 한다.
//   자세한 건 Plant() 안 주석 참고.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class PlantHelipadCenter
{
    const string Flag      = "Temp/yeouido63_hcenter.flag";
    const string RootName  = "GRASS_HelipadCenter";
    const string TexDir    = "Assets/yeouido63/Runtime/Textures/Grass";
    const string MaskPath  = TexDir + "/Grass_Placement_Mask.png";
    const string MatPath   = TexDir + "/M_GrassBlades.mat";
    const string MeshPath  = TexDir + "/GrassClumpSmall.asset";
    const string ShaderName = "Yeouido63/GrassWind";

    // H 원 (4096 아틀라스 픽셀, 이미지 행 기준 = 위가 0).
    // 메운 영역의 bbox 를 직접 재서 얻은 값이다: y 1892-2552 / x 117-777.
    // 반지름은 주석에 적혀 있던 286 이 아니라 330 이다 — 도려낼 때 원 테두리
    // 링까지 같이 잘려 나갔기 때문이다. 추정값 말고 실측값을 쓴다.
    const float CX = 447f, CY = 2222f, CR = 330f;
    const int   AtlasSize = 4096;

    // 기존 잔디(0.5 배 축소본)와 같은 크기로 맞춘다
    const float Density      = 14f;
    const float BladeH       = 0.225f;   // 0.45 * 0.5
    const float BladeW       = 0.17f;    // 0.34 * 0.5
    const float HeightJitter = 0.35f;
    const int   MaxClumps    = 60000;

    // TuneGrass.GrassTint 와 같은 값. 머티리얼을 공유하므로 둘이 달라지면
    // 나중에 실행한 쪽이 이긴다 — 헷갈리지 않게 맞춰 둔다.
    static readonly Color GrassTint = new Color(0.96f, 1.00f, 0.88f, 1f);

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Plant(); }
            catch (System.Exception e) { Debug.LogError("[H잔디] 자동 실행 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/H 안쪽 잔디 심기")]
    public static void Plant()
    {
        GameObject roof = null;
        foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (g.name == "RoofTop" && g.GetComponent<MeshFilter>() != null) { roof = g; break; }
        if (roof == null) { Debug.LogError("[H잔디] RoofTop 을 못 찾았다."); return; }

        var mesh = roof.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null || !mesh.isReadable)
        { Debug.LogError("[H잔디] RoofTop 메쉬를 읽을 수 없다 (Read/Write)."); return; }

        var mask = LoadMask();
        if (mask == null) return;

        Remove();

        var clump = BuildClumpMesh();
        var mat   = LoadMaterial();

        var verts = mesh.vertices;
        var uvs   = mesh.uv;
        var tris  = mesh.triangles;
        var tf    = roof.transform;
        if (uvs == null || uvs.Length != verts.Length)
        { Debug.LogError("[H잔디] UV 가 없다."); return; }

        // V 방향 결정 — 삼각형 무게중심으로는 표본이 3 대 3 동점이 나왔다.
        // 옥상 메쉬가 성겨서 H 원 안에 들어오는 삼각형 자체가 몇 개 없다.
        // 그래서 삼각형 내부를 실제로 촘촘히 샘플해서 센다. 심기와 같은 방식의
        // 표본이라 판정이 그대로 결과에 대응한다.
        {
            var probe = new System.Random(7);
            int hitNoFlip = 0, hitFlip = 0;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int j0 = tris[i], j1 = tris[i + 1], j2 = tris[i + 2];
                for (int k = 0; k < 24; k++)
                {
                    float r1 = (float)probe.NextDouble(), r2 = (float)probe.NextDouble();
                    float s = Mathf.Sqrt(r1);
                    Vector2 uv = uvs[j0] * (1f - s) + uvs[j1] * (s * (1f - r2)) + uvs[j2] * (s * r2);
                    if (!SampleMask(mask, uv)) continue;
                    if (InHCircle(uv, false)) hitNoFlip++;
                    if (InHCircle(uv, true))  hitFlip++;
                }
            }
            _flipV = hitFlip >= hitNoFlip;
            Debug.Log($"[H잔디] V 방향 판정: flip={_flipV} (flip {hitFlip} vs noflip {hitNoFlip} 샘플)");
        }

        var root = new GameObject(RootName);
        root.transform.SetParent(roof.transform.parent, false);
        Undo.RegisterCreatedObjectUndo(root, "H 안쪽 잔디");

        // 루트 트랜스폼은 항등으로 둔다.
        //
        //   잔디 정점은 아래에서 tf.TransformPoint() 로 **현재 월드 좌표**로
        //   구워진다. 그러니 루트에 트랜스폼이 걸리면 그만큼 이중으로 적용돼
        //   엉뚱한 곳으로 날아간다.
        //
        //   옆에 있는 GRASS_Helipad 는 pos(-1551,279,-1589)+Y90 회전을 갖고
        //   있는데, 그건 사용자가 손으로 옮겼기 때문이고 그쪽 정점은 옮기기
        //   **전** 좌표로 구워져 있다. 좌표계가 다르므로 그 트랜스폼을 베껴
        //   오면 안 된다 — 실제로 베껴 봤다가 월드 (-1597,-279,1506) 으로
        //   날아갔다. 그러니 여기서는 아무것도 건드리지 않는다.
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale    = Vector3.one;

        var rnd  = new System.Random(631);   // 기존(63)과 다른 시드 — 같은 점에 겹치지 않게
        var mats = new List<Matrix4x4>();
        int placed = 0, outside = 0;

        for (int i = 0; i + 2 < tris.Length && placed < MaxClumps; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            Vector3 a = tf.TransformPoint(verts[i0]);
            Vector3 b = tf.TransformPoint(verts[i1]);
            Vector3 c = tf.TransformPoint(verts[i2]);

            Vector3 n = Vector3.Cross(b - a, c - a);
            float area2 = n.magnitude;
            if (area2 <= 1e-6f) continue;
            if (Vector3.Dot(n / area2, Vector3.up) < 0.94f) continue;

            float fn = area2 * 0.5f * Density;
            int count = Mathf.FloorToInt(fn);
            if (rnd.NextDouble() < (fn - count)) count++;

            for (int k = 0; k < count && placed < MaxClumps; k++)
            {
                float r1 = (float)rnd.NextDouble(), r2 = (float)rnd.NextDouble();
                float s = Mathf.Sqrt(r1);
                float u = 1f - s, v = s * (1f - r2), w = s * r2;

                Vector2 uv = uvs[i0] * u + uvs[i1] * v + uvs[i2] * w;

                // H 원 안쪽이면서 마스크가 흰 곳만. 두 조건을 모두 요구해야
                // 원이 잔디판 밖으로 삐져나온 부분(있다면)에 심지 않는다.
                if (!InHCircle(uv) || !SampleMask(mask, uv)) { outside++; continue; }

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
            Debug.LogError($"[H잔디] 심을 곳을 못 찾았다. 후보 거부 {outside}개. " +
                           "H 원 좌표(447,2222,r330)가 이 아틀라스와 안 맞을 수 있다.");
            return;
        }

        const int per = 1000;
        int batches = 0;
        for (int s = 0; s < mats.Count; s += per)
        {
            int cnt = Mathf.Min(per, mats.Count - s);
            var cis = new CombineInstance[cnt];
            for (int j = 0; j < cnt; j++) cis[j] = new CombineInstance { mesh = clump, transform = mats[s + j] };
            var merged = new Mesh { name = $"HGrassBatch_{batches}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            merged.CombineMeshes(cis, true, true);
            merged.RecalculateBounds();

            var go = new GameObject($"HGrassBatch_{batches}");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = merged;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            batches++;
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[H잔디] H 안쪽에 {placed} 포기 / {batches} 배치 추가. 거부 {outside}개. " +
                  "기존 GRASS_Helipad 는 건드리지 않았다.");
    }

    [MenuItem("Tools/Yeouido 63/H 안쪽 잔디 제거")]
    public static void Remove()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (go.name == RootName) Undo.DestroyObjectImmediate(go);
    }

    // UV 를 아틀라스 픽셀로 바꿔 H 원 안쪽인지 본다.
    //
    // V 뒤집기를 추측하지 않는 이유
    //   CX/CY 는 PNG 이미지 행(위가 0)으로 쟀는데, Texture2D.GetPixels() 는
    //   아래가 0 행이다. 그래서 V 를 뒤집어야 하는지 아닌지가 UV 레이아웃에
    //   따라 갈린다. 틀리면 원의 정반대편에 잔디가 박히는데 로그만 봐서는
    //   구분이 안 된다(양쪽 다 "몇 포기 심음"으로 보인다).
    //   그래서 두 방향 모두 후보 수를 세어 많이 잡히는 쪽을 쓴다.
    //   H 원 안은 전부 잔디판이고 반대편은 대부분 마스크 밖이라 차이가 크게 난다.
    static bool _flipV;

    static bool InHCircle(Vector2 uv) { return InHCircle(uv, _flipV); }

    static bool InHCircle(Vector2 uv, bool flip)
    {
        float v = Mathf.Clamp01(uv.y);
        float px = Mathf.Clamp01(uv.x) * (AtlasSize - 1);
        float py = (flip ? (1f - v) : v) * (AtlasSize - 1);
        float dx = px - CX, dy = py - CY;
        return dx * dx + dy * dy <= CR * CR;
    }

    static Color[] _px; static int _mw, _mh;
    static Color[] LoadMask()
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (t == null) { Debug.LogError("[H잔디] 마스크가 없다: " + MaskPath); return null; }
        var ti = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
        if (ti != null && (!ti.isReadable || ti.textureCompression != TextureImporterCompression.Uncompressed))
        {
            ti.isReadable = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.sRGBTexture = false;
            ti.SaveAndReimport();
        }
        // 마스크를 에디터 밖에서 다시 칠했으므로 강제 리임포트로 최신 픽셀을 읽는다
        AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
        t = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        _px = t.GetPixels(); _mw = t.width; _mh = t.height;
        return _px;
    }

    static bool SampleMask(Color[] px, Vector2 uv)
    {
        float u = Mathf.Clamp01(uv.x), v = Mathf.Clamp01(uv.y);
        int x = Mathf.Clamp((int)(u * (_mw - 1)), 0, _mw - 1);
        int y = Mathf.Clamp((int)(v * (_mh - 1)), 0, _mh - 1);
        return px[y * _mw + x].r > 0.5f;
    }

    // 기존 포기 메쉬와 같은 구조(쿼드 3장 = 12정점)지만 처음부터 절반 크기.
    // TuneGrass 가 12정점 단위로 끊어 처리하므로 이 메쉬도 같은 규칙을 지킨다.
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
            int b = verts.Count;
            float hw = BladeW * 0.5f;

            verts.Add(-dir * hw);                       uvs.Add(new Vector2(0, 0)); cols.Add(new Color(1,1,1,0));
            verts.Add( dir * hw);                       uvs.Add(new Vector2(1, 0)); cols.Add(new Color(1,1,1,0));
            verts.Add( dir * hw + Vector3.up * BladeH); uvs.Add(new Vector2(1, 1)); cols.Add(new Color(1,1,1,1));
            verts.Add(-dir * hw + Vector3.up * BladeH); uvs.Add(new Vector2(0, 1)); cols.Add(new Color(1,1,1,1));
            for (int j = 0; j < 4; j++) norms.Add(Vector3.up);

            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        var m = new Mesh { name = "GrassClumpSmall" };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetColors(cols);
        m.SetNormals(norms);  m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        Directory.CreateDirectory(TexDir);
        AssetDatabase.CreateAsset(m, MeshPath);
        return m;
    }

    // 머티리얼은 기존 잔디와 공유한다. 따로 만들면 색을 두 군데서 관리하게 된다.
    static Material LoadMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (m == null)
        {
            var sh = Shader.Find(ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, MatPath);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", GrassTint);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", GrassTint);
        EditorUtility.SetDirty(m);
        return m;
    }
}
