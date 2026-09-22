using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NewProjectGenerator
{
    internal static class GeneratorCoreTests
    {
        private static int assertionCount;

        private static int Main()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "NewProjectGeneratorTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempRoot);
                TestSuccessfulGeneration(tempRoot);
                TestDocumentOnlyGeneration(tempRoot);
                TestInvalidFolderName(tempRoot);
                TestPathNormalization(tempRoot);
                TestPathCollisions(tempRoot);
                TestExistingTargetIsUntouched(tempRoot);
                TestCommitTimeCollisionAndCompensation(
                    tempRoot);
                TestFileWriteFailureRollback(tempRoot);
                TestCommitFailureRollback(tempRoot);
                TestUnsafeRollbackIsReported(tempRoot);
                TestExplorerFailureKeepsGeneration(tempRoot);

                Console.WriteLine(
                    "PASS: 모든 생성기 핵심 검증을 통과했습니다. (" +
                    assertionCount + " assertions)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
            finally
            {
                if (Directory.Exists(tempRoot) &&
                    tempRoot.StartsWith(
                        Path.GetTempPath(),
                        StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(tempRoot).StartsWith(
                        "NewProjectGeneratorTest_",
                        StringComparison.Ordinal))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static void TestSuccessfulGeneration(string tempRoot)
        {
            GenerationRequest request = CreateRequest(Path.Combine(tempRoot, "success"), "검증 프로젝트 [한글]");
            request.CreateArchiveFolder = true;
            request.DocumentFolders = new List<string> { "설계자료", "사용자매뉴얼" };
            Assert(ProjectGenerator.Validate(request) == string.Empty, "정상 입력 검증");
            Assert(!ProjectGenerator.BuildPreview(request).Contains(".md"), "미리보기는 폴더만 표시");
            GenerationResult result = ProjectGenerator.Create(request);
            Assert(Directory.Exists(Path.Combine(result.DocumentPath, "받은자료")), "받은자료 생성");
            Assert(Directory.Exists(Path.Combine(result.DocumentPath, "배포자료")), "배포자료 생성");
            Assert(Directory.Exists(Path.Combine(result.DocumentPath, "문서", "설계자료")), "선택 폴더 생성");
            Assert(Directory.Exists(Path.Combine(result.DocumentPath, "문서", "사용자매뉴얼")), "두 번째 선택 폴더 생성");
            Assert(!Directory.Exists(Path.Combine(result.DocumentPath, "문서", "감리자료")), "미선택 폴더 미생성");
            Assert(Directory.Exists(Path.Combine(result.DocumentPath, "보관자료")), "보관자료 생성");
            Assert(Directory.Exists(result.SourcePath), "소스 폴더 생성");
            Assert(Directory.GetFiles(result.DocumentPath, "*", SearchOption.AllDirectories).Length == 0,
                "문서에 Markdown과 작업 표시 파일을 포함한 파일이 없음");
            Assert(Directory.GetFileSystemEntries(result.SourcePath).Length == 0, "소스 폴더는 비어 있음");
            foreach (string path in result.CreatedPaths)
            {
                Assert(Directory.Exists(path), "생성 결과 목록은 실제 폴더만 포함");
            }
            Assert(ProjectGenerator.Validate(request).Length > 0, "기존 프로젝트 덮어쓰기 차단");
            Assert(result.CleanupWarnings.Count == 0, "정리 경고 없음");
            AssertNoStaging(request.DocumentRoot, "문서 스테이징 정리");
            AssertNoStaging(request.SourceRoot, "소스 스테이징 정리");
        }

        private static void TestDocumentOnlyGeneration(string tempRoot)
        {
            GenerationRequest request = CreateRequest(Path.Combine(tempRoot, "document-only"), "문서 전용");
            request.CreateSourceFolder = false;
            Assert(!ProjectGenerator.BuildPreview(request).Contains(".md"), "문서 전용 미리보기는 폴더만 표시");
            GenerationResult result = ProjectGenerator.Create(request);
            Assert(Directory.GetFiles(result.DocumentPath, "*", SearchOption.AllDirectories).Length == 0,
                "문서 전용 결과에 파일 없음");
            Assert(Directory.GetDirectories(result.DocumentPath).Length == 3, "기본 세 폴더만 생성");
            Assert(Directory.GetDirectories(Path.Combine(result.DocumentPath, "문서")).Length == 0,
                "미선택 문서 하위 폴더 없음");
            Assert(result.SourcePath == string.Empty && !Directory.Exists(request.SourceRoot), "소스 미생성");
            Assert(result.CleanupWarnings.Count == 0, "문서 전용 정리 경고 없음");
            AssertNoStaging(request.DocumentRoot, "문서 전용 스테이징 정리");
        }
        private static void TestInvalidFolderName(string tempRoot)
        {
            GenerationRequest invalid = CreateRequest(
                Path.Combine(tempRoot, "invalid-name"),
                "잘못된/이름");
            invalid.CreateSourceFolder = false;
            Assert(ProjectGenerator.Validate(invalid).Length > 0,
                "잘못된 폴더명 차단");
        }

        private static void TestPathNormalization(string tempRoot)
        {
            string driveRoot = Path.GetPathRoot(Directory.GetCurrentDirectory());
            string normalizedDriveRoot = ProjectGenerator.NormalizePath(driveRoot);
            Assert(normalizedDriveRoot.Equals(
                    driveRoot,
                    StringComparison.OrdinalIgnoreCase),
                "드라이브 루트의 끝 구분자 보존");
            Assert(normalizedDriveRoot.EndsWith(
                    Path.DirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal),
                "드라이브 루트가 D: 형태로 축약되지 않음");

            GenerationRequest driveRequest = CreateRequest(
                Path.Combine(tempRoot, "unused-drive"),
                "루트 경로");
            driveRequest.DocumentRoot = driveRoot;
            driveRequest.CreateSourceFolder = false;
            Assert(ProjectGenerator.GetDocumentPath(driveRequest).Equals(
                    ProjectGenerator.NormalizePath(Path.Combine(
                        driveRoot,
                        "[2026] 루트 경로")),
                    StringComparison.OrdinalIgnoreCase),
                "드라이브 루트 기반 최종 경로 계산");

            Assert(ProjectGenerator.NormalizePath("%TEMP%").Equals(
                    ProjectGenerator.NormalizePath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase),
                "%TEMP% 환경변수 확장");

            string variableName = "NPG_TEST_UNICODE_ROOT";
            string previous = Environment.GetEnvironmentVariable(variableName);
            string unicodeRoot = Path.Combine(tempRoot, "한글 경로 공백");
            try
            {
                Environment.SetEnvironmentVariable(variableName, unicodeRoot);
                string variablePath = "%" + variableName + "%" +
                    Path.DirectorySeparatorChar + "하위 폴더" +
                    Path.DirectorySeparatorChar;
                string expected = ProjectGenerator.NormalizePath(Path.Combine(
                    unicodeRoot,
                    "하위 폴더"));
                Assert(ProjectGenerator.NormalizePath(variablePath).Equals(
                        expected,
                        StringComparison.OrdinalIgnoreCase),
                    "환경변수와 한글·공백·끝 구분자 정규화");

                GenerationRequest request = CreateRequest(
                    Path.Combine(tempRoot, "unused-env"),
                    "환경 경로");
                request.DocumentRoot = "%" + variableName + "%";
                request.SourceRoot = "%" + variableName + "%" +
                    Path.DirectorySeparatorChar + "소스 루트";
                string validation = ProjectGenerator.Validate(request);
                Assert(validation == string.Empty,
                    "Validate가 환경변수 정규화 결과 사용");
                Assert(ProjectGenerator.GetDocumentPath(request).StartsWith(
                        ProjectGenerator.NormalizePath(unicodeRoot),
                        StringComparison.OrdinalIgnoreCase),
                    "GetDocumentPath가 공통 정규화 결과 사용");
                Assert(ProjectGenerator.GetSourcePath(request).StartsWith(
                        ProjectGenerator.NormalizePath(Path.Combine(
                            unicodeRoot,
                            "소스 루트")),
                        StringComparison.OrdinalIgnoreCase),
                    "GetSourcePath가 공통 정규화 결과 사용");
            }
            finally
            {
                Environment.SetEnvironmentVariable(variableName, previous);
            }
        }

        private static void TestPathCollisions(string tempRoot)
        {
            string testRoot = Path.Combine(tempRoot, "path-collisions");

            GenerationRequest same = CreateRequest(testRoot, "동일 경로");
            string sameDocumentPath = ProjectGenerator.GetDocumentPath(same);
            same.SourceRoot = same.DocumentRoot;
            same.SourceFolderName = "[2026] 동일 경로";
            AssertCollision(same, sameDocumentPath, sameDocumentPath,
                "문서·소스 동일 경로 차단");

            GenerationRequest documentParent = CreateRequest(
                Path.Combine(testRoot, "document-parent"),
                "문서 부모");
            string documentParentPath =
                ProjectGenerator.GetDocumentPath(documentParent);
            documentParent.SourceRoot = documentParentPath;
            documentParent.SourceFolderName = "소스 자식";
            AssertCollision(
                documentParent,
                documentParentPath,
                ProjectGenerator.GetSourcePath(documentParent),
                "문서가 소스의 부모인 중첩 차단");

            GenerationRequest sourceParent = CreateRequest(
                Path.Combine(testRoot, "source-parent-case"),
                "문서 자식");
            sourceParent.SourceRoot = Path.Combine(testRoot, "shared-root");
            sourceParent.SourceFolderName = "소스 부모";
            string sourceParentPath = ProjectGenerator.GetSourcePath(sourceParent);
            sourceParent.DocumentRoot = sourceParentPath;
            AssertCollision(
                sourceParent,
                ProjectGenerator.GetDocumentPath(sourceParent),
                sourceParentPath,
                "소스가 문서의 부모인 중첩 차단");
        }

        private static void TestExistingTargetIsUntouched(
            string tempRoot)
        {
            GenerationRequest request = CreateRequest(
                Path.Combine(tempRoot, "preexisting"),
                "기존 대상");
            string documentPath = ProjectGenerator.GetDocumentPath(request);
            Directory.CreateDirectory(documentPath);
            string agentsPath = Path.Combine(documentPath, "AGENTS.md");
            byte[] original = Encoding.UTF8.GetBytes("기존 AGENTS 내용");
            File.WriteAllBytes(agentsPath, original);
            string hashBefore = ComputeFileHash(agentsPath);

            Assert(ProjectGenerator.Validate(request).Contains(documentPath),
                "기존 대상 경로를 표시하며 검증 차단");
            AssertThrows<InvalidOperationException>(
                delegate { ProjectGenerator.Create(request); },
                "기존 대상 생성 차단");
            Assert(File.ReadAllText(agentsPath, Encoding.UTF8) == "기존 AGENTS 내용",
                "기존 AGENTS.md 내용 유지");
            Assert(ComputeFileHash(agentsPath) == hashBefore,
                "기존 AGENTS.md 해시 유지");
        }

        private static void TestCommitTimeCollisionAndCompensation(
            string tempRoot)
        {
            GenerationRequest request = CreateRequest(
                Path.Combine(tempRoot, "commit-race"),
                "커밋 경쟁");
            string sourcePath = ProjectGenerator.GetSourcePath(request);
            string existingAgentsPath = Path.Combine(sourcePath, "AGENTS.md");
            string originalText = "생성 시점에 먼저 만들어진 기존 파일";
            string hashBefore = string.Empty;

            GenerationTestHooks hooks = new GenerationTestHooks();
            hooks.BeforeCommit = delegate(
                string role,
                string stagingPath,
                string finalPath)
            {
                if (role == "source")
                {
                    Directory.CreateDirectory(finalPath);
                    File.WriteAllText(existingAgentsPath, originalText, Encoding.UTF8);
                    hashBefore = ComputeFileHash(existingAgentsPath);
                }
            };

            IOException failure = AssertThrows<IOException>(
                delegate
                {
                    ProjectGenerator.Create(request, hooks);
                },
                "생성 시점 대상 충돌 차단");
            Assert(failure.Message.Contains(sourcePath),
                "커밋 충돌 오류에 대상 경로 표시");
            Assert(!Directory.Exists(ProjectGenerator.GetDocumentPath(request)),
                "소스 커밋 충돌 시 먼저 커밋한 문서 보상 롤백");
            Assert(File.Exists(existingAgentsPath),
                "경쟁으로 생긴 기존 소스 파일 유지");
            Assert(File.ReadAllText(existingAgentsPath, Encoding.UTF8) == originalText,
                "경쟁으로 생긴 기존 소스 내용 유지");
            Assert(ComputeFileHash(existingAgentsPath) == hashBefore,
                "경쟁으로 생긴 기존 소스 해시 유지");
            AssertNoStaging(request.DocumentRoot, "커밋 충돌 문서 스테이징 정리");
            AssertNoStaging(request.SourceRoot, "커밋 충돌 소스 스테이징 정리");
        }

        private static void TestFileWriteFailureRollback(
            string tempRoot)
        {
            GenerationRequest request = CreateRequest(
                Path.Combine(tempRoot, "write-failure"),
                "파일 실패");
            GenerationTestHooks hooks = new GenerationTestHooks();
            hooks.BeforeFileWrite = delegate(string path)
            {
                if (path.StartsWith(request.SourceRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("테스트 소스 작업 표시 파일 쓰기 실패");
                }
            };

            IOException failure = AssertThrows<IOException>(
                delegate
                {
                    ProjectGenerator.Create(request, hooks);
                },
                "파일 쓰기 실패 전달");
            Assert(failure.Message.Contains("정리했습니다"),
                "파일 쓰기 실패 후 정리 완료 보고");
            Assert(!Directory.Exists(ProjectGenerator.GetDocumentPath(request)),
                "파일 쓰기 실패 문서 결과 롤백");
            Assert(!Directory.Exists(ProjectGenerator.GetSourcePath(request)),
                "파일 쓰기 실패 소스 결과 롤백");
            AssertNoStaging(request.DocumentRoot, "파일 쓰기 실패 문서 스테이징 정리");
            AssertNoStaging(request.SourceRoot, "파일 쓰기 실패 소스 스테이징 정리");
        }

        private static void TestCommitFailureRollback(
            string tempRoot)
        {
            GenerationRequest request = CreateRequest(
                Path.Combine(tempRoot, "commit-failure"),
                "커밋 실패");
            GenerationTestHooks hooks = new GenerationTestHooks();
            hooks.AfterCommit = delegate(
                string role,
                string stagingPath,
                string finalPath)
            {
                if (role == "source")
                {
                    throw new IOException("테스트 커밋 후 실패");
                }
            };

            IOException failure = AssertThrows<IOException>(
                delegate
                {
                    ProjectGenerator.Create(request, hooks);
                },
                "커밋 중 실패 전달");
            Assert(failure.Message.Contains("정리했습니다"),
                "커밋 실패 후 보상 롤백 완료 보고");
            Assert(!Directory.Exists(ProjectGenerator.GetDocumentPath(request)),
                "커밋 실패 문서 결과 롤백");
            Assert(!Directory.Exists(ProjectGenerator.GetSourcePath(request)),
                "커밋 실패 소스 결과 롤백");
            AssertNoStaging(request.DocumentRoot, "커밋 실패 문서 스테이징 정리");
            AssertNoStaging(request.SourceRoot, "커밋 실패 소스 스테이징 정리");
        }

        private static void TestUnsafeRollbackIsReported(
            string tempRoot)
        {
            GenerationRequest request = CreateRequest(
                Path.Combine(tempRoot, "rollback-refusal"),
                "정리 거부");
            request.CreateSourceFolder = false;
            string documentPath = ProjectGenerator.GetDocumentPath(request);
            GenerationTestHooks hooks = new GenerationTestHooks();
            hooks.AfterCommit = delegate(
                string role,
                string stagingPath,
                string finalPath)
            {
                File.AppendAllText(
                    Path.Combine(finalPath, "external.txt"),
                    "외부 변경",
                    Encoding.UTF8);
                throw new IOException("외부 변경 후 실패");
            };

            IOException failure = AssertThrows<IOException>(
                delegate
                {
                    ProjectGenerator.Create(request, hooks);
                },
                "변경된 결과의 위험한 롤백 거부");
            Assert(Directory.Exists(documentPath),
                "내용이 바뀐 결과는 삭제하지 않음");
            Assert(failure.Message.Contains(documentPath),
                "정리 실패 시 남은 정확한 경로 보고");
        }

        private static void TestExplorerFailureKeepsGeneration(
            string tempRoot)
        {
            GenerationRequest request = CreateRequest(
                Path.Combine(tempRoot, "explorer-failure"),
                "Explorer 실패");
            request.CreateSourceFolder = false;
            GenerationResult generation =
                ProjectGenerator.Create(request);
            string generatedPath = generation.DocumentPath;

            FolderOpenResult openResult = ProjectFolderOpener.TryOpen(
                generatedPath,
                delegate(string path)
                {
                    throw new InvalidOperationException("Explorer 테스트 실패");
                });

            Assert(!openResult.Succeeded,
                "Explorer 실행 실패를 별도 결과로 반환");
            Assert(openResult.ErrorMessage.Contains("Explorer 테스트 실패"),
                "Explorer 실행 실패 원인 유지");
            Assert(generation.DocumentPath == generatedPath,
                "Explorer 실패 후 생성 경로 상태 유지");
            Assert(Directory.Exists(generatedPath),
                "Explorer 실패가 생성 결과를 롤백하지 않음");
            Assert(Directory.Exists(Path.Combine(generatedPath, "문서")), "Explorer 실패 후 폴더 유지");
            Assert(Directory.GetFiles(generatedPath, "*", SearchOption.AllDirectories).Length == 0, "Explorer 실패 후에도 파일 미생성");
        }

        private static GenerationRequest CreateRequest(
            string testRoot,
            string projectName)
        {
            GenerationRequest request = new GenerationRequest();
            request.ProjectName = projectName;
            request.StartYear = 2026;
            request.DocumentRoot = Path.Combine(testRoot, "문서 루트");
            request.SourceRoot = Path.Combine(testRoot, "소스 루트");
            request.CreateSourceFolder = true;
            request.SourceFolderName = "VALIDATION_APP";
            request.CreateArchiveFolder = false;
            request.DocumentFolders = new List<string>();
            return request;
        }

        private static void AssertCollision(
            GenerationRequest request,
            string documentPath,
            string sourcePath,
            string message)
        {
            string validation = ProjectGenerator.Validate(request);
            Assert(validation.Length > 0, message);
            Assert(validation.Contains(documentPath),
                message + " - 문서 경로 표시");
            Assert(validation.Contains(sourcePath),
                message + " - 소스 경로 표시");
        }

        private static void AssertNoStaging(string root, string message)
        {
            if (!Directory.Exists(root))
            {
                Assert(true, message);
                return;
            }

            string[] staging = Directory.GetFileSystemEntries(
                root,
                ".new-project-generator-*.staging",
                SearchOption.TopDirectoryOnly);
            Assert(staging.Length == 0, message);
        }

        private static T AssertThrows<T>(Action action, string message)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T ex)
            {
                assertionCount++;
                return ex;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "검증 실패: " + message +
                    " (예상 예외: " + typeof(T).Name +
                    ", 실제 예외: " + ex.GetType().Name + ")",
                    ex);
            }

            throw new InvalidOperationException(
                "검증 실패: " + message + " (예외가 발생하지 않음)");
        }

        private static string ComputeFileHash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("X2"));
                }
                return text.ToString();
            }
        }

        private static void Assert(bool condition, string message)
        {
            assertionCount++;
            if (!condition)
            {
                throw new InvalidOperationException("검증 실패: " + message);
            }
        }
    }
}
