// TEMPORARY: (1) assigns the rooftop FBX materials by mesh name, (2) locates the
// skybox sun without clipping by rendering 6 directions into FLOAT render targets
// (RenderToCubemap clamps to LDR, which made the first attempt pick a cloud), and
// reports the exact _Rotation needed to line the sky sun up with the directional light.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class _TempSunCalib
{
    const string RanKey  = "_TempSunCalib.ran.v2";
    const string RoofDir = "Assets/yeouido63/Runtime/Models/63_RoofTop";
    const int    Res     = 256;
    static int _frames;

    static _TempSunCalib()
    {
        if (SessionState.GetBool(RanKey, false)) return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { _frames = 0; return; }
        if (++_frames < 30) return;
        EditorApplication.update -= Tick;
        SessionState.SetBool(RanKey, true);
        Run();
    }

    [MenuItem("Tools/_Temp/Fix Materials And Calibrate Sun")]
    public static void Run()
    {
        try
        {
            FixMaterials();
            Calibrate();
        }
        catch (System.Exception e) { Debug.LogError("[CALIB] failed: " + e); }
    }

    // What is actually selected in the editor, and how are its submeshes split?
    [MenuItem("Tools/_Temp/Report Selection")]
    public static void ReportSelection()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogWarning("[SEL] nothing selected"); return; }

        string path = go.name;
        for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
        Debug.Log("[SEL] selected: " + path);

        var r = go.GetComponent<Renderer>();
        var mf = go.GetComponent<MeshFilter>();
        if (r == null || mf == null || mf.sharedMesh == null)
        {
            Debug.Log("[SEL] no renderer/mesh on this object; children:");
            foreach (var cr in go.GetComponentsInChildren<Renderer>())
                Debug.Log("[SEL]   child renderer: " + cr.name);
            return;
        }

        var mesh = mf.sharedMesh;
        Debug.Log(string.Format("[SEL] mesh='{0}' verts={1} subMeshes={2} materialSlots={3}",
            mesh.name, mesh.vertexCount, mesh.subMeshCount, r.sharedMaterials.Length));

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            // world-space Y extent of this submesh tells us which one is the top cap
            var verts = mesh.vertices;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var idx in tris)
            {
                float y = go.transform.TransformPoint(verts[idx]).y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            string matName = s < r.sharedMaterials.Length && r.sharedMaterials[s] != null
                ? r.sharedMaterials[s].name : "<none>";
            Debug.Log(string.Format("[SEL]   submesh {0}: tris={1} worldY[{2:F1}..{3:F1}] material='{4}'",
                s, tris.Length / 3, minY, maxY, matName));
        }
    }

    // Give the top cap submesh its own material instead of the gold curtain wall.
    [MenuItem("Tools/_Temp/Split Cap Material")]
    public static void SplitCap()
    {
        var go = Selection.activeGameObject;
        if (go == null) go = GameObject.Find("63_RoofTop_ALL");
        if (go == null) { Debug.LogError("[CAP] no target selected"); return; }

        // the selected object is usually the FBX root, which has no renderer of its
        // own - walk down to the multi-submesh body mesh
        var r = go.GetComponent<Renderer>();
        var mf = go.GetComponent<MeshFilter>();
        if (r == null || mf == null || mf.sharedMesh == null)
        {
            foreach (var child in go.GetComponentsInChildren<MeshFilter>())
            {
                if (child.sharedMesh == null || child.sharedMesh.subMeshCount < 2) continue;
                mf = child;
                r = child.GetComponent<Renderer>();
                go = child.gameObject;
                break;
            }
        }
        if (r == null || mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("[CAP] no mesh with 2+ submeshes found under " + go.name);
            return;
        }
        Debug.Log("[CAP] target = " + go.name + " (submeshes=" + mf.sharedMesh.subMeshCount + ")");
        var mesh = mf.sharedMesh;

        // pick the submesh whose vertices sit highest -> that is the cap
        int capIndex = -1; float bestMinY = float.MinValue;
        var verts = mesh.vertices;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            float minY = float.MaxValue;
            foreach (var idx in mesh.GetTriangles(s))
            {
                float y = go.transform.TransformPoint(verts[idx]).y;
                if (y < minY) minY = y;
            }
            if (minY > bestMinY) { bestMinY = minY; capIndex = s; }
        }
        if (capIndex < 0) { Debug.LogError("[CAP] could not determine cap submesh"); return; }

        string path = RoofDir + "/M_63_Cap.mat";
        var cap = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (cap == null)
        {
            cap = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(cap, path);
        }
        // dark, matte roof deck - deliberately unlike the mirrored gold wall
        var baseTex = Tex("Roof_Top_blinn2_BaseColor");
        var nrmTex  = Tex("Roof_Top_blinn2_Normal");
        if (baseTex != null) cap.SetTexture("_BaseMap", baseTex);
        if (nrmTex  != null) { cap.SetTexture("_BumpMap", nrmTex); cap.EnableKeyword("_NORMALMAP"); }
        cap.SetColor("_BaseColor", new Color(0.32f, 0.33f, 0.34f, 1f));
        cap.SetFloat("_Metallic", 0f);
        cap.SetFloat("_Smoothness", 0.25f);
        cap.SetFloat("_SmoothnessTextureChannel", 1f);
        EditorUtility.SetDirty(cap);
        AssetDatabase.SaveAssets();

        var mats = r.sharedMaterials;
        if (mats.Length < mesh.subMeshCount) System.Array.Resize(ref mats, mesh.subMeshCount);
        mats[capIndex] = cap;
        r.sharedMaterials = mats;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log(string.Format("[CAP] '{0}' submesh {1} (highest, minY={2:F1}) -> M_63_Cap; scene saved",
            go.name, capIndex, bestMinY));
    }

    // Is the flat-brown facade a UV problem or a shading problem? Report the actual
    // UV extents per mesh so the answer is measured, not guessed.
    [MenuItem("Tools/_Temp/Dump UVs")]
    public static void DumpUVs()
    {
        var root = GameObject.Find("63_RoofTop_ALL");
        if (root == null) { Debug.LogWarning("[UV] 63_RoofTop_ALL not in scene"); return; }

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) { Debug.Log("[UV] " + r.name + ": no mesh"); continue; }

            var uv = mesh.uv;
            if (uv == null || uv.Length == 0)
            {
                Debug.LogWarning("[UV] " + r.name + ": NO UV0 CHANNEL (" + mesh.vertexCount + " verts)");
                continue;
            }

            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var t in uv)
            {
                if (t.x < minU) minU = t.x; if (t.x > maxU) maxU = t.x;
                if (t.y < minV) minV = t.y; if (t.y > maxV) maxV = t.y;
            }
            Debug.Log(string.Format("[UV] {0}: verts={1} uv={2}  U[{3:F3}..{4:F3}] V[{5:F3}..{6:F3}]  spanU={7:F3} spanV={8:F3}",
                r.name, mesh.vertexCount, uv.Length, minU, maxU, minV, maxV, maxU - minU, maxV - minV));
        }
    }

    static Texture2D Tex(string f) { return AssetDatabase.LoadAssetAtPath<Texture2D>(RoofDir + "/" + f + ".png"); }

    static Material Facade()
    {
        string path = RoofDir + "/M_63_Facade.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        var t = Tex("WALL");
        if (t != null) m.SetTexture("_BaseMap", t);
        // Gold curtain wall. Metallic was 0.85 originally, which killed the diffuse and
        // left the tower reading as a flat dark-brown box at this camera distance.
        // Keep enough metal for the gold sheen, but let the texture actually show.
        m.SetFloat("_Metallic", 0.15f);
        m.SetFloat("_Smoothness", 0.7f);
        m.SetFloat("_SmoothnessTextureChannel", 1f);   // use slider, not map alpha
        EditorUtility.SetDirty(m);
        Debug.Log("[CALIB] facade material ready, WALL texture=" + (t != null));
        return m;
    }

    static void FixMaterials()
    {
        var root = GameObject.Find("63_RoofTop_ALL");
        if (root == null) { Debug.LogWarning("[CALIB] 63_RoofTop_ALL not in scene"); return; }

        var facade = Facade();
        var top    = AssetDatabase.LoadAssetAtPath<Material>(RoofDir + "/M_RoofTop.mat");
        var tower  = AssetDatabase.LoadAssetAtPath<Material>(RoofDir + "/M_RoofTower.mat");
        AssetDatabase.SaveAssets();

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            Material pick;
            switch (r.name)
            {
                case "BLD_63SQUARE": pick = facade; break;   // tower body: gold curtain wall
                case "RoofTop":      pick = top;    break;
                case "Tower":        pick = tower;  break;
                case "Win":          pick = facade; break;   // rooftop glazing
                default:             pick = top;    break;
            }
            if (pick == null) { Debug.LogWarning("[CALIB] no material for " + r.name); continue; }

            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = pick;
            r.sharedMaterials = mats;
            Debug.Log("[CALIB] " + r.name + " -> " + pick.name + " (x" + mats.Length + ")");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[CALIB] scene saved with corrected materials");
    }

    static void Calibrate()
    {
        var sky = RenderSettings.skybox;
        if (sky == null) { Debug.LogError("[CALIB] no skybox"); return; }
        float curRot = sky.HasProperty("_Rotation") ? sky.GetFloat("_Rotation") : 0f;

        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) { Debug.LogError("[CALIB] no directional light"); return; }

        var dirs = new[]
        {
            Quaternion.LookRotation(Vector3.forward, Vector3.up),
            Quaternion.LookRotation(Vector3.back,    Vector3.up),
            Quaternion.LookRotation(Vector3.left,    Vector3.up),
            Quaternion.LookRotation(Vector3.right,   Vector3.up),
            Quaternion.LookRotation(Vector3.up,      Vector3.forward),
            Quaternion.LookRotation(Vector3.down,    Vector3.forward),
        };

        var go = new GameObject("__sunScan") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var d = go.AddComponent<UniversalAdditionalCameraData>();
        d.renderPostProcessing = false;                 // no tonemap -> raw HDR
        d.antialiasing = AntialiasingMode.None;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = 0;
        cam.fieldOfView = 90f;
        cam.aspect = 1f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        cam.allowHDR = true;
        go.transform.position = Vector3.zero;

        float best = -1f;
        Vector3 bestDir = Vector3.zero;
        int bestFace = -1;

        var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        rt.Create();
        var tex = new Texture2D(Res, Res, TextureFormat.RGBAFloat, false, true);

        for (int f = 0; f < dirs.Length; f++)
        {
            go.transform.rotation = dirs[f];
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var px = tex.GetPixels();
            for (int y = 0; y < Res; y++)
            {
                for (int x = 0; x < Res; x++)
                {
                    var c = px[y * Res + x];
                    float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    if (lum <= best) continue;
                    best = lum;
                    bestFace = f;
                    // ReadPixels rows are bottom-up, matching viewport v
                    bestDir = cam.ViewportPointToRay(new Vector3((x + 0.5f) / Res, (y + 0.5f) / Res, 0f)).direction;
                }
            }
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);

        bestDir.Normalize();
        float skyElev = Mathf.Asin(Mathf.Clamp(bestDir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float skyAzim = Mathf.Repeat(Mathf.Atan2(bestDir.x, bestDir.z) * Mathf.Rad2Deg, 360f);

        Vector3 toSun = -sun.transform.forward;
        float lightElev = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lightAzim = Mathf.Repeat(Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg, 360f);

        float delta  = Mathf.Repeat(lightAzim - skyAzim, 360f);
        float newRot = Mathf.Repeat(curRot + delta, 360f);

        Debug.Log(string.Format("[CALIB] peak sky luminance = {0:F1} (face {1}) dir={2}", best, bestFace, bestDir));
        Debug.Log(string.Format("[CALIB] SKY   sun: elevation {0:F1}  azimuth {1:F1}", skyElev, skyAzim));
        Debug.Log(string.Format("[CALIB] LIGHT sun: elevation {0:F1}  azimuth {1:F1}", lightElev, lightAzim));
        Debug.Log(string.Format("[CALIB] >>> current _Rotation {0:F1} -> SET _Rotation = {1:F1}", curRot, newRot));
        Debug.Log(string.Format("[CALIB] >>> elevation gap {0:F1} deg (fix by tilting the light, not rotating the sky)",
            lightElev - skyElev));
    }
}
