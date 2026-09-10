using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using ModManager;
using ModManager.Updates;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

internal static class UpdateChecks
{
    private static int _checks;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ErosUpdateChecks-" + Guid.NewGuid().ToString("N"));
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
        _checks++;
    }
    private static void Reject(Action action, string description)
    {
        bool rejected = false;
        try { action(); } catch { rejected = true; }
        Check(rejected, description);
    }

    public static async Task Main()
    {
        Directory.CreateDirectory(Root);
        Paths.BepInExRootPath = Path.Combine(Root, "BepInEx");
        Directory.CreateDirectory(Paths.PluginPath);
        Check(PendingUpdate.ParseVersion("v1.2.3") == PendingUpdate.ParseVersion("1.2.3.0"), "normalized versions");
        Check(PendingUpdate.ParseVersion("1.10.0") > PendingUpdate.ParseVersion("1.9.9"), "numeric version comparison");
        Reject(() => PendingUpdate.ParseVersion("v1.2.3-beta"), "prerelease tags");
        Check(ReleaseSource.Parse("", "Test.dll") == null, "unpublished registration");
        Check(ReleaseSource.Parse("https://github.com/test/repo/releases", "Test.dll").Repository == "test/repo", "release URL");
        foreach (string url in new[] { "http://github.com/test/repo/releases", "https://github.com.evil.com/test/repo/releases", "https://github.com/test/repo/releases?x=1", "https://github.com/test/repo" })
            Reject(() => ReleaseSource.Parse(url, "Test.dll"), "invalid source " + url);
        Reject(() => ReleaseSource.Parse("https://github.com/test/repo/releases", "../Test.dll"), "asset traversal");
        foreach (string path in new[] { "../outside.dll", "../../plugins-other/x.dll", "C:\\outside.dll", "Test.dll:evil" })
            Reject(() => PendingUpdate.ResolveTarget(Paths.PluginPath, path), "target traversal " + path);

        string oldDll = Path.Combine(Paths.PluginPath, "Test.dll");
        string newDll = Path.Combine(Root, "new.dll");
        MakeDll(oldDll, "com.eros.test", "1.0.0");
        MakeDll(newDll, "com.eros.test", "1.1.0");
        PendingUpdate.ValidateAssembly(newDll, "com.eros.test", "v1.1.0", "Test");
        _checks++;
        Reject(() => PendingUpdate.ValidateAssembly(newDll, "com.eros.other", "1.1.0"), "wrong mod");
        Reject(() => PendingUpdate.ValidateAssembly(newDll, "com.eros.test", "1.2.0"), "wrong version");
        Reject(() => PendingUpdate.ValidateAssembly(newDll, "com.eros.test", "1.1.0", "Other"), "wrong assembly");
        string folder = Path.Combine(Root, "apply"); Directory.CreateDirectory(folder);
        var pending = new PendingUpdate { Guid = "com.eros.test", Version = "1.1.0", RelativeTarget = "Test.dll", OriginalHash = PendingUpdate.Hash(oldDll), DownloadHash = PendingUpdate.Hash(newDll) };
        File.Copy(newDll, Path.Combine(folder, "payload.bin"));
        pending.Save(Path.Combine(folder, "pending.xml"));
        PendingUpdate loaded = PendingUpdate.Load(Path.Combine(folder, "pending.xml"));
        Check(loaded.DownloadHash == pending.DownloadHash, "manifest roundtrip");
        loaded.Apply(Paths.PluginPath, folder);
        Check(PendingUpdate.Hash(oldDll) == pending.DownloadHash, "atomic replacement");
        Check(PendingUpdate.Hash(Path.Combine(folder, "previous.bin")) == pending.OriginalHash, "backup retained");
        loaded.Apply(Paths.PluginPath, folder); _checks++; // Idempotent after an interrupted finalization.
        File.Copy(Path.Combine(folder, "previous.bin"), oldDll, true);
        File.Copy(newDll, Path.Combine(folder, "payload.bin"), true);
        loaded.DownloadHash = new string('0', 64);
        Reject(() => loaded.Apply(Paths.PluginPath, folder), "tampered payload");
        Check(PendingUpdate.Hash(oldDll) == pending.OriginalHash, "tamper leaves original intact");
        loaded.DownloadHash = pending.DownloadHash;
        loaded.OriginalHash = new string('0', 64);
        Reject(() => loaded.Apply(Paths.PluginPath, folder), "externally modified installed DLL");

        Chainloader.PluginInfos["com.eros.test"] = new PluginInfo { Metadata = new Metadata { Version = new Version("1.0.0") }, Location = oldDll };
        ModManagerApi.RegisterReleaseSource("com.eros.test", "https://github.com/test/repo/releases", "Test.dll");
        byte[] bytes = File.ReadAllBytes(newDll);
        var handler = new FakeHttp(bytes, pending.DownloadHash);
        var service = new UpdateService(new ManualLogSource(), new HttpClient(handler), false);
        await Run(service, false);
        Check(service.Get("com.eros.test").Available, "newer release detected");
        Check(service.Get("com.eros.test").Status == "Out of date", "out of date status");
        await Run(service, false);
        Check(handler.Checks == 1, "refresh cooldown");
        string updater = Path.Combine(Paths.BepInExRootPath, "patchers", "ModManager");
        Directory.CreateDirectory(updater);
        File.WriteAllText(Path.Combine(updater, "ModManager.Updater.dll"), "test installer marker");
        await Run(service, true);
        Check(service.Get("com.eros.test").Staged, "download staged");
        Check(PendingUpdate.Hash(oldDll) == pending.OriginalHash, "download does not modify running DLL");
        var reopened = new UpdateService(new ManualLogSource(), new HttpClient(handler), false);
        Check(reopened.Get("com.eros.test").Staged, "pending state survives reopen");
        ModManager.Updater.Patcher.Initialize();
        Check(PendingUpdate.Hash(oldDll) == pending.DownloadHash, "startup applies downloaded update");
        Check(!Directory.GetFiles(Path.Combine(Paths.BepInExRootPath, "ModManagerUpdates"), "pending.xml", SearchOption.AllDirectories).Any(), "startup marks completed manifest");

        foreach (string mode in new[] { "current", "older", "missing", "404", "403", "badjson", "prerelease", "badurl", "wrongdigest", "truncated", "wrongdll" })
        {
            handler.Mode = mode;
            var scenario = new UpdateService(new ManualLogSource(), new HttpClient(handler), false);
            bool download = mode == "wrongdigest" || mode == "truncated" || mode == "wrongdll";
            await Run(scenario, download);
            UpdateState status = scenario.Get("com.eros.test");
            if (mode == "current" || mode == "older") Check(status.Current && !status.Available, mode + " release");
            else Check(!status.Current && !status.Available && !status.Staged, mode + " failure is not success");
        }
        Console.WriteLine("Passed " + _checks + " updater checks. Fixtures: " + Root);
    }

    private static async Task Run(UpdateService service, bool install)
    {
        service.Check(new[] { "com.eros.test" }, install);
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (service.Busy && DateTime.UtcNow < deadline) { service.Tick(); await Task.Delay(5); }
        service.Tick();
        Check(!service.Busy, "worker completes");
    }

    private static void MakeDll(string path, string guid, string version)
    {
        using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Test", new Version(version)), "Test", ModuleKind.Dll))
        {
            ModuleDefinition module = assembly.MainModule;
            var type = new TypeDefinition("Test", "Plugin", TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(type);
            var attrType = new TypeReference("BepInEx", "BepInPlugin", module, new AssemblyNameReference("BepInEx", new Version(5, 4, 23, 0)));
            var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attrType) { HasThis = true };
            for (int i = 0; i < 3; i++) ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            var attr = new CustomAttribute(ctor);
            foreach (string value in new[] { guid, "Test", version }) attr.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
            type.CustomAttributes.Add(attr);
            assembly.Write(path);
        }
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        private readonly string _hash;
        internal string Mode = "new";
        internal int Checks;
        internal FakeHttp(byte[] bytes, string hash) { _bytes = bytes; _hash = hash; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri.Host == "api.github.com")
            {
                Checks++;
                if (Mode == "404" || Mode == "403") return Task.FromResult(new HttpResponseMessage((HttpStatusCode)int.Parse(Mode)));
                var json = new JObject
                {
                    ["tag_name"] = Mode == "current" ? "v1.0.0" : Mode == "older" ? "v0.9.0" : "v1.1.0",
                    ["draft"] = false, ["prerelease"] = Mode == "prerelease",
                    ["assets"] = new JArray(new JObject {
                        ["name"] = Mode == "missing" ? "Other.dll" : "Test.dll",
                        ["size"] = _bytes.Length,
                        ["digest"] = "sha256:" + (Mode == "wrongdigest" ? new string('0', 64) : _hash),
                        ["browser_download_url"] = Mode == "badurl" ? "https://example.com/Test.dll" : "https://github.com/test/repo/releases/download/v1.1.0/Test.dll"
                    })
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Mode == "badjson" ? "bad" : json.ToString()) });
            }
            byte[] bytes = Mode == "truncated" ? _bytes.Take(30).ToArray() : Mode == "wrongdll" ? new byte[_bytes.Length] : _bytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
