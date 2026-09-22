using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NewProjectGenerator
{
    public sealed class AppSettings
    {
        public string DocumentRoot { get; set; }
        public string SourceRoot { get; set; }

        public static AppSettings Load(string baseDirectory)
        {
            AppSettings settings = new AppSettings();
            settings.DocumentRoot = @"D:\document";
            settings.SourceRoot = @"D:\source";

            string path = Path.Combine(baseDirectory, "settings.json");
            if (!File.Exists(path))
            {
                return settings;
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> values =
                serializer.Deserialize<Dictionary<string, object>>(json);

            object value;
            if (values.TryGetValue("documentRoot", out value) && value != null)
            {
                settings.DocumentRoot = value.ToString();
            }
            if (values.TryGetValue("sourceRoot", out value) && value != null)
            {
                settings.SourceRoot = value.ToString();
            }

            return settings;
        }
    }

    public sealed class GenerationRequest
    {
        public string ProjectName { get; set; }
        public int StartYear { get; set; }
        public string DocumentRoot { get; set; }
        public string SourceRoot { get; set; }
        public bool CreateSourceFolder { get; set; }
        public string SourceFolderName { get; set; }
        public bool CreateArchiveFolder { get; set; }
        public List<string> DocumentFolders { get; set; }

        public GenerationRequest()
        {
            DocumentFolders = new List<string>();
        }
    }

    public sealed class GenerationResult
    {
        public string OperationId { get; set; }
        public string DocumentPath { get; set; }
        public string SourcePath { get; set; }
        public List<string> CreatedPaths { get; private set; }
        public List<string> CleanupWarnings { get; private set; }

        public GenerationResult()
        {
            CreatedPaths = new List<string>();
            CleanupWarnings = new List<string>();
        }
    }

    public sealed class FolderOpenResult
    {
        public bool Succeeded { get; private set; }
        public string ErrorMessage { get; private set; }

        internal static FolderOpenResult Success()
        {
            return new FolderOpenResult
            {
                Succeeded = true,
                ErrorMessage = string.Empty
            };
        }

        internal static FolderOpenResult Failure(string errorMessage)
        {
            return new FolderOpenResult
            {
                Succeeded = false,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }
    }

    public static class ProjectFolderOpener
    {
        public static FolderOpenResult TryOpen(
            string path,
            Action<string> startProcess)
        {
            if (startProcess == null)
            {
                throw new ArgumentNullException("startProcess");
            }

            try
            {
                startProcess(path);
                return FolderOpenResult.Success();
            }
            catch (Exception ex)
            {
                return FolderOpenResult.Failure(ex.Message);
            }
        }
    }

    internal sealed class GenerationTestHooks
    {
        public Action<string> BeforeFileWrite { get; set; }
        public Action<string, string, string> BeforeCommit { get; set; }
        public Action<string, string, string> AfterCommit { get; set; }
    }

    internal sealed class GenerationTarget
    {
        public string Role { get; set; }
        public string RootPath { get; set; }
        public string FinalPath { get; set; }
        public string StagingPath { get; set; }
        public string MarkerRelativePath { get; set; }
        public bool StagingDirectoryCreated { get; set; }
        public bool Committed { get; set; }
        public HashSet<string> ExpectedDirectories { get; private set; }
        public Dictionary<string, string> ExpectedFileHashes { get; private set; }
        public List<string> ResultRelativePaths { get; private set; }

        public GenerationTarget()
        {
            ExpectedDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            ExpectedFileHashes = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            ResultRelativePaths = new List<string>();
        }

        public string CurrentOwnedPath
        {
            get { return Committed ? FinalPath : StagingPath; }
        }
    }

    public static class ProjectGenerator
    {
        private const string OwnershipMarkerName =
            ".new-project-generator-operation";

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("경로가 비어 있습니다.", "value");
            }

            string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            string fullPath = Path.GetFullPath(expanded);
            string root = Path.GetPathRoot(fullPath);
            char[] separators =
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            };

            string comparableFull = fullPath.TrimEnd(separators);
            string comparableRoot = (root ?? string.Empty).TrimEnd(separators);
            if (comparableFull.Equals(
                comparableRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            return comparableFull;
        }

        public static string GetDocumentPath(GenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            string folderName = "[" + request.StartYear + "] " +
                (request.ProjectName ?? string.Empty).Trim();
            string root = NormalizePath(request.DocumentRoot);
            return NormalizePath(Path.Combine(root, folderName));
        }

        public static string GetSourcePath(GenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            if (!request.CreateSourceFolder)
            {
                return string.Empty;
            }

            string root = NormalizePath(request.SourceRoot);
            return NormalizePath(Path.Combine(
                root,
                (request.SourceFolderName ?? string.Empty).Trim()));
        }

        public static string Validate(GenerationRequest request)
        {
            if (request == null)
            {
                return "생성 요청 정보가 없습니다.";
            }
            if (request.StartYear < 2000 || request.StartYear > 2100)
            {
                return "시작 연도는 2000년부터 2100년 사이로 입력하세요.";
            }
            if (!IsValidFolderName(request.ProjectName))
            {
                return "프로젝트명에 사용할 수 없는 문자나 예약어가 포함되어 있습니다.";
            }
            if (string.IsNullOrWhiteSpace(request.DocumentRoot))
            {
                return "문서 기본 위치를 입력하세요.";
            }

            string documentRoot;
            string documentPath;
            try
            {
                documentRoot = NormalizePath(request.DocumentRoot);
                documentPath = GetDocumentPath(request);
            }
            catch (Exception ex)
            {
                return "문서 경로가 올바르지 않습니다: " + ex.Message;
            }
            if (!IsChildPath(documentRoot, documentPath))
            {
                return "생성될 문서 경로가 문서 기본 위치 밖을 가리킵니다.";
            }

            List<string> documentFolders = request.DocumentFolders ??
                new List<string>();
            for (int i = 0; i < documentFolders.Count; i++)
            {
                if (!IsValidFolderName(documentFolders[i]))
                {
                    return "문서 하위 폴더명이 올바르지 않습니다: " +
                        documentFolders[i];
                }
            }

            string sourcePath = string.Empty;
            if (request.CreateSourceFolder)
            {
                if (!IsValidFolderName(request.SourceFolderName))
                {
                    return "소스 폴더명을 올바르게 입력하세요.";
                }
                if (string.IsNullOrWhiteSpace(request.SourceRoot))
                {
                    return "소스 기본 위치를 입력하세요.";
                }

                string sourceRoot;
                try
                {
                    sourceRoot = NormalizePath(request.SourceRoot);
                    sourcePath = GetSourcePath(request);
                }
                catch (Exception ex)
                {
                    return "소스 경로가 올바르지 않습니다: " + ex.Message;
                }
                if (!IsChildPath(sourceRoot, sourcePath))
                {
                    return "생성될 소스 경로가 소스 기본 위치 밖을 가리킵니다.";
                }

                if (PathsOverlap(documentPath, sourcePath))
                {
                    return "문서 경로와 소스 경로가 같거나 서로 포함되어 생성할 수 없습니다." +
                        Environment.NewLine + "문서: " + documentPath +
                        Environment.NewLine + "소스: " + sourcePath;
                }
            }

            if (Directory.Exists(documentPath) || File.Exists(documentPath))
            {
                return "같은 프로젝트 문서 폴더가 이미 존재합니다: " + documentPath;
            }
            if (request.CreateSourceFolder &&
                (Directory.Exists(sourcePath) || File.Exists(sourcePath)))
            {
                return "같은 소스 폴더가 이미 존재합니다: " + sourcePath;
            }

            return string.Empty;
        }

        public static string BuildPreview(GenerationRequest request)
        {
            string validation = ValidateForPreview(request);
            if (validation.Length > 0)
            {
                return validation;
            }

            string fullValidation = Validate(request);
            if (fullValidation.Length > 0)
            {
                return fullValidation;
            }

            StringBuilder text = new StringBuilder();
            string documentPath = GetDocumentPath(request);
            text.AppendLine("문서 프로젝트");
            text.AppendLine(documentPath);
            text.AppendLine("  ├─ 받은자료");
            text.AppendLine("  ├─ 문서");
            List<string> documentFolders = request.DocumentFolders ??
                new List<string>();
            for (int i = 0; i < documentFolders.Count; i++)
            {
                text.AppendLine("  │  └─ " + documentFolders[i]);
            }
            text.AppendLine("  └─ 배포자료");
            if (request.CreateArchiveFolder)
            {
                text.AppendLine("  └─ 보관자료");
            }

            text.AppendLine();
            if (request.CreateSourceFolder)
            {
                text.AppendLine("소스 프로젝트");
                text.AppendLine(GetSourcePath(request));
            }
            else
            {
                text.AppendLine("소스 폴더: 생성하지 않음");
            }
            text.AppendLine();
            text.AppendLine("기존 폴더나 파일은 덮어쓰지 않습니다.");
            text.AppendLine("실패한 현재 작업의 임시 산출물과 신규 결과만 안전하게 정리합니다.");
            text.AppendLine("Git 명령은 수행하지 않습니다.");
            return text.ToString();
        }

        public static GenerationResult Create(
            GenerationRequest request)
        {
            return Create(request, null);
        }

        internal static GenerationResult Create(
            GenerationRequest request,
            GenerationTestHooks hooks)
        {
            string validation = Validate(request);
            if (validation.Length > 0)
            {
                throw new InvalidOperationException(validation);
            }

            string operationId = Guid.NewGuid().ToString("N");
            string documentRoot = NormalizePath(request.DocumentRoot);
            string sourceRoot = request.CreateSourceFolder
                ? NormalizePath(request.SourceRoot)
                : string.Empty;
            string documentPath = GetDocumentPath(request);
            string sourcePath = GetSourcePath(request);
            GenerationTarget documentTarget = null;
            GenerationTarget sourceTarget = null;

            try
            {
                documentTarget = DefineTarget(
                    "document",
                    documentRoot,
                    documentPath,
                    operationId);
                InitializeTarget(documentTarget, operationId, hooks);
                if (request.CreateSourceFolder)
                {
                    sourceTarget = DefineTarget(
                        "source",
                        sourceRoot,
                        sourcePath,
                        operationId);
                    InitializeTarget(sourceTarget, operationId, hooks);
                }

                BuildDocumentStaging(documentTarget, request);

                CommitTarget(documentTarget, hooks);
                if (sourceTarget != null)
                {
                    CommitTarget(sourceTarget, hooks);
                }

                GenerationResult result = new GenerationResult();
                result.OperationId = operationId;
                result.DocumentPath = documentPath;
                result.SourcePath = sourcePath;
                AddResultPaths(result, documentTarget);
                if (sourceTarget != null)
                {
                    AddResultPaths(result, sourceTarget);
                }

                RemoveOwnershipMarker(documentTarget, result.CleanupWarnings);
                if (sourceTarget != null)
                {
                    RemoveOwnershipMarker(sourceTarget, result.CleanupWarnings);
                }
                return result;
            }
            catch (Exception ex)
            {
                List<string> remainingPaths = new List<string>();
                TryRollbackTarget(sourceTarget, remainingPaths);
                TryRollbackTarget(documentTarget, remainingPaths);

                StringBuilder message = new StringBuilder();
                message.Append("프로젝트 생성 작업 ");
                message.Append(operationId);
                message.Append("이 실패했습니다: ");
                message.Append(ex.Message);
                if (remainingPaths.Count == 0)
                {
                    message.Append(Environment.NewLine);
                    message.Append("실패한 현재 작업의 임시 산출물과 신규 결과를 정리했습니다.");
                }
                else
                {
                    message.Append(Environment.NewLine);
                    message.Append("안전하게 정리하지 못한 현재 작업 경로:");
                    for (int i = 0; i < remainingPaths.Count; i++)
                    {
                        message.Append(Environment.NewLine);
                        message.Append("- ");
                        message.Append(remainingPaths[i]);
                    }
                }

                throw new IOException(message.ToString(), ex);
            }
        }

        public static bool IsValidFolderName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string name = value.Trim();
            if (name.EndsWith(".", StringComparison.Ordinal) ||
                name.EndsWith(" ", StringComparison.Ordinal))
            {
                return false;
            }
            if (name == "." || name == "..")
            {
                return false;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            string baseName = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
            for (int i = 0; i < ReservedNames.Length; i++)
            {
                if (baseName == ReservedNames[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string ValidateForPreview(GenerationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ProjectName))
            {
                return "프로젝트명을 입력하면 생성될 구조가 여기에 표시됩니다.";
            }
            if (!IsValidFolderName(request.ProjectName))
            {
                return "프로젝트명에 사용할 수 없는 문자가 포함되어 있습니다.";
            }
            if (string.IsNullOrWhiteSpace(request.DocumentRoot))
            {
                return "문서 기본 위치를 입력하세요.";
            }
            if (request.CreateSourceFolder &&
                !IsValidFolderName(request.SourceFolderName))
            {
                return "빈 소스 폴더를 만들려면 소스 폴더명을 입력하세요.";
            }
            return string.Empty;
        }

        private static bool IsChildPath(string parent, string child)
        {
            string normalizedParent = NormalizePath(parent);
            string normalizedChild = NormalizePath(child);
            string parentPrefix = AppendDirectorySeparator(normalizedParent);
            return normalizedChild.StartsWith(
                parentPrefix,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsOverlap(string first, string second)
        {
            string normalizedFirst = NormalizePath(first);
            string normalizedSecond = NormalizePath(second);
            return normalizedFirst.Equals(
                    normalizedSecond,
                    StringComparison.OrdinalIgnoreCase) ||
                IsChildPath(normalizedFirst, normalizedSecond) ||
                IsChildPath(normalizedSecond, normalizedFirst);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(
                    Path.DirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal) ||
                path.EndsWith(
                    Path.AltDirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }

        private static GenerationTarget DefineTarget(
            string role,
            string rootPath,
            string finalPath,
            string operationId)
        {
            GenerationTarget target = new GenerationTarget();
            target.Role = role;
            target.RootPath = rootPath;
            target.FinalPath = finalPath;
            target.MarkerRelativePath = OwnershipMarkerName;
            target.StagingPath = Path.Combine(
                rootPath,
                ".new-project-generator-" + operationId + "-" + role +
                    ".staging");
            return target;
        }

        private static void InitializeTarget(
            GenerationTarget target,
            string operationId,
            GenerationTestHooks hooks)
        {
            Directory.CreateDirectory(target.RootPath);

            if (Directory.Exists(target.StagingPath) ||
                File.Exists(target.StagingPath))
            {
                throw new IOException(
                    "작업 스테이징 경로가 이미 존재합니다: " +
                    target.StagingPath);
            }

            Directory.CreateDirectory(target.StagingPath);
            target.StagingDirectoryCreated = true;

            string markerValue = "NewProjectGenerator" + Environment.NewLine +
                "OperationId=" + operationId + Environment.NewLine +
                "Role=" + target.Role + Environment.NewLine;
            WriteOwnedFile(target, OwnershipMarkerName, markerValue, hooks);
        }

        private static void BuildDocumentStaging(
            GenerationTarget target,
            GenerationRequest request)
        {
            CreateOwnedDirectory(target, "받은자료");
            CreateOwnedDirectory(target, "문서");
            CreateOwnedDirectory(target, "배포자료");

            List<string> documentFolders = request.DocumentFolders ??
                new List<string>();
            for (int i = 0; i < documentFolders.Count; i++)
            {
                CreateOwnedDirectory(
                    target,
                    Path.Combine("문서", documentFolders[i]));
            }
            if (request.CreateArchiveFolder)
            {
                CreateOwnedDirectory(target, "보관자료");
            }
        }

        private static void CreateOwnedDirectory(
            GenerationTarget target,
            string relativePath)
        {
            string normalizedRelative = NormalizeRelativePath(relativePath);
            if (target.ExpectedDirectories.Contains(normalizedRelative))
            {
                return;
            }

            string path = Path.Combine(target.StagingPath, normalizedRelative);
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new IOException("스테이징 항목이 이미 존재합니다: " + path);
            }
            Directory.CreateDirectory(path);
            target.ExpectedDirectories.Add(normalizedRelative);
            target.ResultRelativePaths.Add(normalizedRelative);
        }

        private static void WriteOwnedFile(
            GenerationTarget target,
            string relativePath,
            string content,
            GenerationTestHooks hooks)
        {
            string normalizedRelative = NormalizeRelativePath(relativePath);
            string path = Path.Combine(target.StagingPath, normalizedRelative);
            if (hooks != null && hooks.BeforeFileWrite != null)
            {
                hooks.BeforeFileWrite(path);
            }

            byte[] bytes = GetUtf8BomBytes(content ?? string.Empty);
            using (FileStream stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }

            target.ExpectedFileHashes[normalizedRelative] = ComputeHash(bytes);

        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return relativePath.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
        }

        private static byte[] GetUtf8BomBytes(string content)
        {
            Encoding encoding = new UTF8Encoding(true);
            byte[] preamble = encoding.GetPreamble();
            byte[] body = encoding.GetBytes(content);
            byte[] bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
            return bytes;
        }

        private static void CommitTarget(
            GenerationTarget target,
            GenerationTestHooks hooks)
        {
            if (hooks != null && hooks.BeforeCommit != null)
            {
                hooks.BeforeCommit(
                    target.Role,
                    target.StagingPath,
                    target.FinalPath);
            }

            if (Directory.Exists(target.FinalPath) || File.Exists(target.FinalPath))
            {
                throw new IOException(
                    "커밋 시점에 대상 경로가 이미 존재합니다: " +
                    target.FinalPath);
            }

            Directory.Move(target.StagingPath, target.FinalPath);
            target.Committed = true;

            if (hooks != null && hooks.AfterCommit != null)
            {
                hooks.AfterCommit(
                    target.Role,
                    target.StagingPath,
                    target.FinalPath);
            }
        }

        private static void AddResultPaths(
            GenerationResult result,
            GenerationTarget target)
        {
            result.CreatedPaths.Add(target.FinalPath);
            for (int i = 0; i < target.ResultRelativePaths.Count; i++)
            {
                result.CreatedPaths.Add(Path.Combine(
                    target.FinalPath,
                    target.ResultRelativePaths[i]));
            }
        }

        private static void RemoveOwnershipMarker(
            GenerationTarget target,
            List<string> warnings)
        {
            string markerPath = Path.Combine(
                target.FinalPath,
                target.MarkerRelativePath);
            string expectedHash;
            if (!target.ExpectedFileHashes.TryGetValue(
                    target.MarkerRelativePath,
                    out expectedHash))
            {
                warnings.Add(markerPath);
                return;
            }

            try
            {
                if (!File.Exists(markerPath) ||
                    !ComputeFileHash(markerPath).Equals(
                        expectedHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(markerPath);
                    return;
                }
                File.Delete(markerPath);
                if (File.Exists(markerPath))
                {
                    warnings.Add(markerPath);
                }
            }
            catch
            {
                warnings.Add(markerPath);
            }
        }

        private static void TryRollbackTarget(
            GenerationTarget target,
            List<string> remainingPaths)
        {
            if (target == null || string.IsNullOrEmpty(target.CurrentOwnedPath))
            {
                return;
            }

            string path = target.CurrentOwnedPath;
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            if (!target.StagingDirectoryCreated ||
                !IsExpectedOwnedPath(target, path) ||
                !OwnedInventoryMatches(target, path))
            {
                remainingPaths.Add(path);
                return;
            }

            try
            {
                List<string> files = new List<string>(
                    target.ExpectedFileHashes.Keys);
                files.Sort(CompareDeepestPathFirst);
                for (int i = 0; i < files.Count; i++)
                {
                    File.Delete(Path.Combine(path, files[i]));
                }

                List<string> directories = new List<string>(
                    target.ExpectedDirectories);
                directories.Sort(CompareDeepestPathFirst);
                for (int i = 0; i < directories.Count; i++)
                {
                    Directory.Delete(Path.Combine(path, directories[i]), false);
                }
                Directory.Delete(path, false);
            }
            catch
            {
                if (Directory.Exists(path) || File.Exists(path))
                {
                    remainingPaths.Add(path);
                }
            }
        }

        private static bool IsExpectedOwnedPath(
            GenerationTarget target,
            string path)
        {
            string normalized = NormalizePath(path);
            string expected = target.Committed
                ? NormalizePath(target.FinalPath)
                : NormalizePath(target.StagingPath);
            return normalized.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool OwnedInventoryMatches(
            GenerationTarget target,
            string path)
        {
            try
            {
                HashSet<string> actualDirectories = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> actualFiles = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                Stack<string> pending = new Stack<string>();
                pending.Push(path);

                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    string[] directories = Directory.GetDirectories(current);
                    for (int i = 0; i < directories.Length; i++)
                    {
                        FileAttributes attributes = File.GetAttributes(directories[i]);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            return false;
                        }

                        string relative = GetRelativePath(path, directories[i]);
                        actualDirectories.Add(relative);
                        pending.Push(directories[i]);
                    }

                    string[] files = Directory.GetFiles(current);
                    for (int i = 0; i < files.Length; i++)
                    {
                        FileAttributes attributes = File.GetAttributes(files[i]);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            return false;
                        }
                        actualFiles.Add(GetRelativePath(path, files[i]));
                    }
                }

                if (!actualDirectories.SetEquals(target.ExpectedDirectories) ||
                    actualFiles.Count != target.ExpectedFileHashes.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<string, string> item in
                    target.ExpectedFileHashes)
                {
                    if (!actualFiles.Contains(item.Key))
                    {
                        return false;
                    }
                    string actualHash = ComputeFileHash(
                        Path.Combine(path, item.Key));
                    if (!actualHash.Equals(
                            item.Value,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            string prefix = AppendDirectorySeparator(NormalizePath(root));
            string normalizedPath = NormalizePath(path);
            if (!normalizedPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "작업 경로 밖의 항목을 발견했습니다: " + path);
            }
            return NormalizeRelativePath(normalizedPath.Substring(prefix.Length));
        }

        private static int CompareDeepestPathFirst(string first, string second)
        {
            int lengthComparison = second.Length.CompareTo(first.Length);
            if (lengthComparison != 0)
            {
                return lengthComparison;
            }
            return string.Compare(
                second,
                first,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeHash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(bytes));
            }
        }

        private static string ComputeFileHash(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(stream));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                text.Append(bytes[i].ToString("X2"));
            }
            return text.ToString();
        }
    }
}
