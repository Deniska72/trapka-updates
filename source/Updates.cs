using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

[assembly: AssemblyVersion("0.8.0.0")]

static class AppPaths {
    public static string DataRoot {
        get {
            string path = Environment.GetEnvironmentVariable("TRAPKA_DATA_DIR");
            if (String.IsNullOrEmpty(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trapka");
            Directory.CreateDirectory(path); return path;
        }
    }
}

[XmlRoot("TrapkaUpdate")]
public class UpdateManifest {
    public string Version;
    public string Url;
    public long Size;
    public string Sha256;
    public string Signature;
    public byte[] SignedBytes() {
        return Encoding.UTF8.GetBytes("TrapkaUpdate-v1\n" + Version + "\n" + Url + "\n" + Size.ToString(CultureInfo.InvariantCulture) + "\n" + (Sha256 ?? "").ToUpperInvariant());
    }
}

class PreparedUpdate {
    public Version Version;
    public string Path;
}

static class Updates {
    public static readonly Version CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version;
    const long MaximumPackageSize = 200L * 1024 * 1024;
    static string SourcePath { get { return Path.Combine(AppPaths.DataRoot, "update-source.txt"); } }
    public static string Source {
        get {
            if (File.Exists(SourcePath)) return File.ReadAllText(SourcePath).Trim();
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trapka.update-source.txt"))
                if (resource != null) using (var reader = new StreamReader(resource)) return reader.ReadToEnd().Trim();
            return "";
        }
        set { if (!String.IsNullOrWhiteSpace(value)) RequireHttps(value); File.WriteAllText(SourcePath, value.Trim()); }
    }
    public static Uri RequireHttps(string value) {
        Uri uri;
        if (value == null || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !String.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Нужна ссылка HTTPS на файл манифеста обновлений.");
        return uri;
    }
    static string PublicKey {
        get { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trapka.update-public-key.xml")) {
            if (stream == null) throw new InvalidDataException("Update verification key is missing");
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        } }
    }
    public static UpdateManifest ParseAndVerify(byte[] bytes, string publicKey) {
        if (bytes == null || bytes.Length > 65536) throw new InvalidDataException("Update manifest is too large");
        UpdateManifest manifest;
        using (var stream = new MemoryStream(bytes)) using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            manifest = (UpdateManifest)new XmlSerializer(typeof(UpdateManifest)).Deserialize(reader);
        Version version;
        if (manifest == null || !Version.TryParse(manifest.Version, out version) || version.Major < 0 || version.Build < 0 || version.Revision < 0)
            throw new InvalidDataException("Invalid update version");
        RequireHttps(manifest.Url);
        if (manifest.Size < 1 || manifest.Size > MaximumPackageSize || !Regex.IsMatch(manifest.Sha256 ?? "", "\\A[0-9a-fA-F]{64}\\z") || String.IsNullOrEmpty(manifest.Signature))
            throw new InvalidDataException("Invalid update metadata");
        byte[] signature = Convert.FromBase64String(manifest.Signature);
        using (var rsa = new RSACryptoServiceProvider(new CspParameters(24))) {
            rsa.PersistKeyInCsp = false; rsa.FromXmlString(publicKey);
            if (!rsa.VerifyData(manifest.SignedBytes(), "SHA256", signature)) throw new InvalidDataException("Подпись обновления не прошла проверку.");
        }
        return manifest;
    }
    static Stream OpenHttps(string url) {
        Uri uri = RequireHttps(url);
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        for (int redirects = 0; redirects <= 5; ++redirects) {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            request.AllowAutoRedirect = false; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            request.UserAgent = "Trapka-Updater/" + CurrentVersion;
            var response = (HttpWebResponse)request.GetResponse();
            int status = (int)response.StatusCode;
            if (status >= 300 && status <= 399) {
                string location = response.Headers["Location"]; response.Dispose();
                if (String.IsNullOrEmpty(location)) throw new InvalidDataException("Redirect has no location");
                uri = RequireHttps(new Uri(uri, location).AbsoluteUri); continue;
            }
            if (status != 200) { response.Dispose(); throw new InvalidDataException("Unexpected HTTP response"); }
            return new ResponseStream(response);
        }
        throw new InvalidDataException("Too many update redirects");
    }
    class ResponseStream : Stream {
        readonly HttpWebResponse response; readonly Stream stream;
        public ResponseStream(HttpWebResponse value) { response = value; stream = value.GetResponseStream(); }
        public override int Read(byte[] buffer, int offset, int count) { return stream.Read(buffer, offset, count); }
        public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long length) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) { stream.Dispose(); response.Dispose(); } base.Dispose(disposing); }
    }
    static void CopyBounded(Stream input, Stream output, long maximum) {
        byte[] buffer = new byte[65536]; long length = 0; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
            length += count; if (length > maximum) throw new InvalidDataException("Update download exceeded its size limit");
            output.Write(buffer, 0, count);
        }
    }
    public static bool PackageMatches(string path, UpdateManifest manifest) {
        if (!File.Exists(path) || new FileInfo(path).Length != manifest.Size) return false;
        using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public static PreparedUpdate Prepare(UpdateManifest manifest, string cacheRoot, Func<string, Stream> open) {
        // Manifest must already be signature-verified by Check, or by the release tests.
        Version version = new Version(manifest.Version);
        string directory = Path.Combine(cacheRoot, version.ToString()); Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Trapka.exe");
        if (PackageMatches(target, manifest)) return new PreparedUpdate { Version = version, Path = target };
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        try {
            using (var source = open(manifest.Url)) using (var destination = File.Create(temporary)) CopyBounded(source, destination, manifest.Size);
            if (!PackageMatches(temporary, manifest)) throw new InvalidDataException("Контрольная сумма обновления не совпала.");
            if (File.Exists(target)) {
                if (PackageMatches(target, manifest)) return new PreparedUpdate { Version = version, Path = target };
                throw new InvalidDataException("An invalid cached update exists; the current app was retained");
            }
            File.Move(temporary, target);
            return new PreparedUpdate { Version = version, Path = target };
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static PreparedUpdate Check(string source) {
        RequireHttps(source); byte[] bytes;
        using (var input = OpenHttps(source)) using (var buffer = new MemoryStream()) { CopyBounded(input, buffer, 65536); bytes = buffer.ToArray(); }
        var manifest = ParseAndVerify(bytes, PublicKey);
        if (new Version(manifest.Version) <= CurrentVersion) return null;
        return Prepare(manifest, Path.Combine(AppPaths.DataRoot, "updates"), OpenHttps);
    }
    public static void Restart(PreparedUpdate update) {
        Process.Start(new ProcessStartInfo(update.Path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(update.Path) });
    }
}
