using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Input;

namespace HowToFishTrainer.App;

public sealed partial class FeatureItemViewModel
{
    public bool IsMagicBullet => Descriptor.Id == "combat.silent_aim";
    public NumericInput MagicAngleInput { get; } = new("捕获角度（度）", 8, .5, 180);
    public NumericInput MagicDistanceInput { get; } = new("追踪距离（米）", 35, 1, 1000);
    public NumericInput MagicStrengthInput { get; } = new("追踪强度（%）", 100, 0, 100);
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
                _ = ExecuteToggleAsync(true, true, _appliedMagicArguments.HasValue ? TargetingArguments.WithTeammates(_appliedMagicArguments.Value, value) : null);
        }
    }
    public AimAssistTriggerOption MagicTrigger
    {
        get => _magicTrigger ?? MagicTriggerModes[1];
        set { if (value == null || value != MagicTriggerModes[0] && value != MagicTriggerModes[1] || _magicTrigger == value) return; _magicTrigger = value; Changed(); }
    }
    public bool CanApplyMagic => IsMagicBullet && Enabled && CanInteract && !MagicAngleInput.HasError && !MagicDistanceInput.HasError && !MagicStrengthInput.HasError;
    public ICommand ApplyMagicCommand => new RelayCommand(() => { if (CanApplyMagic) _ = ExecuteToggleAsync(true, true); });
    public JsonElement MagicArguments(bool enabled) => enabled ? JsonSerializer.SerializeToElement(new {
        enabled, angle = MagicAngleInput.Value, distance = MagicDistanceInput.Value,
        strength = MagicStrengthInput.Value, trigger = MagicTrigger.Value,
        targetTeammates = MagicTargetTeammates }) : JsonSerializer.SerializeToElement(new { enabled });
}
