using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HowToFishTrainer.App;

[JsonConverter(typeof(AnimalCountSettingsConverter))]
public sealed class AnimalCountSettings : Dictionary<string,int> { }
public sealed class AnimalCountSettingsConverter : JsonConverter<AnimalCountSettings>
{
    public override AnimalCountSettings Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        using var doc=JsonDocument.ParseValue(ref reader);var result=new AnimalCountSettings();
        if(doc.RootElement.ValueKind==JsonValueKind.Object)
            foreach(var p in doc.RootElement.EnumerateObject())
                if(p.Value.ValueKind==JsonValueKind.Number&&p.Value.TryGetInt32(out int count)&&count>=1&&count<=50&&FunEventDefinitions.All.Any(d=>d.HasAnimalCount&&d.Id==p.Name))result[p.Name]=count;
        return result;
    }
    public override void Write(Utf8JsonWriter writer,AnimalCountSettings value,JsonSerializerOptions options)
    {writer.WriteStartObject();foreach(var p in value)writer.WriteNumber(p.Key,p.Value);writer.WriteEndObject();}
}

public sealed class CombatFeedbackOptionsConverter : JsonConverter<CombatFeedbackOptions>
{
    public override CombatFeedbackOptions Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        using var doc=JsonDocument.ParseValue(ref reader);var p=doc.RootElement;var result=new CombatFeedbackOptions();
        if(p.ValueKind!=JsonValueKind.Object)return result;
        bool Flag(string key,bool fallback)=>p.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.True or JsonValueKind.False?v.GetBoolean():fallback;
        double Number(string key,double fallback,double min,double max)=>p.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)&&n>=min&&n<=max?n:fallback;
        result.Streaks=Flag("Streaks",result.Streaks);result.Markers=Flag("Markers",result.Markers);result.HeadSound=Flag("HeadSound",result.HeadSound);
        result.KillSound=Flag("KillSound",result.KillSound);result.Numbers=Flag("Numbers",result.Numbers);result.DynamicCrosshair=Flag("DynamicCrosshair",result.DynamicCrosshair);
        result.ComboWindow=Number("ComboWindow",8,1,30);result.Volume=Number("Volume",.35,0,1);result.Opacity=Number("Opacity",.9,.1,1);result.Scale=Number("Scale",1,.5,2);
        return result;
    }
    public override void Write(Utf8JsonWriter writer,CombatFeedbackOptions value,JsonSerializerOptions options)
    {
        writer.WriteStartObject();writer.WriteBoolean("Streaks",value.Streaks);writer.WriteBoolean("Markers",value.Markers);writer.WriteBoolean("HeadSound",value.HeadSound);
        writer.WriteBoolean("KillSound",value.KillSound);writer.WriteBoolean("Numbers",value.Numbers);writer.WriteBoolean("DynamicCrosshair",value.DynamicCrosshair);
        writer.WriteNumber("ComboWindow",value.ComboWindow);writer.WriteNumber("Volume",value.Volume);writer.WriteNumber("Opacity",value.Opacity);writer.WriteNumber("Scale",value.Scale);writer.WriteEndObject();
    }
}
