// 구름 레이어 핸들을 씬에 심는다
//
// 메뉴: Tools/Yeouido 63/구름 레이어 오브젝트 만들기
//
// 구름은 Volume 프로파일 오버라이드라 하이어라키에 아무것도 없다.
// 이 스크립트가 CLOUD_Layer 오브젝트를 만들어 프로파일에 연결하고,
// 프로파일의 현재 값을 그대로 끌어와 초기 상태를 맞춘다.
// (임의의 기본값으로 덮어쓰면 지금 보이는 하늘이 바뀌어 버린다.)

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class SetupCloudHandle
{
    const string Flag     = "Temp/yeouido63_cloudhandle.flag";
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string ObjName  = "CLOUD_Layer";

    // 구름 덩어리가 흘러가는 속도(m/s).
    //
    //   처음에 12 로 뒀다. "구름은 멀리 있으니 빨라도 된다" 는 계산이었는데
    //   반대였다 — shapeOffset 은 카메라 거리와 무관하게 무늬를 미는 값이라
    //   원근 감쇠가 없다. 12 면 하늘 전체가 눈에 띄게 쓸려 간다.
    //
    //   2.5 -> 0.8 -> 0.4. 10 분에 240 m 라 흐른다기보다 아주 천천히
    //   밀려나는 정도다.
    const float DriftSpeed  = 0.4f;

    // 제자리에서 굴러가는 속도(배율).
    //
    //   여기가 진짜 범인이었다. 드리프트만 낮추고 이걸 그대로 뒀더니
    //   구름이 여전히 빨라 보였다. globalSpeed 는 아래 두 배율을 곱하는데
    //   둘 다 1.0(상한)으로 올려 둔 상태라 실효 속도가 그대로였다.
    //
    //   globalSpeed 7.5 -> 2.5 -> 0.6 -> 0.3 으로 내렸다.
    //   셋이 곱해지므로 하나만 만지면 체감이 잘 안 바뀐다.
    //
    //   한 번 더 절반으로 줄일 때는 globalSpeed 만 건드린다. 곱이라
    //   실효가 정확히 반이 되고, 형상과 침식의 비율(0.35:0.25)도
    //   그대로 유지돼 구름 결이 안 변한다.
    //     형상 실효 0.21 -> 0.105
    //     침식 실효 0.15 -> 0.075
    const float GlobalSpeed   = 0.3f;
    const float ShapeSpeed    = 0.35f;   // 형상이 뭉개지는 속도
    const float ErosionSpeed  = 0.25f;   // 가장자리가 헐리는 속도

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[구름] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/구름 레이어 오브젝트 만들기")]
    public static void Run()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null)
        {
            Debug.LogError("[구름] 포스트 프로파일을 못 찾았다: " + PostPath);
            return;
        }

        var go = GameObject.Find(ObjName);
        if (go == null)
        {
            go = new GameObject(ObjName);
            Undo.RegisterCreatedObjectUndo(go, "Create Cloud Layer");
        }

        var h = go.GetComponent<CloudLayerHandle>();
        if (h == null) h = Undo.AddComponent<CloudLayerHandle>(go);

        h.profile = post;

        // 프로파일이 원본이다. 현재 값을 먼저 읽어 와 위치/두께/속도를
        // 맞춘 뒤에야 다시 쓴다. 순서를 뒤집으면 컴포넌트 기본값이
        // 프로파일을 덮어써서 하늘이 통째로 바뀐다.
        h.Pull();

        // 수평 기준점은 0 으로 되돌린다.
        //
        //   Pull 은 프로파일 오프셋에서 드리프트를 빼 기준점을 구하는데,
        //   스크립트 리로드로 _drift 가 0 이 된 직후에는 뺄 값이 없다.
        //   그러면 누적된 이동량이 통째로 기준점으로 굳어서, 셋업을
        //   돌릴 때마다 구름이 수천 m 씩 밀려난다 (실제로 -2543 이 나왔다).
        //
        //   수평 기준점은 어차피 의미 있는 값이 아니다. 같은 노이즈를
        //   어디서 잘라 보느냐일 뿐이라 0 이 기준으로 적당하다.
        //   고도(y)는 진짜 의미가 있으니 Pull 이 읽은 값을 그대로 둔다.
        var pos = h.transform.position;
        h.transform.position = new Vector3(0f, pos.y, 0f);

        // 회전 연동은 꺼 둔다. 방위는 SyncWind 가 바람 각도에서 계산해
        // 쓰고 있어서, 둘 다 켜면 마지막에 쓴 쪽이 이긴다.
        h.driveOrientation = false;

        // 드리프트 — 구름 덩어리를 X 축으로 흘려보낸다.
        //
        //   globalSpeed 를 아무리 올려도 구름은 제자리에서 뭉개지기만 한다.
        //   판 전체를 미는 건 shapeOffset 뿐이라 거기에 속도를 준다.
        h.driftSpeed = DriftSpeed;
        h.driftDirection = new Vector2(1f, 0f);   // +X
        h.driftInEditMode = true;

        // 굴러가는 속도. 세 값이 곱해지므로 같이 낮춰야 체감이 바뀐다.
        h.speed        = GlobalSpeed;
        h.shapeSpeed   = ShapeSpeed;
        h.erosionSpeed = ErosionSpeed;

        h.Apply();

        EditorUtility.SetDirty(h);
        EditorUtility.SetDirty(post);
        AssetDatabase.SaveAssets();

        var scene = go.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var p = go.transform.position;
        Debug.Log($"[구름] '{ObjName}' 준비됐다. " +
                  $"고도 {p.y:F0}~{p.y + h.thickness:F0} m, " +
                  $"기준 오프셋 ({p.x:F0},{p.z:F0}), 짙기 {h.density:F2}. " +
                  $"드리프트 {h.driftSpeed:F1} m/s -> " +
                  $"({h.driftDirection.x:F0},{h.driftDirection.y:F0}). " +
                  $"굴러가기 {h.speed:F2} x 형상 {h.shapeSpeed:F2}/침식 {h.erosionSpeed:F2} " +
                  $"= 실효 {h.speed * h.shapeSpeed:F2}/{h.speed * h.erosionSpeed:F2}.");
    }
}
