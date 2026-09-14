// 플래그로 도는 작업 묶음
//
//   에디터 바깥에서 `Temp/yeouido63_*.flag` 를 만들고 에디터에 포커스를
//   주면 리컴파일 직후 한 번 돈다. 배선은 AutoRunFlag 가 담당한다.
//   "컴포넌트 값만 보고 됐다고 하지 말고 픽셀로 확인" 을 강제하는 장치라
//   대부분이 마지막에 캡처로 끝난다.
//
// 예전에 여기 있던 것들
//   구름이 안테나 탑 위로 새던 버그를 쫓느라 진단 훅이 6 개 더 있었다
//   (msaa / leak / split / score / step / edge). 해상도·업스케일·시간누적·
//   MSAA 가 원인인지 하나씩 물어보는 것들이었는데 답이 전부 "아니오" 로
//   나왔고, 진짜 원인은 깊이를 한 점만 읽는 것이었다.
//
//   결론은 THIRD_PARTY.md 의 표에 남겼고 셰이더는 패치됐다. 기각된 가설을
//   다시 시험할 이유가 없어서 훅과 스크립트를 같이 지웠다. 회귀 감시는
//   order(누수율) 하나로 충분하다 — 그게 4.46% -> 0.32% 를 낸 계측기다.

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class RunFixAndCapture
{
    // 구름이 탑을 제대로 가리는지 누수율로 잰다. 2% 아래면 정상이다.
    [DidReloadScripts]
    static void OnOrder()
        => AutoRunFlag.Consume("order", "순서", ProbeCloudOrder.Run);

    // 볼류메트릭 구름이 실제로 화면에 뭘 하는지 본다 (off/on 픽셀 비교).
    [DidReloadScripts]
    static void OnProbe()
        => AutoRunFlag.Consume("probe", "구름", ProbeClouds.Run);

    // 주스텝을 훑어 가장자리가 언제부터 깨끗해지는지 본다.
    [DidReloadScripts]
    static void OnStep()
        => AutoRunFlag.Consume("step", "스텝", ProbeCloudSteps.Run);

    // 대기 튜닝 + 먼지 재배치 + 캡처를 한 번에.
    //   먼지는 값이 파티클 시스템에 구워져 있어서 다시 심어야 반영된다.
    [DidReloadScripts]
    static void OnTune()
        => AutoRunFlag.Consume("tune", "대기", () =>
        {
            TuneAtmosphere.Run();
            SetupDustParticles.Setup();

            // 먼지가 제대로 심겼는지 확인하고 넘어간다. 예전에 도메인
            // 리로드와 겹쳐서 삭제만 저장되고 재생성이 날아간 적이 있다.
            var root = GameObject.Find("DUST_Particles");
            int layers = root == null
                ? 0 : root.GetComponentsInChildren<ParticleSystem>(true).Length;
            if (layers < 3)
            {
                Debug.LogError($"[대기] 먼지가 제대로 안 심겼다 (층 {layers}/3). 캡처 취소.");
                return;
            }

            EditorApplication.delayCall += CaptureGameView.Capture;
        }, exitPlay: true);

    // 활성 카메라의 포스트프로세싱을 켜고 찍는다.
    [DidReloadScripts]
    static void OnFixCam()
        => AutoRunFlag.Consume("fixcam", "카메라", () =>
        {
            FixCameraPost.Run();
            // 포스트 파이프라인이 한 프레임 뒤에 붙으므로 캡처를 미룬다.
            EditorApplication.delayCall += CaptureGameView.Capture;
        }, exitPlay: true);
}
