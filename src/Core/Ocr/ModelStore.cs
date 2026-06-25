using System.IO;
using System.Reflection;

namespace AlctClient.Core;

// exe에 임베드된 OCR 모델(EmbeddedResource)을 %APPDATA%\ALCT\models 로 1회 추출한다(앱 버전 변경 시 갱신).
// RapidOcrNet은 모델을 디스크 경로로 받으므로, 단일 파일(PublishSingleFile) 배포에서도 추출이 필요하다.
// 버전기반 갱신 방식은 AssetCache(Utils)와 동일한 패턴.
internal sealed record ModelPaths(string Det, string Cls, string Rec, string Keys);

internal static class ModelStore
{
    private static readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ALCT", "models");
    private static readonly string _versionFile = Path.Combine(_dir, "version.txt");

    private static readonly string _currentVersion =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}"
            : "unknown";

    // (임베드 리소스명[csproj LogicalName], 추출 파일명)
    private static readonly (string Resource, string File)[] _models =
    {
        ("AlctClient.models.det.onnx",  "det.onnx"),
        ("AlctClient.models.cls.onnx",  "cls.onnx"),
        ("AlctClient.models.rec.onnx",  "rec.onnx"),
        ("AlctClient.models.dict.txt",  "dict.txt"),
    };

    public static ModelPaths EnsureExtracted()
    {
        var versionChanged = !File.Exists(_versionFile)
                             || File.ReadAllText(_versionFile).Trim() != _currentVersion;
        Directory.CreateDirectory(_dir);

        foreach (var (resource, file) in _models)
        {
            var path = Path.Combine(_dir, file);
            if (versionChanged || !File.Exists(path))
                ExtractResource(resource, path);
        }
        File.WriteAllText(_versionFile, _currentVersion);

        return new ModelPaths(
            Path.Combine(_dir, "det.onnx"),
            Path.Combine(_dir, "cls.onnx"),
            Path.Combine(_dir, "rec.onnx"),
            Path.Combine(_dir, "dict.txt"));
    }

    private static void ExtractResource(string resourceName, string destPath)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded model resource missing: {resourceName}");
        using var file = File.Create(destPath);
        stream.CopyTo(file);
    }
}
