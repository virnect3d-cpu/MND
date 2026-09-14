// 구름이 움직일 때 보이는 아지랑이(어른거림)를 없앤다
//
// 메뉴: Tools/Yeouido 63/아지랑이 제거 (디더링 끄기)
//
// 무엇이 원인이었나
//   URP 카메라의 디더링이다. TAA 도, 시간누적도, 구름 셰이더도 아니었다.
//
//   디더링은 색 밴딩(계단 무늬)을 감추려고 출력 직전에 픽셀마다 미세한
//   노이즈를 얹는다. 그런데 그 노이즈 패턴이 프레임마다 바뀐다. 하늘이나
//   구름처럼 완만한 그라데이션 위에서는 그 변화가 어른거림으로 보인다.
//   구름 가장자리가 화면에서 제일 완만한 그라데이션이라 거기서 제일
//   도드라진다 — "구름 움직일 때 아지랑이" 의 정체가 이것이다.
//
// 어떻게 알아냈나 (ProbeShimmerTime 측정)
//   구름도 카메라도 안 움직이는 정지 화면에서 연속 두 장을 찍어
//   픽셀 변화를 셌다. 아무것도 안 바꿨는데 72% 픽셀이 매 프레임 달랐다.
//   거기서 하나씩 껐다.
//
//     기준              평균차 1.83 , 바뀐 픽셀 72.4%
//     구름 OFF          평균차 1.80 , 71.4%   <- 구름 무관
//     먼지 OFF          평균차 1.72 , 70.7%   <- 먼지 무관
//     TAA OFF           평균차 1.63 , 70.2%   <- TAA 무관
//     SMAA 로 교체      평균차 1.80 , 71.5%   <- 안티앨리어싱 무관
//     디더링 OFF        평균차 0.45 ,  5.3%   <- 범인
//     포스트 전체 OFF   평균차 0.31 ,  3.0%   <- 디더링만 꺼도 거의 여기까지 온다
//     빈 카메라         평균차 0.02 ,  0.9%   <- 측정 하네스는 정상
//
//   시간누적(temporalAccumulationFactor)도 0/0.5/1 로 돌려 봤지만
//   전부 70% 대로 동일했다. 재투영은 아지랑이와 무관하다.
//
// 맞바꾸는 것
//   디더링을 끄면 아주 어두운 그라데이션에서 색 밴딩이 보일 수 있다.
//   이 씬은 낮 하늘이라 밴딩이 잘 안 생기는 조건이고, 어른거림 쪽이
//   훨씬 거슬린다고 보고 택했다. 밴딩이 눈에 띄면 디더링을 되켜는 대신
//   출력 포맷을 올리는 쪽이 맞다.

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class FixShimmerDithering
{
    [DidReloadScripts]
    static void OnReload()
        => AutoRunFlag.Consume("shimfix", "아지랑이", FixShimmerDithering.Run, exitPlay: true);

    [MenuItem("Tools/Yeouido 63/아지랑이 제거 (디더링 끄기)")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        int changed = 0, total = 0;

        // 비활성 카메라도 포함한다. 씬에 카메라가 두 대 있고 둘 다
        // 디더링이 켜져 있었다. 한 대만 고치면 그 카메라로 볼 때 재발한다.
        foreach (var cam in Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var extra = cam.GetUniversalAdditionalCameraData();
            if (extra == null) continue;
            total++;
            if (!extra.dithering) continue;

            Undo.RecordObject(extra, "아지랑이 제거: 디더링 끄기");
            extra.dithering = false;
            EditorUtility.SetDirty(extra);
            changed++;
            Debug.Log($"[아지랑이] {cam.name}: 디더링 끔");
        }

        Debug.Log($"[아지랑이] 카메라 {total} 대 중 {changed} 대 수정");

        if (changed > 0)
            SceneSaver.Save(scene, Yeouido63Paths.Scene, "아지랑이");
        else
            Debug.Log("[아지랑이] 이미 전부 꺼져 있다. 저장 안 함.");
    }
}
