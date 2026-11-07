using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;

sealed class ExperienceRuntime : IGameRuntimeClient
{
    public readonly List<FeatureCommand> Commands=new();
    public bool FailClear,FailWave;
    public TaskCompletionSource<RuntimeCommandResult>? ClearPending;
    public TaskCompletionSource<RuntimeCommandResult>? SpinPending,GenerationPending;
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command,CancellationToken token)
    {
        Commands.Add(command);object data=new{applied=true,x=1.25,y=2.5,z=-3.75};bool ok=true;
        if(command.FeatureId=="player.spin.lock"&&SpinPending!=null)return new(SpinPending.Task);
        if(command.FeatureId=="generation.item"&&GenerationPending!=null)return new(GenerationPending.Task);
        if(command.FeatureId=="locks.disable_all") { if(ClearPending!=null)return new(ClearPending.Task);ok=!FailClear; }
        if(command.FeatureId=="catalog.items")data=new{items=new object[]{
            new{id=11,name="Test Rifle",fish=false,kind="枪械",weapon=new{damage=9},attachments=new{sights=new[]{new{id="scope",name="Scope"}},barrels=new[]{new{id="barrel",name="Barrel"}},ammo=new[]{new{id="ammo",name="Ammo"}},extendedMag=true,laser=true},skins=new[]{new{id="blue",name="Blue",rarity="rare"}}},
            new{id=5,name="Apple",fish=false,kind="食品"},new{id=21,name="Fish",fish=true,kind="鱼类"}}};
        if(command.FeatureId=="catalog.islands")data=new{islands=new[]{new{index=1,key="island:a",name="A",rawName="A",available=true,runtimeIndex=1,sceneBuildIndex=2}}};
        if(command.FeatureId=="fun.event.catalog")data=new{events=FunEventDefinitions.All.Select(e=>new{id=e.Id,available=true,reason="fixture"}).ToArray(),totalFunEntities=8,currentWaveCount=8};
        if(command.FeatureId=="fun.event.execute"){ok=!FailWave;data=new{registered=8,totalFunEntities=8};}
        if(command.FeatureId=="players.catalog")data=new{players=Array.Empty<object>()};
        if(command.FeatureId=="features.proofs")data=Array.Empty<object>();
        if(command.FeatureId=="casino.status")data=new{available=true,outcomeMode="black",multiplier=2,remainingRounds=0};
        var value=JsonSerializer.SerializeToElement(data);return ValueTask.FromResult(new RuntimeCommandResult(ok,"fixture",value,value,false));
    }
}
sealed class ExperiencePreferenceStore:IUserPreferencesStore
{
    public string Json="{}";public string? Warning=>null;
    public TrainerUserPreferences Load()=>TrainerUserPreferences.Parse(Json);
    public void Save(TrainerUserPreferences p)=>Json=p.ToJson();
}
static class ExperienceVerification
{
    static readonly List<string> Checks=new();
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Checks.Add(message);}
    static void Pump(int ms=0){var watch=Stopwatch.StartNew();do{var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);if(ms>0)Thread.Sleep(3);}while(watch.ElapsedMilliseconds<ms);}
    static void Await(Task task){while(!task.IsCompleted)Pump(5);task.GetAwaiter().GetResult();}
    static IEnumerable<T> Find<T>(DependencyObject node)where T:DependencyObject{if(node is T value)yield return value;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
    static JsonElement Session(int alive=0)=>JsonSerializer.SerializeToElement(new{predictiveAim=new{lockedTarget="Seagull",last=new{state="predicted",speed=100,flightTime=.25,distance=25,leadOffset=new{x=1.25,y=0,z=2.5}},weapon=new{name="Rifle",speed=100},performance=new{trackingMeanUs=12.5,trackingP95UpperUs=25,exceptions=0},seagullWaves=new[]{new{alive}}}});
    static FeatureItemViewModel Feature(MainViewModel vm,string id){var d=FeatureRegistry.Find(id)!;vm.SelectedPage=vm.Pages.Single(p=>p.Category==MainViewModel.DisplayCategory(d.Id,d.Category));return vm.Features.Single(f=>f.Descriptor.Id==id);}
    [STAThread] static int Main(string[] args)
    {
        bool codeOnly=Environment.GetEnvironmentVariable("ABYSS_CODE_ONLY")=="1";
        if(!codeOnly){var app=new App();app.InitializeComponent();}
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root=args[0],folder=Path.Combine(root,"verification","experience-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"experience-settings.json");
        var store=new ExperienceSettingsStore(path);var initial=store.Load();Check(initial.Presets.Count==0,"First launch defaults");
        initial.Presets.Add(new("valid","{}"));store.Save(initial);initial.SpinCurve="脉冲";store.Save(initial);
        Check(File.Exists(path+".bak"),"Atomic store retains prior valid backup");File.WriteAllText(path,"broken");var recovered=new ExperienceSettingsStore(path);Check(recovered.Load().Presets.Count==1&&recovered.Warning!=null,"Corrupt primary recovers backup");recovered.Save(initial);Check(ExperienceSettingsStore.Parse(File.ReadAllText(path+".bak")).Presets.Count==1,"Broken primary never overwrites good backup");
        string future=Path.Combine(folder,"future.json");File.WriteAllText(future,"{\"Version\":3}");var futureStore=new ExperienceSettingsStore(future);futureStore.Load();bool blocked=false;try{futureStore.Save(new());}catch(IOException){blocked=true;}Check(blocked&&File.ReadAllText(future).Contains(":3"),"Future version file is protected");
        var sanitized=ExperienceSettingsStore.Parse("{\"RefillLimit\":99,\"SpinCurve\":\"bad\",\"Presets\":[{\"Name\":\"bad\",\"Parameters\":\"broken\"},{\"Name\":\"good\",\"Parameters\":\"{}\"}]}");Check(sanitized.RefillLimit==3&&sanitized.SpinCurve=="固定"&&sanitized.Presets.Count==1,"Invalid entries fall back without discarding valid preset");

        string livePath=Path.Combine(folder,"live","experience-settings.json");var prefs=new ExperiencePreferenceStore();var vm=new MainViewModel(preferencesStore:prefs,experiencePath:livePath);
        Check(!vm.AutoRefill&&!vm.TrackDeathPosition&&!vm.WidgetVisible,"Startup has no automation or widget");
        var runtime=new ExperienceRuntime();vm.Connect(runtime,CapabilityMatrix.For(SessionMode.Host,false));Pump(15);
        var playerNumber=vm.Features.First(f=>f.Descriptor.Control==FeatureControl.Number);string numberId=playerNumber.Descriptor.Id;double expected=playerNumber.ValueInput.Minimum!.Value+(playerNumber.ValueInput.Maximum!.Value-playerNumber.ValueInput.Minimum.Value)*.375;if(playerNumber.ValueInput.IntegerOnly)expected=Math.Round(expected);playerNumber.ValueInput.Value=expected;
        Check(vm.FeelFeatures.Contains(playerNumber)&&!vm.DisplayFeatures.Contains(playerNumber),"Player numeric features are top panel only");
        var fishNumberPage=vm.Pages.Single(p=>p.Category=="钓鱼");vm.SelectedPage=fishNumberPage;Check(vm.FeelFeatures.Count>0&&vm.DisplayFeatures.All(f=>!vm.FeelFeatures.Contains(f)),"Fishing numeric features are top panel only");
        var item=Feature(vm,"generation.item");item.SelectedOption=item.Options.Single(o=>o.Id==11);item.SelectedSight=item.SelectedOption.Sights.Single();item.SelectedSkin=item.SelectedOption.Skins.Single();item.DistanceInput.Text="4.25";item.QuantityInput.Text="3";item.ExtendedMagazine=true;
        item.ToggleFavoriteCommand.Execute(null);Check(item.IsFavorite,"Favorite keyed by feature and item ID");item.SearchText="Rifle";item.RememberSearchCommand.Execute(null);Check(item.RecentSearches.Contains("Rifle"),"Recent search captured explicitly");item.SearchText="";
        item.CatalogView="收藏";Check(item.FilteredOptions.Count==1&&item.FilteredOptions[0].Id==11,"Favorite filter isolated");item.SelectedOption=item.Options.Single(o=>o.Id==11);item.RestoreBookmarkedConfigurationCommand.Execute(null);Check(item.SelectedSight?.Id=="scope"&&item.SelectedSkin?.Id=="blue"&&item.Distance==4.25,"Favorite restores compatible attachment/skin/numeric IDs");
        int commands=runtime.Commands.Count;item.ExecuteCommand.Execute(null);Pump();Check(runtime.Commands.Skip(commands).Any(c=>c.FeatureId=="generation.item"),"Manual generator still dispatches");item.CatalogView="最近生成";Check(item.FilteredOptions.Single().Id==11,"Successful generation enters recent list");
        vm.AimAssist.Disconnect();vm.AimAssist.HeadLockEnabled=true;vm.AimAssist.LeadEnabled=false;vm.Crosshair.Shape="圆环";vm.SpinCurve="脉冲";vm.RefillLimitInput.Text="4";vm.PresetName="探索方案";vm.SavePresetCommand.Execute(null);var preset=vm.SelectedPreset!;
        Check(!preset.Parameters.Contains("\"enabled\"",StringComparison.OrdinalIgnoreCase)&&preset.Parameters.Contains("scope"),"Preset captures selections, excludes activation state");
        Feature(vm,numberId).ValueInput.Value=Feature(vm,numberId).ValueInput.Minimum!.Value;vm.AimAssist.HeadLockEnabled=false;vm.SpinCurve="固定";vm.SelectedPreset=preset;
        commands=runtime.Commands.Count;Await(vm.ApplySelectedPresetAsync());Check(Feature(vm,numberId).Value==expected,"Preset restores numeric parameter across categories");
        Check(vm.AimAssist.HeadLockEnabled&&!vm.AimAssist.Enabled&&!vm.Crosshair.Enabled&&!vm.AutoRefill,"Preset restores head preference while all toggles remain off");
        Check(runtime.Commands.Skip(commands).All(c=>c.FeatureId is "locks.disable_all" or "players.catalog" or "casino.status" or "fun.event.catalog" or "features.proofs"),"Restore never enables aim/spin, generates, teleports, applies gambling or saves game");
        Check(vm.SpinCurve=="脉冲"&&vm.RefillLimitInput.LastValidValue==4,"Preset includes curve and refill limit, no active refill flag");
        var raceRuntime=new ExperienceRuntime();var raceVm=new MainViewModel(experiencePath:Path.Combine(folder,"race","experience.json"));raceVm.Connect(raceRuntime,CapabilityMatrix.For(SessionMode.Host,false));Pump();raceVm.PresetName="race";raceVm.SavePresetCommand.Execute(null);
        var oldSpin=Feature(raceVm,"player.spin.lock");raceRuntime.SpinPending=new();var pendingSpin=raceRuntime.SpinPending;oldSpin.Enabled=true;Await(raceVm.ApplySelectedPresetAsync());raceRuntime.SpinPending=null;pendingSpin.SetResult(new(true,"old enable",JsonSerializer.SerializeToElement(new{applied=true}),JsonSerializer.SerializeToElement(new{}),false));Pump();Check(!Feature(raceVm,"player.spin.lock").Enabled&&!raceVm.ActiveFeaturesText.Contains("大陀螺"),"Late toggle completion cannot re-enable UI after preset clear");
        oldSpin=Feature(raceVm,"player.spin.lock");raceRuntime.SpinPending=new();pendingSpin=raceRuntime.SpinPending;oldSpin.Enabled=true;raceVm.SelectedPage=raceVm.Pages[1];raceVm.SelectedPage=raceVm.Pages[0];raceRuntime.SpinPending=null;pendingSpin.SetResult(new(true,"enable",JsonSerializer.SerializeToElement(new{applied=true}),JsonSerializer.SerializeToElement(new{}),false));Pump();Check(Feature(raceVm,"player.spin.lock").Enabled,"Ordinary page changes preserve pending toggle state within same action generation");raceVm.MarkAllLocksDisabled("fixture");
        var delayedItem=Feature(raceVm,"generation.item");delayedItem.SelectedOption=delayedItem.Options.Single(o=>o.Id==11);delayedItem.QuantityInput.Text="3";delayedItem.DistanceInput.Text="4.25";raceRuntime.GenerationPending=new();var pendingGeneration=raceRuntime.GenerationPending;delayedItem.ExecuteCommand.Execute(null);delayedItem.QuantityInput.Text="77";delayedItem.SelectedOption=delayedItem.Options.Single(o=>o.Id==5);raceRuntime.GenerationPending=null;pendingGeneration.SetResult(new(true,"generated",JsonSerializer.SerializeToElement(new{}),JsonSerializer.SerializeToElement(new{}),false));Pump();delayedItem.CatalogView="最近生成";Check(delayedItem.FilteredOptions.Single().Id==11,"Recent generation records the invoked item, not a later edited choice");delayedItem.RestoreBookmarkedConfigurationCommand.Execute(null);Check(delayedItem.Quantity==3&&delayedItem.Distance==4.25,"Recent generation stores immutable parameters from submission time");raceVm.CloseExperience();raceVm.ClosePreferences();
        var spin=Feature(vm,"player.spin.lock");spin.SpinSpeedText="7.25";spin.SpinSpeedText="-";vm.PresetName="有效草稿";vm.SavePresetCommand.Execute(null);var validDraft=vm.SelectedPreset!;spin.SpinSpeedText="12";vm.SelectedPreset=validDraft;Await(vm.ApplySelectedPresetAsync());Check(Feature(vm,"player.spin.lock").SpinSpeedText=="7.25","Preset saves last valid spin number instead of incomplete draft");
        vm.FlushPreferences();vm.FlushExperience();
        var restarted=new MainViewModel(preferencesStore:prefs,experiencePath:livePath);Check(restarted.Presets.Count==2&&!restarted.AutoRefill&&!restarted.AimAssist.Enabled,"Restart restores presets without automation");
        restarted.Connect(new ExperienceRuntime(),CapabilityMatrix.For(SessionMode.Host,false));Pump();item=Feature(restarted,"generation.item");Check(item.CatalogView=="最近生成"&&item.FilteredOptions.Single().Id==11,"Generator view and recent entries survive restart/catalog load");item.CatalogView="收藏";item.SelectedOption=item.Options.Single(o=>o.Id==11);Check(item.IsFavorite,"Favorite survives restart");
        vm.SelectedPreset=preset;runtime.ClearPending=new();var transition=vm.ApplySelectedPresetAsync();Check(vm.PresetBusy&&!vm.CanEditParameters,"Preset transition locks editing while runtime clear pending");
        commands=runtime.Commands.Count;Feature(vm,"player.spin.lock").Enabled=true;Pump();Check(runtime.Commands.Count==commands,"Pending preset blocks late generic enable");runtime.ClearPending.SetResult(new(true,"cleared",JsonSerializer.SerializeToElement(new{}),null,false));Await(transition);runtime.ClearPending=null;Check(!vm.PresetBusy&&!vm.AimAssist.Enabled,"Preset barrier releases after clear without enabling");
        runtime.FailClear=true;vm.SelectedPreset=preset;Await(vm.ApplySelectedPresetAsync());Check(vm.ExperienceStatus.StartsWith("未切换预设")&&vm.AimAssist.CanConfigure,"Failed clear is reported and aim remains configurable");runtime.FailClear=false;

        string imported="{\"version\":1,\"points\":[{\"group\":\"测试\",\"name\":\"港口\",\"x\":1.25,\"y\":2.5,\"z\":-3.75}]}";
        Check(vm.ImportTeleportPoints(imported)==1&&vm.ImportTeleportPoints(imported)==0,"Teleport merge imports once and preserves duplicate");vm.SelectedTeleportPoint=vm.TeleportPoints.Single();vm.PointNote="钓鱼港口";vm.PointColor="紫色";vm.PointIcon="⚓";vm.PointFavorite=true;vm.SavePointDetailsCommand.Execute(null);Check(vm.GetPointDecoration(vm.SelectedTeleportPoint!)?.Favorite==true,"Point metadata saved");vm.TeleportPointQuery="钓鱼";Check(vm.FilteredTeleportPoints.Count==1,"Point search includes notes");
        string exported=vm.ExportTeleportPoints();var receiver=new MainViewModel(experiencePath:Path.Combine(folder,"receiver","settings.json"));Check(receiver.ImportTeleportPoints(exported)==1&&receiver.GetPointDecoration(receiver.TeleportPoints.Single())?.Icon=="⚓","Point export/import retains annotation/icon/favorite");
        int count=receiver.TeleportPoints.Count;blocked=false;try{receiver.ImportTeleportPoints("{\"version\":1,\"points\":[{\"group\":\"A\",\"name\":\"B\",\"x\":999999,\"y\":0,\"z\":0}]}");}catch(JsonException){blocked=true;}Check(blocked&&receiver.TeleportPoints.Count==count,"Invalid import is transactional");
        vm.TrackDeathPosition=true;vm.ApplyDeathPositionSample(JsonSerializer.SerializeToElement(new{players=new[]{new{isLocal=true,alive=true,x=10.25,y=2,z=-5}}}));vm.ApplyDeathPositionSample(JsonSerializer.SerializeToElement(new{players=new[]{new{isLocal=true,alive=false,x=99,y=99,z=99}}}));commands=runtime.Commands.Count;vm.SelectDeathPositionCommand.Execute(null);Check(vm.CurrentX==10.25&&vm.CurrentZ==-5&&runtime.Commands.Count==commands,"Death sample restores last live position to editor without teleporting");
        vm.ResetWorldContext();vm.SelectDeathPositionCommand.Execute(null);Check(vm.PositionStatus.Contains("尚未记录")&&!vm.TrackDeathPosition,"World transition clears ephemeral death position and sampler");
        vm.UpdateExperienceSession(Session(4));Check(vm.CombatDiagnostics.Contains("Rifle")&&vm.CombatDiagnostics.Contains("12.5")&&vm.CombatDiagnostics.Contains("1.25"),"Combat diagnostic parses weapon, lead vector and microsecond metrics");
        vm.UpdateExperienceSession(JsonSerializer.SerializeToElement(new{predictiveAim=new{last=new{state="off"},weapon=new{name="Stale Gun",speed=999}},experienceTelemetry=new{available=true,name="Current Gun",speed=57.5}}));Check(vm.CombatDiagnostics.Contains("Current Gun")&&vm.CombatDiagnostics.Contains("57.5")&&!vm.CombatDiagnostics.Contains("Stale Gun"),"Current read-only weapon overrides stale aim sample while aim is off");
        vm.AutoRefill=true;vm.UpdateExperienceSession(Session(4));Check(runtime.Commands.Count(c=>c.FeatureId=="fun.event.execute")==0,"Live seagulls suppress automatic spawn");vm.UpdateExperienceSession(Session(0));Pump();Check(vm.AutoRefillCount==1&&runtime.Commands.Last(c=>c.FeatureId=="fun.event.execute").Arguments.GetProperty("eventId").GetString()=="seagull-migration","Empty seagulls execute existing wave once");vm.UpdateExperienceSession(Session(0));Pump();Check(vm.AutoRefillCount==1,"Refill cooldown prevents stale snapshot burst");vm.FunEventsPage.CleanupAllCommand.Execute(null);Pump();Check(!vm.AutoRefill,"Cleanup stops automatic refill first");
        vm.AutoRefill=true;vm.UpdateExperienceSession(JsonSerializer.SerializeToElement(new{predictiveAim=new{}}));Check(!vm.AutoRefill&&vm.AutoRefillStatus.Contains("缺少"),"Missing live count stops refill instead of guessing");
        runtime.FailWave=true;vm.AutoRefill=true;vm.UpdateExperienceSession(Session(0));Pump();Check(!vm.AutoRefill&&vm.AutoRefillStatus.Contains("未成功"),"Failed wave stops automatic retries");runtime.FailWave=false;
        vm.AutoRefill=true;vm.UpdateExperienceSession(null);Check(!vm.AutoRefill,"Missing entire session disarms refill");vm.AutoRefill=true;vm.UpdateExperienceSession(JsonSerializer.SerializeToElement(new{}));Check(!vm.AutoRefill,"Older runtime without diagnostic section disarms refill");
        vm.AutoRefill=true;vm.TrackDeathPosition=true;vm.Connect(runtime,CapabilityMatrix.For(SessionMode.Host,false));Pump();Check(!vm.AutoRefill&&!vm.TrackDeathPosition,"Direct capability/session reconnection disarms automation even when scene handle is unchanged");
        vm.RefillLimitInput.Value=1;vm.AutoRefill=true;vm.UpdateExperienceSession(Session(0));Pump();Check(vm.AutoRefillCount==1,"Refill records one confirmed wave");
        typeof(MainViewModel).GetField("_lastRefill",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(vm,DateTimeOffset.MinValue);commands=runtime.Commands.Count;vm.UpdateExperienceSession(Session(0));Pump();Check(!vm.AutoRefill&&vm.AutoRefillCount==1&&!runtime.Commands.Skip(commands).Any(c=>c.FeatureId=="fun.event.execute"),"Wave limit preserves count and blocks further generation");
        vm.SelectedTeleportPoint=vm.TeleportPoints.Single();vm.CurrentXInput.Text="17.5";commands=runtime.Commands.Count;vm.TeleportSelectedCommand.Execute(null);Pump();Check(runtime.Commands.Skip(commands).Last(c=>c.FeatureId=="world.teleport").Arguments.GetProperty("x").GetDouble()==17.5,"Selected point supplies initial coordinates; edited coordinates control actual teleport");
        vm.CurrentXInput.Text="-";commands=runtime.Commands.Count;vm.TeleportSelectedCommand.Execute(null);Pump();Check(runtime.Commands.Count==commands,"Invalid edited coordinate blocks teleport even with saved point selected");
        foreach(string curve in new[]{"固定","缓慢加速","脉冲"})foreach(double speed in new[]{2,4,7.25,15})foreach(double second in new[]{0,.5,2,5,10,50})Check(MainViewModel.SpinCurveValue(curve,speed,second)>=2&&MainViewModel.SpinCurveValue(curve,speed,second)<=15,$"Bounded spin curve {curve}/{speed}/{second}");
        var curvedSpin=Feature(vm,"player.spin.lock");curvedSpin.SpinSpeedText="4";curvedSpin.SpinAutoJump=true;curvedSpin.Enabled=true;Pump();
        vm.SelectedPage=vm.Pages.Single(p=>p.Category=="钓鱼");vm.SpinCurve="脉冲";Await(vm.TickExperienceAsync(DateTimeOffset.UtcNow));
        Check(runtime.Commands.Last(c=>c.FeatureId=="player.spin.lock").Arguments.GetProperty("autoJump").GetBoolean(),"Spin curve preserves applied cosmetic jump while another category is selected");
        vm.MarkAllLocksDisabled("fixture curve cleanup");vm.SpinCurve="固定";
        vm.SelectedPage=vm.Pages.Single(p=>p.Category=="战斗");vm.DiagnosticOnly=true;commands=runtime.Commands.Count;vm.AimAssist.Enabled=true;var silent=vm.Features.Single(f=>f.Descriptor.Id=="combat.silent_aim");silent.Enabled=true;Pump();Check(!vm.AimAssist.Enabled&&!silent.Enabled&&!runtime.Commands.Skip(commands).Any(c=>c.FeatureId is "combat.silent_aim" or "combat.aim_assist"&&c.Arguments.GetProperty("enabled").GetBoolean()),"Observation mode blocks aim and silent lock activation");
        vm.FeatureQuery="大陀螺";Check(vm.SearchResults.Any(h=>h.Id=="player.spin.lock"),"Global search includes feature title");vm.SelectedSearchHit=vm.SearchResults.First();Check(vm.SelectedPage.Category=="玩家","Search navigates existing category");

        if(codeOnly)
        {
            foreach(var model in new[]{vm,restarted,receiver,raceVm}){model.Disconnect();model.CloseExperience();model.ClosePreferences();}
            var digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(root,"verification","experience-code-results.json"),JsonSerializer.Serialize(new{passed=true,checks=Checks.Count,assembly_sha256=digest,isolated_storage=folder,no_game_injection=true,codeOnly=true,uiVerified=false,details=Checks},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"Experience code-only: {Checks.Count} checks passed; no windows created");return 0;
        }
        var window=new MainWindow{ShowActivated=false,ShowInTaskbar=false,Left=-20000,Top=-20000,IsHitTestVisible=false};window.DataContext=vm;window.Show();Pump(100);window.UpdateLayout();
        var rendering=new List<object>();Directory.CreateDirectory(Path.Combine(root,"verification","experience-shots"));
        foreach(var size in new[]{new Size(1180,720),new Size(1460,900)}){
            window.Width=size.Width;window.Height=size.Height;
            foreach(string category in new[]{"玩家","钓鱼","战斗","物品生成","世界传送","趣味小功能"}){
                vm.SelectedPage=vm.Pages.Single(p=>p.Category==category);Pump(110);window.UpdateLayout();
                Check(Find<NumericInputBox>(window).Where(e=>e.IsVisible).All(e=>e.ActualWidth>0),$"Visible numeric controls have layout {category}/{size.Width}");
                var bmp=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);bmp.Render((Visual)window.Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));string shot=Path.Combine(root,"verification","experience-shots",$"{category}-{size.Width}.png");using(var stream=File.Create(shot))encoder.Save(stream);rendering.Add(new{category,width=size.Width,shot});
                if(category is "玩家" or "钓鱼"){var feel=Find<FeelPanel>(window).Single(p=>p.IsVisible);var firstCard=Find<ItemsControl>(feel).First();Check(firstCard.ActualHeight>0,$"Feel panel exists at top in corresponding category {category}/{size.Width}");var draft=vm.FeelFeatures.First();draft.ValueInput.Text="-";window.UpdateLayout();Check(draft.ValueInput.Text=="-",$"Slider binding never rewrites invalid draft {category}/{size.Width}");}
            }
        }
        vm.CompactNavigation=true;Pump();window.UpdateLayout();Check(((ListBox)window.FindName("TopCategories")).Visibility==Visibility.Collapsed,"Navigation collapses without changing current category");vm.CompactNavigation=false;Pump();Check(((ListBox)window.FindName("TopCategories")).IsVisible,"Navigation expands again");
        vm.SelectedPage=vm.Pages.Single(p=>p.Category=="世界传送");vm.SelectedTeleportPoint=vm.TeleportPoints.Single();window.UpdateLayout();vm.CurrentXInput.Text="-";vm.PointSort="名称";window.UpdateLayout();Check(vm.SelectedTeleportPoint!=null&&vm.CurrentXInput.Text=="-","Bound teleport sort preserves selected point and unfinished coordinate draft");
        item=Feature(vm,"generation.item");window.UpdateLayout();item.SearchText="brand new search";window.UpdateLayout();Check(item.SearchText=="brand new search","Recent-search selector cannot clear newly typed search");
        foreach(var expander in Find<Expander>(window).Where(e=>e.IsVisible&&e.Header is DockPanel))expander.IsExpanded=true;Pump(300);window.UpdateLayout();Check(Find<TextBox>(window).Any(t=>t.IsVisible&&t.Text==vm.PresetName),"Expanded preset manager exposes editable name");
        var widget=new StatusWidget(vm,window,()=>null){Left=-20000,Top=-20000};Check(widget.Content is Border&&widget.ShowActivated==false&&widget.ShowInTaskbar==false,"Widget uses themed, nonactivating independent window");vm.WidgetVisible=true;widget.Show();widget.Close();Check(!vm.WidgetVisible&&!widget.IsVisible,"Closing widget hides it and updates checkbox without destroying reusable window");widget.Show();Check(widget.IsVisible,"Widget can reopen after close");widget.Dispose();
        window.Close();vm.CloseExperience();vm.ClosePreferences();restarted.CloseExperience();restarted.ClosePreferences();receiver.CloseExperience();receiver.ClosePreferences();
        var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant();File.WriteAllText(Path.Combine(root,"verification","experience-results.json"),JsonSerializer.Serialize(new{passed=true,checks=Checks.Count,assembly_sha256=hash,isolated_storage=folder,no_game_injection=true,rendering,details=Checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Experience: {Checks.Count} checks passed; isolated files, no game injection");return 0;
    }
}
