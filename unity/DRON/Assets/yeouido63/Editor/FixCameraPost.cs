// 활성 카메라에 포스트프로세싱을 켠다
//
// 메뉴: Tools/Yeouido 63/카메라 포스트 켜기
//
// 왜 필요한가
//   씬에 카메라가 둘이다. main_came(활성)과 CAM_63_Air(비활성).
//   포스트프로세싱이 켜져 있는 쪽은 비활성인 CAM_63_Air 였다.
//
//   그래서 Yeouido63_Post.asset 의 오버라이드 전체 — 톤매핑, 컬러그레이딩,
//   블룸, 비네트, 그리고 볼류메트릭 구름 — 가 화면에 하나도 안 나왔다.
//   구름 속도를 몇 번이나 낮췄는데 그게 전부 안 보이는 구름을 만진 것이다.
//   볼륨 쪽은 멀쩡했다. 카메라 체크박스 하나가 전부 삼키고 있었다.
//
//   안개는 이것과 무관하게 나온다 — RendererFeature 라 카메라 토글을
//   안 탄다. 그래서 "안개는 보이는데 구름은 없다" 가 단서였어야 했다.
//
// Camera.main 이 null 인 문제도 같이 고친다.
//   MainCamera 태그가 비활성 카메라에 붙어 있어서 Camera.main 이 null 이다.
//   지금은 아무도 안 쓰지만 런타임 스크립트가 하나라도 쓰면 NRE 다.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class FixCameraPost
{
    [MenuItem("Tools/Yeouido 63/카메라 포스트 켜기")]
    public static void Run()
    {
        var cams = Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cams.Length == 0) { Debug.LogError("[카메라] 씬에 카메라가 없다"); return; }

        int fixedCount = 0;
        foreach (var cam in cams)
        {
            bool active = cam.gameObject.activeInHierarchy;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) continue;

            // 활성 카메라만 켠다. 비활성 쪽은 건드리지 않는다 —
            // 나중에 항공 시점으로 돌아갈 때 설정이 남아 있어야 한다.
            if (active && !data.renderPostProcessing)
            {
                Undo.RecordObject(data, "카메라 포스트 켜기");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.dithering = true;
                EditorUtility.SetDirty(data);
                fixedCount++;
                Debug.Log($"[카메라] {cam.name}: 포스트프로세싱 켬 (SMAA + 디더링)");
            }

            // MainCamera 태그는 활성 쪽에 있어야 Camera.main 이 잡힌다.
            if (active && cam.tag != "MainCamera")
            {
                Undo.RecordObject(cam.gameObject, "MainCamera 태그");
                cam.gameObject.tag = "MainCamera";
                EditorUtility.SetDirty(cam.gameObject);
                Debug.Log($"[카메라] {cam.name}: MainCamera 태그 부여");
                fixedCount++;
            }
            else if (!active && cam.CompareTag("MainCamera"))
            {
                Undo.RecordObject(cam.gameObject, "MainCamera 태그 해제");
                cam.gameObject.tag = "Untagged";
                EditorUtility.SetDirty(cam.gameObject);
                Debug.Log($"[카메라] {cam.name}: 비활성이라 MainCamera 태그 뗌");
                fixedCount++;
            }
        }

        if (fixedCount == 0) { Debug.Log("[카메라] 이미 정상이다"); return; }

        // 플레이 중이면 SceneSaver 가 경고만 남기고 건너뛴다.
        // 값은 이미 메모리에 들어갔으니 화면으로는 확인된다.
        SceneSaver.Save(EditorSceneManager.GetActiveScene(), "카메라");
    }
}
