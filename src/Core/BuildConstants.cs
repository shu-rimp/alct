namespace AlctClient.Core;

internal static class BuildConstants
{
    // 용어집, 버전 정보를 받는 정적 호스팅 베이스.(기존 서버 -> github pages로 전환)
    internal const string SERVER_URL = "#{ALCT_SERVER_URL}#";

    // 로고, 온보딩 데모 영상 등 asset을 받아오는 베이스 URL.
    // %APPDATA%/ALCT/assets 디렉토리 내 파일이 있으면 정상 동작함 — 해당 URL은 .exe파일 크기 절감을 위해 사용.
    internal const string ASSETS_BASE_URL = "https://raw.githubusercontent.com/shu-rimp/alct/#{ALCT_VERSION_TAG}#/src/assets";
}
