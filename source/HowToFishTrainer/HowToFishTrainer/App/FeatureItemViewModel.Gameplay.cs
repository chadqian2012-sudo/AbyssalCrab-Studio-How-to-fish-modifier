using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class FeatureItemViewModel
{
    private async Task ClearWorldDropsAsync(IGameRuntimeClient runtime, CapabilitySet capabilities)
    {
        var engine = new FeatureEngine(runtime);
        string? operationId = null;
        int previousRemaining = int.MaxValue;
        string confirmed = "";
        while (true)
        {
            if (_executionBlockReason?.Invoke() is string reason)
            {
                StateLabel = "已停止";
                Status = confirmed + reason;
                return;
            }
            var arguments = operationId == null ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(new { operationId });
            OperationReceipt receipt;
            try
            {
                receipt = await engine.ExecuteAsync(new FeatureCommand(Descriptor.Id, arguments, Descriptor.RequiredCapability), capabilities, CancellationToken.None);
            }
            catch (Exception error) when (operationId != null)
            {
                StateLabel = "清理中断";
                Status = confirmed + "本批结果未确认。" + error.Message;
                return;
            }
            if (receipt.Status != ReceiptStatus.Succeeded)
            {
                StateLabel = "清理中断";
                Status = confirmed + receipt.Message;
                return;
            }
            var result = receipt.ReadBack ?? throw new InvalidDataException("游戏没有返回清理结果");
            var id = result.GetProperty("operationId").GetString();
            int remaining = result.GetProperty("remaining").GetInt32();
            if (!Guid.TryParseExact(id, "N", out _) || (operationId != null && operationId != id) ||
                remaining < 0 || remaining >= previousRemaining)
                throw new InvalidDataException("游戏返回的清理批次无效，已停止");
            operationId = id;
            previousRemaining = remaining;
            int removed = result.GetProperty("removed").GetInt32();
            int skipped = result.GetProperty("skipped").GetInt32();
            int failed = result.GetProperty("failed").GetInt32();
            bool retainedLive = result.TryGetProperty("summonTrackingAvailable", out var tracking) && tracking.ValueKind == JsonValueKind.False;
            confirmed = $"已清理 {removed} 个，跳过 {skipped} 个，失败 {failed} 个";
            if (retainedLive) confirmed += "。召唤来源不可核实，已保留活体";
            Status = remaining == 0 ? confirmed : $"{confirmed}，待处理 {remaining} 个";
            if (remaining == 0)
            {
                IsSuccessful = failed == 0 && !retainedLive;
                StateLabel = failed != 0 ? "部分完成" : retainedLive ? "仅清理掉落物" : "已完成";
                return;
            }
            confirmed += "。";
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    public bool IsMagicBullet => Descriptor.Id == "combat.silent_aim";
    public NumericInput EntityOverlayDistanceInput { get; }
    public NumericInput MagicAngleInput { get; } = new("捕获角度（度）", 8, .5, 180);
    public NumericInput MagicDistanceInput { get; } = new("追踪距离（米）", 35, 1, 1000);
    public NumericInput MagicStrengthInput { get; } = new("追踪强度（%）", 100, 0, 100);
    public NumericInput MagicSplashRadiusInput { get; } = new("溅射半径（米）", 2, .1, 20);
    public NumericInput MagicWidthRadiusInput { get; } = new("弹道半径（米）", .5, .05, 5);
    private bool _magicSplash, _magicWide, _magicLethal;
    public bool MagicSplash { get => _magicSplash; set { if(_magicSplash==value)return; _magicSplash=value; Changed(); NotifyStateChanged(); } }
    public bool MagicWide { get => _magicWide; set { if(_magicWide==value)return; _magicWide=value; Changed(); NotifyStateChanged(); } }
    public bool MagicLethal { get => _magicLethal; set { if(_magicLethal==value)return; _magicLethal=value; Changed(); NotifyStateChanged(); } }
    public IReadOnlyList<AimAssistTriggerOption> MagicTriggerModes { get; } = new[] {
        new AimAssistTriggerOption("ads", "仅开镜射击时追踪", "以开枪时是否开镜为准"), new AimAssistTriggerOption("fire", "所有射击均追踪", "开镜与不开镜的射击均追踪") };
    private AimAssistTriggerOption? _magicTrigger;
    private JsonElement? _appliedMagicArguments;
    private bool _magicTargetTeammates;
    public bool MagicTargetTeammates
    {
        get => _magicTargetTeammates;
        set
        {
            if (_magicTargetTeammates == value) return;
            _magicTargetTeammates = value;
            Changed();
            NotifyStateChanged();
            if (!_restoringParameters && Enabled && CanInteract)
                _ = ExecuteToggleAsync(true, true, _appliedMagicArguments.HasValue ? TargetingArguments.WithTeammates(_appliedMagicArguments.Value, value, EntityOverlayDistanceInput.LastValidValue) : null);
        }
    }
    public AimAssistTriggerOption MagicTrigger
    {
        get => _magicTrigger ?? MagicTriggerModes[1];
        set { if (value == null || value != MagicTriggerModes[0] && value != MagicTriggerModes[1] || _magicTrigger == value) return; _magicTrigger = value; Changed(); }
    }
    public bool CanApplyMagic => IsMagicBullet && Enabled && CanInteract && !MagicAngleInput.HasError && !MagicDistanceInput.HasError && !MagicStrengthInput.HasError && !MagicSplashRadiusInput.HasError && !MagicWidthRadiusInput.HasError;
    public ICommand ApplyMagicCommand => new RelayCommand(() => { if (CanApplyMagic) _ = ExecuteToggleAsync(true, true); });
    public JsonElement MagicArguments(bool enabled) => enabled ? JsonSerializer.SerializeToElement(new {
        enabled, angle = MagicAngleInput.Value, distance = MagicDistanceInput.Value,
        strength = MagicStrengthInput.Value, trigger = MagicTrigger.Value,
        splash = MagicSplash, wide = MagicWide, lethal = MagicLethal, splashRadius = MagicSplashRadiusInput.Value, widthRadius = MagicWidthRadiusInput.Value,
        targetTeammates = MagicTargetTeammates, entityOverlayDistance = EntityOverlayDistanceInput.LastValidValue })
        : JsonSerializer.SerializeToElement(new { enabled, entityOverlayDistance = EntityOverlayDistanceInput.LastValidValue });
}
