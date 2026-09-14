// 구름 가장자리 깨짐을 숫자로 잰다
//
// 메뉴: Tools/Yeouido 63/구름 깨짐 점수
//
// 왜 필요한가
//   "비슷해 보인다" 로는 Bilateral 과 전체 해상도 중 뭘 고를지 못 정한다.
//   눈은 큰 덩어리 차이에 끌려서 1~2 픽셀짜리 지글거림을 놓친다.
//
// 무엇을 재나
//   깨짐은 "이웃 픽셀과 튀는 값" 으로 나타난다. 매끄러운 구름 가장자리는
//   값이 완만하게 변하고, 부서진 가장자리는 한 픽셀 건너 확 튄다.
//   그래서 라플라시안(주변 4 픽셀 평균과의 차이)의 크기를 센다.
//
//   처음엔 하늘 전체를 평균 냈는데 그게 틀렸다. 화면 대부분이 매끄러운
//   하늘이라 그게 평균을 눌러 버리고, 해상도를 올려서 생긴 "정상적인
//   구름 디테일" 까지 깨짐으로 세서 점수가 거꾸로 나왔다.
//
//   깨짐은 탑 주변에서만 난다. 그래서 탑 실루엣에서 일정 거리 안쪽
//   띠(band)만 잰다. 탑 픽셀 자체는 빼고 — 격자라 원래 고주파다.
//
//   점수가 낮을수록 매끄럽다. 기준(base)과 비교해서 얼마나 줄었는지 본다.

using System.IO;
using UnityEditor;
using UnityEngine;

public static class ScoreCloudFix
{
    const string Dir = "Temp/Captures";

    [MenuItem("Tools/Yeouido 63/구름 깨짐 점수")]
    public static void Run()
    {
        string[] names = { "fix_base", "fix_fullres", "fix_bilateral", "fix_notemp", "fix_both" };

        float baseScore = -1f;
        foreach (var n in names)
        {
            string path = Path.Combine(Dir, n + ".png");
            if (!File.Exists(path)) { Debug.LogWarning($"[점수] 없음: {n}.png"); continue; }

            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            float s = Score(tex, out int counted);
            Object.DestroyImmediate(tex);

            if (baseScore < 0f) baseScore = s;
            float delta = baseScore > 0f ? (s - baseScore) / baseScore * 100f : 0f;

            Debug.Log($"[점수] {n,-14} 깨짐 {s:F3}  " +
                      $"(하늘 픽셀 {counted:N0})  " +
                      (n == "fix_base" ? "기준" : $"{delta:+0.0;-0.0}%"));
        }

        Debug.Log("[점수] 낮을수록 매끄럽다. 기준 대비 마이너스면 개선된 것이다.");
    }

    // 탑에서 이 픽셀 수 안쪽만 잰다. 절반 해상도 업스케일이 번지는
    // 범위가 대략 이 정도다.
    const int Band = 24;

    static float Score(Texture2D tex, out int counted)
    {
        int w = tex.width, h = tex.height;
        var px = tex.GetPixels32();

        // 1) 탑 픽셀을 찾는다. 빨강이 파랑보다 세거나(빨간 격자),
        //    충분히 어두운(회색 구조물) 픽셀.
        var isTower = new bool[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            isTower[i] = (c.r > c.b + 20) || (c.r < 110 && c.g < 110 && c.b < 130);
        }

        // 2) 탑에서 Band 안에 있는 하늘 픽셀만 센다.
        //    행 단위로 탑의 좌우 끝을 찾아 그 바깥 띠를 잡는다.
        double sum = 0; counted = 0;
        for (int y = 1; y < h - 1; y++)
        {
            int left = -1, right = -1;
            for (int x = 0; x < w; x++)
                if (isTower[y * w + x]) { if (left < 0) left = x; right = x; }
            if (left < 0) continue;   // 이 행엔 탑이 없다

            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                if (isTower[i]) continue;           // 탑 자체는 뺀다

                // 탑 실루엣에서 Band 안쪽인가
                int dist = x < left ? left - x : (x > right ? x - right : 0);
                if (dist == 0 || dist > Band) continue;

                var c = px[i];
                if (c.b < 120) continue;            // 하늘/구름만

                var l = px[i - 1]; var r = px[i + 1];
                var u = px[i - w]; var d = px[i + w];
                sum += Mathf.Abs(4f * c.g - l.g - r.g - u.g - d.g);
                counted++;
            }
        }

        return counted > 0 ? (float)(sum / counted) : 0f;
    }
}
