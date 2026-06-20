namespace AlctClient.Core;

// 이 값들의 비밀성에 보안을 의존하지 않는다 — 실제 접근 통제는 서버에서 별도로 처리한다.
internal static class BuildConstants
{
    internal const string SERVER_URL   = "#{ALCT_SERVER_URL}#";
    internal const string SERVER_TOKEN = "#{ALCT_SERVER_TOKEN}#";
    
    // 로고, 온보딩 데모 영상 등 asset을 받아오는 베이스 URL.
    // %APPDATA%/ALCT/assets 디렉토리 내 파일이 있으면 정상 동작함 — 해당 URL은 .exe파일 크기 절감을 위해 사용.
    internal const string ASSETS_BASE_URL = "https://raw.githubusercontent.com/shu-rimp/alct/#{ALCT_VERSION_TAG}#/src/assets";
}
