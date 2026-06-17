namespace AlctClient.Core;

internal static class BuildConstants
{
    internal const string SERVER_URL   = "#{ALCT_SERVER_URL}#";
    internal const string SERVER_TOKEN = "#{ALCT_SERVER_TOKEN}#";
    internal const string ASSETS_BASE_URL = "https://raw.githubusercontent.com/shu-rimp/alct/#{ALCT_VERSION_TAG}#/src/assets";
}
