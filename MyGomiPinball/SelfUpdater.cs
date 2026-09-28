using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MyGomiPinball
{
    internal enum UpdateProduct
    {
        Standard,
        Auto
    }

    internal sealed class UpdateManifest
    {
        public string ProductId { get; set; }
        public int SchemaVersion { get; set; }
        public string Version { get; set; }
        public string ReleaseNotes { get; set; }
        public UpdateFile Standard { get; set; }
        public UpdateFile Auto { get; set; }
    }

    internal sealed class UpdateFile
    {
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }

    internal static class SelfUpdater
    {
        private const string ManifestUrl = "https://raw.githubusercontent.com/Gyeon-ai/MyGomiPinball/main/update.json";
        private const string ManifestSignatureUrl = "https://raw.githubusercontent.com/Gyeon-ai/MyGomiPinball/main/update.json.sig";
        private const string UpdatePublicKeyResourceName = "MyGomiPinballUpdatePublicKey";
        private const int ManifestTimeoutMilliseconds = 7000;
        private const int DownloadTimeoutMilliseconds = 120000;
        private const int ParentExitTimeoutMilliseconds = 60000;
        private const int StartupSignalTimeoutMilliseconds = 20000;
        private const int MaximumManifestBytes = 64 * 1024;
        private const int MaximumSignatureDocumentBytes = 16 * 1024;
        private const long MaximumDownloadBytes = 100L * 1024L * 1024L;
        private static readonly object LogSync = new object();
        private static int _checkStarted;

        public static bool TryHandleApplyMode(string[] args, UpdateProduct product)
        {
            string encodedTarget = GetArgumentValue(args, "--apply-update=");
            if (String.IsNullOrWhiteSpace(encodedTarget))
            {
                return false;
            }

            string targetPath = null;
            try
            {
                ProductConfiguration configuration = GetConfiguration(product);
                targetPath = DecodePath(encodedTarget);
                string expectedSha256 = GetArgumentValue(args, "--expected-sha256=");
                int waitProcessId = ParseProcessId(GetArgumentValue(args, "--wait-pid="));
                ApplyDownloadedUpdate(configuration, targetPath, waitProcessId, expectedSha256);
            }
            catch (Exception ex)
            {
                WriteUpdateLog("apply", ex);
                MessageBox.Show(
                    "업데이트를 적용하지 못했습니다. 기존 프로그램은 그대로 유지됩니다.\r\n\r\n" + ex.Message,
                    "업데이트 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                TryRestartExistingApplication(targetPath);
            }

            return true;
        }

        public static void BeginUpdateCheck(Form owner, UpdateProduct product)
        {
            if (owner == null || owner.IsDisposed || Interlocked.Exchange(ref _checkStarted, 1) != 0)
            {
                return;
            }

            ProductConfiguration configuration = GetConfiguration(product);
            CleanupUpdateCacheAsync(configuration);
            CheckForUpdateAsync(owner, configuration);
        }

        public static void SignalSuccessfulStartup(string[] args)
        {
            string encodedSignalPath = GetArgumentValue(args, "--update-signal=");
            string token = GetArgumentValue(args, "--update-token=");
            if (String.IsNullOrWhiteSpace(encodedSignalPath) || String.IsNullOrWhiteSpace(token))
            {
                return;
            }

            try
            {
                string signalPath = Path.GetFullPath(DecodePath(encodedSignalPath));
                string tempDirectory = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string signalDirectory = Path.GetFullPath(Path.GetDirectoryName(signalPath)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fileName = Path.GetFileName(signalPath);
                if (!String.Equals(signalDirectory, tempDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !fileName.StartsWith("MyGomiPinballUpdate-", StringComparison.Ordinal) ||
                    !fileName.EndsWith(".signal", StringComparison.Ordinal))
                {
                    return;
                }

                File.WriteAllText(signalPath, token, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                WriteUpdateLog("startup-signal", ex);
            }
        }

        private static async void CheckForUpdateAsync(Form owner, ProductConfiguration configuration)
        {
            UpdateCandidate candidate;
            try
            {
                candidate = await GetUpdateCandidateAsync(configuration);
            }
            catch (Exception ex)
            {
                WriteUpdateLog("check", ex);
                return;
            }

            if (candidate == null || owner.IsDisposed || !owner.IsHandleCreated)
            {
                return;
            }

            using (var prompt = new UpdatePromptDialog(candidate.Version, candidate.ReleaseNotes))
            {
                if (prompt.ShowDialog(owner) != DialogResult.OK)
                {
                    return;
                }
            }

            string currentExecutable = Path.GetFullPath(Application.ExecutablePath);
            if (!CanWriteToDirectory(Path.GetDirectoryName(currentExecutable)))
            {
                MessageBox.Show(
                    "현재 프로그램이 있는 폴더에 파일을 쓸 수 없어 자동 업데이트할 수 없습니다.\r\n" +
                    "프로그램을 바탕화면이나 문서 폴더로 옮긴 뒤 다시 실행해 주세요.",
                    "업데이트 권한 필요",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            UpdateProgressDialog progress = null;
            try
            {
                progress = new UpdateProgressDialog();
                progress.Show(owner);
                progress.Refresh();

                string downloadedExecutable = await DownloadAndValidateUpdateAsync(
                    configuration,
                    candidate,
                    progress);

                progress.Close();
                progress.Dispose();
                progress = null;

                StartUpdateInstaller(downloadedExecutable, currentExecutable, candidate.File.Sha256);
                Application.Exit();
            }
            catch (Exception ex)
            {
                if (progress != null)
                {
                    progress.Close();
                    progress.Dispose();
                }

                WriteUpdateLog("download", ex);
                MessageBox.Show(
                    "업데이트를 완료하지 못했습니다. 기존 프로그램은 변경되지 않았습니다.\r\n\r\n" + ex.Message,
                    "업데이트 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static async Task<UpdateCandidate> GetUpdateCandidateAsync(ProductConfiguration configuration)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string cacheToken = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            byte[] manifestBytes;
            byte[] signatureDocumentBytes;

            using (var client = CreateWebClient(ManifestTimeoutMilliseconds))
            {
                manifestBytes = await client.DownloadDataTaskAsync(new Uri(AddCacheToken(ManifestUrl, cacheToken)));
                signatureDocumentBytes = await client.DownloadDataTaskAsync(new Uri(AddCacheToken(ManifestSignatureUrl, cacheToken)));
            }

            if (manifestBytes == null || manifestBytes.Length == 0 || manifestBytes.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException("업데이트 정보 크기가 허용 범위를 벗어났습니다.");
            }

            // 서명 검증을 마친 원문만 파싱해 다운로드 주소와 해시를 신뢰한다.
            VerifyManifestSignature(manifestBytes, signatureDocumentBytes);

            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(manifestBytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("업데이트 정보의 문자 형식이 올바르지 않습니다.", ex);
            }

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 64 * 1024;
            UpdateManifest manifest = serializer.Deserialize<UpdateManifest>(json);
            ValidateManifestIdentity(manifest);

            Version availableVersion;
            if (!Version.TryParse(manifest.Version, out availableVersion))
            {
                throw new InvalidDataException("업데이트 버전 정보가 올바르지 않습니다.");
            }

            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null || availableVersion <= currentVersion)
            {
                return null;
            }

            UpdateFile file = configuration.Product == UpdateProduct.Standard ? manifest.Standard : manifest.Auto;
            ValidateManifestFile(file);

            return new UpdateCandidate
            {
                Version = availableVersion,
                ReleaseNotes = NormalizeReleaseNotes(manifest.ReleaseNotes),
                File = file
            };
        }

        private static void ValidateManifestIdentity(UpdateManifest manifest)
        {
            // A shared publisher key authenticates the signer, not the intended product.
            if (manifest == null || manifest.SchemaVersion != 2 ||
                !String.Equals(manifest.ProductId, "Gyeon-ai/MyGomiPinball", StringComparison.Ordinal))
            {
                throw new InvalidDataException("지원하지 않는 업데이트 정보 형식입니다.");
            }
        }

        private static string AddCacheToken(string url, string cacheToken)
        {
            string separator = url.IndexOf('?') >= 0 ? "&" : "?";
            return url + separator + "cache=" + cacheToken;
        }

        private static void VerifyManifestSignature(byte[] manifestBytes, byte[] signatureDocumentBytes)
        {
            if (signatureDocumentBytes == null ||
                signatureDocumentBytes.Length == 0 ||
                signatureDocumentBytes.Length > MaximumSignatureDocumentBytes)
            {
                throw new InvalidDataException("업데이트 서명 크기가 허용 범위를 벗어났습니다.");
            }

            byte[] signature;
            try
            {
                string signatureText = new UTF8Encoding(false, true).GetString(signatureDocumentBytes).Trim();
                signature = Convert.FromBase64String(signatureText);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("업데이트 서명 형식이 올바르지 않습니다.", ex);
            }

            string publicKeyXml;
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(UpdatePublicKeyResourceName))
            {
                if (stream == null || stream.Length <= 0 || stream.Length > MaximumSignatureDocumentBytes)
                {
                    throw new InvalidDataException("내장된 업데이트 공개키를 읽지 못했습니다.");
                }

                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false))
                {
                    publicKeyXml = reader.ReadToEnd();
                }
            }

            var cspParameters = new CspParameters { ProviderType = 24 };
            using (var rsa = new RSACryptoServiceProvider(cspParameters))
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(publicKeyXml);
                string sha256Oid = CryptoConfig.MapNameToOID("SHA256");
                if (!rsa.VerifyData(manifestBytes, sha256Oid, signature))
                {
                    throw new CryptographicException("업데이트 정보의 디지털 서명이 올바르지 않습니다.");
                }
            }
        }

        private static async Task<string> DownloadAndValidateUpdateAsync(
            ProductConfiguration configuration,
            UpdateCandidate candidate,
            UpdateProgressDialog progress)
        {
            string cacheDirectory = GetProductCacheDirectory(configuration);
            Directory.CreateDirectory(cacheDirectory);
            string hashPrefix = candidate.File.Sha256.Substring(0, 12).ToLowerInvariant();
            string finalPath = Path.Combine(
                cacheDirectory,
                configuration.AssemblyName + "-" + candidate.Version + "-" + hashPrefix + ".exe");

            if (File.Exists(finalPath))
            {
                try
                {
                    ValidateDownloadedExecutable(finalPath, configuration, candidate);
                    progress.SetProgress(100);
                    return finalPath;
                }
                catch
                {
                    SafeDelete(finalPath);
                }
            }

            string partialPath = finalPath + ".download";
            SafeDelete(partialPath);

            try
            {
                using (var client = CreateWebClient(DownloadTimeoutMilliseconds))
                {
                    client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                    {
                        progress.SetProgress(e.ProgressPercentage);
                    };
                    await client.DownloadFileTaskAsync(new Uri(candidate.File.Url), partialPath);
                }

                ValidateDownloadedExecutable(partialPath, configuration, candidate);
                SafeDelete(finalPath);
                File.Move(partialPath, finalPath);
                return finalPath;
            }
            catch
            {
                SafeDelete(partialPath);
                throw;
            }
        }

        private static void ValidateManifestFile(UpdateFile file)
        {
            if (file == null)
            {
                throw new InvalidDataException("현재 프로그램용 업데이트 파일 정보가 없습니다.");
            }

            Uri downloadUri;
            if (!Uri.TryCreate(file.Url, UriKind.Absolute, out downloadUri) ||
                !String.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !IsAllowedDownloadHost(downloadUri.Host))
            {
                throw new InvalidDataException("업데이트 다운로드 주소가 올바르지 않습니다.");
            }

            if (!IsValidSha256(file.Sha256))
            {
                throw new InvalidDataException("업데이트 파일 해시가 올바르지 않습니다.");
            }

            if (file.Size <= 0 || file.Size > MaximumDownloadBytes)
            {
                throw new InvalidDataException("업데이트 파일 크기가 허용 범위를 벗어났습니다.");
            }
        }

        private static void ValidateDownloadedExecutable(
            string path,
            ProductConfiguration configuration,
            UpdateCandidate candidate)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != candidate.File.Size)
            {
                throw new InvalidDataException("다운로드한 업데이트 파일 크기가 일치하지 않습니다.");
            }

            string actualHash = ComputeSha256(path);
            if (!String.Equals(actualHash, candidate.File.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("다운로드한 업데이트 파일의 SHA256이 일치하지 않습니다.");
            }

            ValidateAssemblyIdentity(path, configuration.AssemblyName, candidate.Version);
        }

        private static void StartUpdateInstaller(string downloadedExecutable, string targetPath, string expectedSha256)
        {
            string encodedTarget = Convert.ToBase64String(Encoding.UTF8.GetBytes(targetPath));
            string arguments =
                "--apply-update=" + encodedTarget +
                " --wait-pid=" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                " --expected-sha256=" + expectedSha256.ToUpperInvariant();

            var startInfo = new ProcessStartInfo(downloadedExecutable, arguments);
            startInfo.UseShellExecute = false;
            startInfo.WorkingDirectory = Path.GetDirectoryName(targetPath);
            Process.Start(startInfo);
        }

        private static void ApplyDownloadedUpdate(
            ProductConfiguration configuration,
            string targetPath,
            int waitProcessId,
            string expectedSha256)
        {
            if (!IsValidSha256(expectedSha256))
            {
                throw new InvalidDataException("업데이트 검증 해시가 올바르지 않습니다.");
            }

            string sourcePath = Path.GetFullPath(Application.ExecutablePath);
            targetPath = Path.GetFullPath(targetPath);
            if (String.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("실행 중인 파일은 직접 교체할 수 없습니다.");
            }

            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException("교체할 기존 프로그램을 찾지 못했습니다.", targetPath);
            }

            string sourceHash = ComputeSha256(sourcePath);
            if (!String.Equals(sourceHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("업데이트 실행 파일의 SHA256이 일치하지 않습니다.");
            }

            AssemblyName sourceAssembly = ValidateAssemblyIdentity(sourcePath, configuration.AssemblyName, null);
            ValidateAssemblyIdentity(targetPath, configuration.AssemblyName, null);
            WaitForProcessExit(waitProcessId);

            if (!CanWriteToDirectory(Path.GetDirectoryName(targetPath)))
            {
                throw new UnauthorizedAccessException("기존 프로그램 폴더에 파일을 쓸 수 없습니다.");
            }

            string startupToken = Guid.NewGuid().ToString("N");
            string signalPath = Path.Combine(Path.GetTempPath(), "MyGomiPinballUpdate-" + startupToken + ".signal");
            SafeDelete(signalPath);

            // 새 프로세스의 시작 확인 신호를 받을 때까지 기존 EXE를 복구용으로 보관한다.
            string backupPath = ReplaceExecutable(sourcePath, targetPath, sourceHash);
            Process updatedProcess = null;
            try
            {
                ValidateAssemblyIdentity(targetPath, configuration.AssemblyName, sourceAssembly.Version);
                string encodedSignalPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(signalPath));
                string arguments = "--update-signal=" + encodedSignalPath + " --update-token=" + startupToken;
                var startInfo = new ProcessStartInfo(targetPath, arguments);
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Path.GetDirectoryName(targetPath);
                updatedProcess = Process.Start(startInfo);
                if (updatedProcess == null || !WaitForStartupSignal(updatedProcess, signalPath, startupToken))
                {
                    StopProcess(updatedProcess);
                    RestoreBackup(targetPath, backupPath);
                    throw new InvalidOperationException("새 버전이 정상적으로 시작되지 않아 기존 버전으로 복구했습니다.");
                }

                SafeDelete(backupPath);
            }
            catch
            {
                if (File.Exists(backupPath))
                {
                    StopProcess(updatedProcess);
                    RestoreBackup(targetPath, backupPath);
                }
                throw;
            }
            finally
            {
                SafeDelete(signalPath);
                if (updatedProcess != null)
                {
                    updatedProcess.Dispose();
                }
            }
        }

        private static string ReplaceExecutable(string sourcePath, string targetPath, string expectedSha256)
        {
            string stagingPath = targetPath + ".update-new";
            string backupPath = targetPath + ".update-backup";
            SafeDelete(stagingPath);
            SafeDelete(backupPath);
            File.Copy(sourcePath, stagingPath, true);

            if (!String.Equals(ComputeSha256(stagingPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                SafeDelete(stagingPath);
                throw new InvalidDataException("교체 준비 파일의 SHA256이 일치하지 않습니다.");
            }

            Exception lastError = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    SafeDelete(backupPath);
                    File.Replace(stagingPath, targetPath, backupPath, true);
                    lastError = null;
                    break;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                Thread.Sleep(500);
            }

            if (lastError != null)
            {
                SafeDelete(stagingPath);
                throw new IOException("기존 프로그램 파일을 교체하지 못했습니다.", lastError);
            }

            try
            {
                string installedHash = ComputeSha256(targetPath);
                if (!String.Equals(installedHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("교체된 프로그램의 SHA256이 일치하지 않습니다.");
                }

                return backupPath;
            }
            catch
            {
                RestoreBackup(targetPath, backupPath);
                throw;
            }
        }

        private static bool WaitForStartupSignal(Process process, string signalPath, string expectedToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < StartupSignalTimeoutMilliseconds)
            {
                if (process.HasExited)
                {
                    return false;
                }

                try
                {
                    if (File.Exists(signalPath))
                    {
                        string actualToken = File.ReadAllText(signalPath, Encoding.UTF8).Trim();
                        return String.Equals(actualToken, expectedToken, StringComparison.Ordinal);
                    }
                }
                catch (IOException)
                {
                }

                Thread.Sleep(100);
            }

            return false;
        }

        private static void StopProcess(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }
        }

        private static void RestoreBackup(string targetPath, string backupPath)
        {
            if (!File.Exists(backupPath))
            {
                return;
            }

            try
            {
                if (File.Exists(targetPath))
                {
                    File.Replace(backupPath, targetPath, null, true);
                }
                else
                {
                    File.Move(backupPath, targetPath);
                }
            }
            catch (Exception ex)
            {
                WriteUpdateLog("rollback", ex);
            }
        }

        private static AssemblyName ValidateAssemblyIdentity(string path, string expectedAssemblyName, Version expectedVersion)
        {
            AssemblyName assemblyName;
            try
            {
                assemblyName = AssemblyName.GetAssemblyName(path);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("업데이트 파일이 올바른 Windows 프로그램이 아닙니다.", ex);
            }

            if (!String.Equals(assemblyName.Name, expectedAssemblyName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("다른 종류의 프로그램 업데이트가 감지되었습니다.");
            }

            if (expectedVersion != null && !expectedVersion.Equals(assemblyName.Version))
            {
                throw new InvalidDataException("업데이트 파일의 프로그램 버전이 일치하지 않습니다.");
            }

            return assemblyName;
        }

        private static void WaitForProcessExit(int processId)
        {
            if (processId <= 0 || processId == Process.GetCurrentProcess().Id)
            {
                return;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (!process.HasExited && !process.WaitForExit(ParentExitTimeoutMilliseconds))
                    {
                        throw new TimeoutException("기존 프로그램이 제한 시간 안에 종료되지 않았습니다.");
                    }
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private static TimeoutWebClient CreateWebClient(int timeoutMilliseconds)
        {
            var client = new TimeoutWebClient(timeoutMilliseconds);
            client.Headers[HttpRequestHeader.UserAgent] = "MyGomiPinball-Updater/1.0";
            client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return client;
        }

        private static bool IsAllowedDownloadHost(string host)
        {
            return String.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidSha256(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static bool CanWriteToDirectory(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            string testPath = Path.Combine(directory, ".mygomi-update-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(testPath, "update-test", Encoding.ASCII);
                File.Delete(testPath);
                return true;
            }
            catch
            {
                SafeDelete(testPath);
                return false;
            }
        }

        private static string GetArgumentValue(string[] args, string prefix)
        {
            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string value = args[i];
                if (value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring(prefix.Length);
                }
            }

            return null;
        }

        private static string DecodePath(string encodedPath)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("업데이트 대상 경로를 읽지 못했습니다.", ex);
            }
        }

        private static int ParseProcessId(string value)
        {
            int processId;
            if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out processId) || processId < 0)
            {
                throw new InvalidDataException("종료 대기 프로세스 정보가 올바르지 않습니다.");
            }
            return processId;
        }

        private static string NormalizeReleaseNotes(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return "안정성 개선과 최신 수정사항이 포함되어 있습니다.";
            }

            string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= 180 ? normalized : normalized.Substring(0, 177) + "...";
        }

        private static ProductConfiguration GetConfiguration(UpdateProduct product)
        {
            if (product == UpdateProduct.Auto)
            {
                return new ProductConfiguration
                {
                    Product = product,
                    AssemblyName = "MyGomiPinballAuto",
                    DisplayName = "곰이 종겜핀볼 자동판",
                    CacheKey = "auto"
                };
            }

            return new ProductConfiguration
            {
                Product = product,
                AssemblyName = "MyGomiPinball",
                DisplayName = "곰이 종겜핀볼",
                CacheKey = "standard"
            };
        }

        private static string GetProductCacheDirectory(ProductConfiguration configuration)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Gyeona",
                "MyGomiPinball",
                "Updates",
                configuration.CacheKey);
        }

        private static void CleanupUpdateCacheAsync(ProductConfiguration configuration)
        {
            Task.Run(delegate
            {
                string cacheDirectory = GetProductCacheDirectory(configuration);
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        if (!Directory.Exists(cacheDirectory))
                        {
                            return;
                        }

                        string currentExecutable = Path.GetFullPath(Application.ExecutablePath);
                        string[] files = Directory.GetFiles(cacheDirectory, "*", SearchOption.TopDirectoryOnly);
                        for (int i = 0; i < files.Length; i++)
                        {
                            if (!String.Equals(Path.GetFullPath(files[i]), currentExecutable, StringComparison.OrdinalIgnoreCase))
                            {
                                SafeDelete(files[i]);
                            }
                        }

                        if (Directory.GetFiles(cacheDirectory).Length == 0)
                        {
                            Directory.Delete(cacheDirectory, false);
                            return;
                        }
                    }
                    catch
                    {
                    }
                }
            });
        }

        private static void TryRestartExistingApplication(string targetPath)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                {
                    Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
                }
            }
            catch
            {
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void WriteUpdateLog(string stage, Exception ex)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Gyeona",
                    "MyGomiPinball");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "updater.log");
                string message = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                                 " [" + stage + "] " + ex + Environment.NewLine + Environment.NewLine;
                lock (LogSync)
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    {
                        File.WriteAllText(path, message, Encoding.UTF8);
                    }
                    else
                    {
                        File.AppendAllText(path, message, Encoding.UTF8);
                    }
                }
            }
            catch
            {
            }
        }

        private sealed class ProductConfiguration
        {
            public UpdateProduct Product;
            public string AssemblyName;
            public string DisplayName;
            public string CacheKey;
        }

        private sealed class UpdateCandidate
        {
            public Version Version;
            public string ReleaseNotes;
            public UpdateFile File;
        }

        private sealed class TimeoutWebClient : WebClient
        {
            private readonly int _timeoutMilliseconds;

            public TimeoutWebClient(int timeoutMilliseconds)
            {
                _timeoutMilliseconds = timeoutMilliseconds;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = _timeoutMilliseconds;
                var httpRequest = request as HttpWebRequest;
                if (httpRequest != null)
                {
                    httpRequest.ReadWriteTimeout = _timeoutMilliseconds;
                    httpRequest.AllowAutoRedirect = true;
                }
                return request;
            }
        }
    }


    internal sealed class UpdateProgressDialog : Form
    {
        private readonly ProgressBar _progressBar;

        public UpdateProgressDialog()
        {
            Text = "업데이트 다운로드";
            ClientSize = new Size(390, 112);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            BackColor = Color.FromArgb(255, 252, 250);
            Font = UiFont.Make(9.0f, FontStyle.Regular);

            var label = new Label();
            label.Text = "업데이트 파일을 안전하게 확인하고 있습니다.";
            label.SetBounds(24, 20, 342, 24);
            label.Font = UiFont.Make(9.2f, FontStyle.Bold);
            label.ForeColor = Color.FromArgb(43, 28, 38);
            Controls.Add(label);

            _progressBar = new ProgressBar();
            _progressBar.SetBounds(24, 58, 342, 18);
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_progressBar);
        }

        public void SetProgress(int value)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { SetProgress(value); });
                return;
            }

            _progressBar.Value = Math.Max(_progressBar.Minimum, Math.Min(_progressBar.Maximum, value));
        }
    }
}
