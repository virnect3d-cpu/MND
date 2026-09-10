// TEMPORARY setup + verification helper.
//  1. force-imports newly added assets (8K sky cubemap, 63 rooftop FBX + textures)
//  2. fixes texture import settings (normal maps, linear masks, 2K cap)
//  3. builds URP Lit materials and wires them to the rooftop model
//  4. drops the model into the Yeouido63 scene
//  5. renders verification PNGs
// Runs once per domain reload via EditorApplication.update (delayCall races with
// asset import and silently gets dropped). Safe to delete when the look is signed off.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class _TempRenderCheck
{
    const string ScenePath  = "Assets/yeouido63/Scenes/Yeouido63.unity";
    const string CamName    = "CAM_63_Air";
    const string RoofDir    = "Assets/yeouido63/Runtime/Models/63_RoofTop";
    const string RoofFbx    = RoofDir + "/63_BILLD_ALL.fbx";
    const string SkyHdr     = "Assets/yeouido63/Runtime/HDRI/Sky_Yeouido_4K.hdr";
    const string RootName   = "63_RoofTop_ALL";
    const string OutDir     = @"C:\Users\VIRNECT\AppData\Local\Temp";
    const string RanKey     = "_TempRenderCheck.ran";
    const int    TexCap     = 2048;
    const float  SunFov     = 60f;

    static int _frames;

    static _TempRenderCheck()
    {
        if (SessionState.GetBool(RanKey, false)) return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        // let the editor finish whatever import triggered this reload
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { _frames = 0; return; }
        if (++_frames < 30) return;

        EditorApplication.update -= Tick;
        SessionState.SetBool(RanKey, true);
        Run();
    }

    [MenuItem("Tools/_Temp/Run Setup And Capture")]
    public static void Run()
    {
        try
        {
            Debug.Log("[SETUP] start");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            FixTextureImportSettings();
            ImportIfMissing(SkyHdr);
            ImportIfMissing(RoofFbx);

            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath && !scene.isDirty)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            PlaceRooftop();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[SETUP] scene saved");

            CaptureAll();
            Debug.Log("[SETUP] done");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SETUP] failed: " + e);
        }
    }

    static void ImportIfMissing(string path)
    {
        var obj = AssetDatabase.LoadMainAssetAtPath(path);
        if (obj == null)
        {
            Debug.Log("[SETUP] force importing " + path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            obj = AssetDatabase.LoadMainAssetAtPath(path);
        }
        Debug.Log("[SETUP] " + path + " -> " + (obj == null ? "STILL NULL" : obj.GetType().Name + " '" + obj.name + "'"));
    }

    static void FixTextureImportSettings()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RoofDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;

            bool isNormal = path.Contains("_Normal");
            bool isLinear = path.Contains("_Metallic") || path.Contains("_Roughness")
                         || path.Contains("_AO") || path.Contains("_Height");

            bool changed = false;
            var wantType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != wantType) { ti.textureType = wantType; changed = true; }

            bool wantSrgb = !(isNormal || isLinear);
            if (ti.sRGBTexture != wantSrgb) { ti.sRGBTexture = wantSrgb; changed = true; }

            if (ti.maxTextureSize > TexCap) { ti.maxTextureSize = TexCap; changed = true; }

            if (changed)
            {
                ti.SaveAndReimport();
                Debug.Log("[SETUP] texture fixed: " + Path.GetFileName(path)
                          + " type=" + wantType + " sRGB=" + wantSrgb + " cap=" + TexCap);
            }
        }
    }

    static Texture2D Tex(string file)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(RoofDir + "/" + file + ".png");
    }

    static Material BuildMat(string name, string baseColor, string normal, string mask, string ao)
    {
        string path = RoofDir + "/M_" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Debug.LogError("[SETUP] URP/Lit shader not found"); return null; }
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
        }

        var bc = baseColor != null ? Tex(baseColor) : null;
        var nm = normal    != null ? Tex(normal)    : null;
        var ms = mask      != null ? Tex(mask)      : null;
        var oc = ao        != null ? Tex(ao)        : null;

        if (bc != null) mat.SetTexture("_BaseMap", bc);
        if (nm != null) { mat.SetTexture("_BumpMap", nm); mat.EnableKeyword("_NORMALMAP"); }
        if (ms != null)
        {
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);   // metallic map alpha
        }
        if (oc != null) { mat.SetTexture("_OcclusionMap", oc); mat.EnableKeyword("_OCCLUSIONMAP"); }

        EditorUtility.SetDirty(mat);
        Debug.Log(string.Format("[SETUP] material {0}: base={1} normal={2} mask={3} ao={4}",
            name, bc != null, nm != null, ms != null, oc != null));
        return mat;
    }

    static void PlaceRooftop()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(RoofFbx);
        if (fbx == null) { Debug.LogError("[SETUP] FBX not loaded: " + RoofFbx); return; }

        var mTop   = BuildMat("RoofTop",   "Roof_Top_blinn2_BaseColor",   "Roof_Top_blinn2_Normal",   "Roof_Top_blinn2_MetallicSmoothness",   "Roof_Top_blinn2_AO");
        var mTower = BuildMat("RoofTower", "Roof_Tower_blinn4_BaseColor", "Roof_Tower_blinn4_Normal", "Roof_Tower_blinn4_MetallicSmoothness", "Roof_Tower_blinn4_AO");
        var mWall  = BuildMat("RoofWall",  "WALL", null, null, null);
        AssetDatabase.SaveAssets();

        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Debug.Log("[SETUP] '" + RootName + "' already in scene, reusing");
            Object.DestroyImmediate(existing);
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        inst.name = RootName;
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;

        // report what the FBX actually contains so materials can be matched properly
        var rends = inst.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds();
        bool first = true;
        var slots = new List<string>();
        foreach (var r in rends)
        {
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            foreach (var m in r.sharedMaterials)
                if (m != null && !slots.Contains(m.name)) slots.Add(m.name);
        }
        Debug.Log(string.Format("[SETUP] rooftop: {0} renderers, bounds center={1} size={2}",
            rends.Length, bounds.center, bounds.size));
        Debug.Log("[SETUP] FBX material slots: " + string.Join(", ", slots));

        // assign by best-guess name match; unmatched slots are reported, not silently left
        foreach (var r in rends)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var n = mats[i] != null ? mats[i].name.ToLower() : "";
                if (n.Contains("tower") || n.Contains("blinn4")) mats[i] = mTower ?? mats[i];
                else if (n.Contains("wall")) mats[i] = mWall ?? mats[i];
                else mats[i] = mTop ?? mats[i];
            }
            r.sharedMaterials = mats;
        }
        Debug.Log("[SETUP] materials assigned to " + rends.Length + " renderers");
    }

    // ---------- capture ----------

    // Capture without re-running setup: re-instantiating the FBX would wipe the
    // per-mesh material assignments done by _TempSunCalib.
    [MenuItem("Tools/_Temp/Capture Only")]
    public static void CaptureOnly()
    {
        try { CaptureAll(); }
        catch (System.Exception e) { Debug.LogError("[SHOT] capture failed: " + e); }
    }

    static void CaptureAll()
    {
        var camGo = GameObject.Find(CamName);
        var mainCam = camGo != null ? camGo.GetComponent<Camera>() : null;
        if (mainCam == null) { Debug.LogError("[SHOT] " + CamName + " not found"); return; }

        Capture(mainCam, 1920, 1080, Path.Combine(OutDir, "shot_main.png"));

        Renderer water = null;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && m.name.Contains("M_SEA")) { water = r; break; }
            if (water != null) break;
        }
        if (water != null)
        {
            var b = water.bounds;
            Debug.Log("[SHOT] water bounds center=" + b.center + " size=" + b.size);
            var go = new GameObject("__waterCam") { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>();
            CopySettings(mainCam, c);
            float span = Mathf.Max(b.size.x, b.size.z);
            go.transform.position = b.center + new Vector3(-span * 0.4f, span * 0.3f, -span * 0.4f);
            go.transform.LookAt(b.center);
            c.farClipPlane = Mathf.Max(mainCam.farClipPlane, span * 4f);
            Capture(c, 1920, 1080, Path.Combine(OutDir, "shot_water.png"));
            Object.DestroyImmediate(go);
        }
        else Debug.LogWarning("[SHOT] no M_SEA renderer found");

        // close-up of the 63 tower: at city-wide framing it is only ~60px across,
        // which is far too small to judge the facade material on
        var tower = GameObject.Find("63_RoofTop_ALL");
        if (tower != null)
        {
            var tb = new Bounds();
            bool f0 = true;
            foreach (var rr in tower.GetComponentsInChildren<Renderer>())
            {
                if (f0) { tb = rr.bounds; f0 = false; } else tb.Encapsulate(rr.bounds);
            }
            var go = new GameObject("__towerCam") { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>();
            CopySettings(mainCam, c);
            float d = tb.size.magnitude * 1.1f;
            go.transform.position = tb.center + new Vector3(-d * 0.55f, d * 0.30f, -d * 0.55f);
            go.transform.LookAt(tb.center);
            c.fieldOfView = 45f;
            c.nearClipPlane = 1f;
            c.farClipPlane = Mathf.Max(mainCam.farClipPlane, d * 6f);
            Capture(c, 1440, 1440, Path.Combine(OutDir, "shot_tower.png"));
            Object.DestroyImmediate(go);
            Debug.Log("[SHOT] tower bounds center=" + tb.center + " size=" + tb.size);
        }

        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null)
        {
            Vector3 toSun = -sun.transform.forward;
            float rot = RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Rotation")
                ? RenderSettings.skybox.GetFloat("_Rotation") : -1f;
            Debug.Log(string.Format("[SHOT] toSun={0} elev={1:F1} azim={2:F1} skyboxRot={3:F1} skybox={4}",
                toSun,
                Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg,
                Mathf.Repeat(Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg, 360f),
                rot,
                RenderSettings.skybox != null ? RenderSettings.skybox.name : "NULL"));

            var go = new GameObject("__sunCam") { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>();
            var d = go.AddComponent<UniversalAdditionalCameraData>();
            d.renderPostProcessing = false;
            d.antialiasing = AntialiasingMode.None;
            c.clearFlags = CameraClearFlags.Skybox;
            c.cullingMask = 0;
            c.fieldOfView = SunFov;
            c.nearClipPlane = 0.1f;
            c.farClipPlane = 1000f;
            go.transform.position = mainCam.transform.position;
            go.transform.rotation = Quaternion.LookRotation(toSun, Vector3.up);
            Capture(c, 1024, 1024, Path.Combine(OutDir, "shot_sunaim.png"));
            Object.DestroyImmediate(go);
            Debug.Log("[SHOT] sun-aim: " + SunFov + "deg / 1024px = " + (SunFov / 1024f).ToString("F4") + " deg per px");
        }
    }

    static void CopySettings(Camera src, Camera dst)
    {
        dst.clearFlags      = src.clearFlags;
        dst.backgroundColor = src.backgroundColor;
        dst.cullingMask     = src.cullingMask;
        dst.fieldOfView     = src.fieldOfView;
        dst.nearClipPlane   = src.nearClipPlane;
        dst.farClipPlane    = src.farClipPlane;

        var s = src.GetComponent<UniversalAdditionalCameraData>();
        var t = dst.gameObject.AddComponent<UniversalAdditionalCameraData>();
        if (s != null)
        {
            t.renderPostProcessing = s.renderPostProcessing;
            t.antialiasing         = s.antialiasing;
            t.antialiasingQuality  = s.antialiasingQuality;
            t.renderShadows        = s.renderShadows;
        }
    }

    static void Capture(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.Create();
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        Debug.Log("[SHOT] wrote " + path);
    }
}
