using System.Drawing;

namespace AlctClient.Core;

// OCR 엔진 구현체(RapidOcrNet)를 이 인터페이스 뒤에 둔다.
// 입력은 마스킹까지 끝난 비트맵
// 출력은 후처리(OcrLineReconstructor)에 넘길 박스+텍스트 조각들.
internal interface IOcrEngine : IDisposable
{
    IReadOnlyList<OcrLineReconstructor.Fragment> Recognize(Bitmap bitmap);
}
