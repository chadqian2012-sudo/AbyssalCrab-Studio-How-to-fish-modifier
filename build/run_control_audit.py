from pathlib import Path
import subprocess,json,shutil
root=Path(__file__).resolve().parents[1];base=root.parent/'howtofish-local-test'
sdk=Path(json.loads((base/'build-tools/sdk-download.json').read_text(encoding='utf-8-sig'))['sdk_directory'])
net=sdk/'shared/Microsoft.NETCore.App/8.0.31';desktop=sdk/'shared/Microsoft.WindowsDesktop.App/8.0.31'
out=root/'verification/control-audit-bin';out.mkdir(exist_ok=True)
source=(base/'build/verify/ControlAudit.cs').read_text(encoding='utf-8-sig')
source=source.replace('window.Left = -4000;', '((DispatcherTimer)window.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!).Stop();\n        window.Left = -4000;')
source=source.replace('window.Top = -4000;', 'window.Top = -4000;\n        ((HowToFishTrainer.App.MainViewModel)window.DataContext).Connect(new CatalogReplay(Path.Combine(assemblyDir, "..", "..", "live-read-results.json")), HowToFishTrainer.Core.CapabilityMatrix.For(HowToFishTrainer.Core.SessionMode.Host, true));')
source+='''
internal sealed class CatalogReplay : HowToFishTrainer.Core.IGameRuntimeClient
{
    private readonly System.Collections.Generic.Dictionary<string,System.Text.Json.JsonElement> _responses = new();
    public CatalogReplay(string path)
    {
        using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
        foreach(var item in document.RootElement.GetProperty("commands").EnumerateArray())
            if(item.TryGetProperty("applied",out var applied)&&applied.ValueKind!=System.Text.Json.JsonValueKind.Null)
                _responses[item.GetProperty("id").GetString()!] = applied.Clone();
    }
    public System.Threading.Tasks.ValueTask<HowToFishTrainer.Core.RuntimeCommandResult> ExecuteAsync(HowToFishTrainer.Core.FeatureCommand command,System.Threading.CancellationToken cancellationToken)
    {
        var data=_responses.TryGetValue(command.FeatureId,out var value)?value:System.Text.Json.JsonSerializer.SerializeToElement(new{});
        return System.Threading.Tasks.ValueTask.FromResult(new HowToFishTrainer.Core.RuntimeCommandResult(true,"Replay only; no game writes",data,data,false));
    }
}
'''
# A selected caption must render, not merely avoid a record dump.
source=source.replace('bool looksLikeRecord = rendered.Contains(\'{\') && rendered.Contains(" = ");','bool looksLikeRecord = (rendered.Contains(\'{\') && rendered.Contains(" = ")) || rendered.StartsWith("<no ");')
source=source.replace('Console.WriteLine($"checkBoxProblems={problems} comboBoxProblems={comboBad}");','Console.WriteLine($"checkBoxProblems={problems} comboBoxProblems={comboBad}");\n        File.WriteAllText(Path.Combine(outDir,"control-results.json"), System.Text.Json.JsonSerializer.Serialize(new { checkboxes = checkBoxes.Count, checkbox_problems = problems, comboboxes = comboSeen, combobox_problems = comboBad, combo_failures = comboFailures, passed = problems == 0 && comboBad == 0 }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));')
path=root/'verification/ControlAudit.cs';path.write_text(source,encoding='utf-8')
refs=[p for p in net.glob('System*.dll') if not p.name.endswith('.Native.dll')]+[net/'mscorlib.dll',net/'netstandard.dll']+[desktop/n for n in ['PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','System.Xaml.dll']]
refs += [root/'verification/assemblies'/name for name in ['HowToFishTrainer.dll','HowToFishTrainer.Core.dll','HowToFishTrainer.Memory.dll']]
opts=['/nologo','/target:exe','/langversion:12','/nullable:enable',f'/out:"{out/"ControlAudit.dll"}"']+[f'/reference:"{p}"' for p in refs]+[f'"{path}"',f'"{base/"build/verify/FakeLicenseClient.cs"}"']
rsp=out/'control.rsp';rsp.write_text('\n'.join(opts),encoding='utf-8-sig')
for path in (root/'verification/assemblies').glob('HowToFishTrainer*.dll'):shutil.copy2(path,out/path.name)
(out/'ControlAudit.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.WindowsDesktop.App','version':'8.0.31'}}}),encoding='utf-8')
subprocess.run(['dotnet',str(base/'tools/microsoft.net.compilers.toolset/tasks/netcore/bincore/csc.dll'),'@'+str(rsp)],check=True)
subprocess.run([str(sdk/'dotnet.exe'),str(out/'ControlAudit.dll'),str(root/'verification/assemblies'),str(root/'verification/controls')],check=True)
