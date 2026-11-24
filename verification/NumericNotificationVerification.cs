using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using HowToFishTrainer.App;

static class NumericNotificationVerification
{
    static readonly List<string> Passed=new(),Failed=new();
    static void Check(bool ok,string label) => (ok?Passed:Failed).Add(label);
    [STAThread] static int Main(string[] args)
    {
        var n=new NumericInput("test",3,1,5); n.Text="99";
        Check(n.Value==3&&n.LastValidValue==3&&n.HasError,"Invalid typed draft keeps prior numeric value");
        int valueEvents=0,textEvents=0;
        n.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NumericInput.Value))valueEvents++;if(e.PropertyName==nameof(NumericInput.Text))textEvents++;};
        n.Value=99;
        Check(n.Value==99&&n.LastValidValue==3&&n.Text=="99"&&n.HasError,"Programmatic invalid value retains the existing draft contract");
        Check(valueEvents==1&&textEvents==0,"Same invalid draft with changed Value notifies Value bindings once without rewriting text");
        n.Value=99;
        Check(valueEvents==1&&textEvents==0,"Repeating identical programmatic value does not send another notification");
        n.Value=4;
        Check(n.Value==4&&n.LastValidValue==4&&!n.HasError&&valueEvents==2&&textEvents==1,"Valid programmatic update notifies bindings and restores valid state");
        var model=new NumericInput("binding",3,1,5); model.Text="99";
        var slider=new Slider { Minimum=1,Maximum=5 };
        BindingOperations.SetBinding(slider,Slider.ValueProperty,new Binding(nameof(NumericInput.Value)){Source=model,Mode=BindingMode.OneWay});
        Check(slider.Value==3,"Slider binding starts from retained typed-draft value");
        model.Value=99;
        Check(slider.Value==5&&model.Value==99&&model.Text=="99"&&model.HasError,"Slider binding refreshes without rewriting the invalid numeric draft");
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            n.Text = "1,25";
            Check(!n.HasError && n.Value == 1.25 && n.Text == "1,25", "Local decimal separator remains editable and is applied as a fraction");
            n.Text = "2.75";
            Check(!n.HasError && n.Value == 2.75, "Existing dot-decimal input keeps its value under a comma-decimal culture");
            n.Text = "1.234,5";
            Check(n.HasError && n.LastValidValue == 2.75, "Ambiguous grouping does not replace the last valid number");
            Check(FeatureItemViewModel.TrySpinSpeed("29,75", out double speed) && speed == 29.75
                && FeatureItemViewModel.TrySpinSpeed("2", out _) && FeatureItemViewModel.TrySpinSpeed("30", out _)
                && !FeatureItemViewModel.TrySpinSpeed("1,99", out _) && !FeatureItemViewModel.TrySpinSpeed("30,01", out _),
                "Spin speed accepts localized decimals and both endpoints of 2–30 revolutions per second");
            using var journal = new FishingViewModel(gameRunning: () => false, startTimer: false);
            journal.BeginSession();
            journal.SpeciesText = "Test fish";
            journal.WeightText = "2,75";
            journal.AddCatch();
            Check(journal.WeightError == "" && journal.Catches.Count == 1 && journal.Catches[0].WeightKg == 2.75,
                "The journal accepts and records the same localized weight displayed by its editor");
            var quantity = new NumericInput("count", 2, 1, 10, true) { Text = "2,5" };
            Check(quantity.HasError && quantity.Value == 2, "An integer-only field rejects a localized fractional count");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            n.Text = "1,250";
            Check(n.HasError && n.LastValidValue == 2.75, "Thousands separators cannot accidentally change the applied multiplier");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        var folder=Path.Combine(args[0],"verification/numeric-notification-review-20261030",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var dll=typeof(NumericInput).Assembly.Location;
        File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new {passed=Failed.Count==0,checks=Passed.Count+Failed.Count,passedChecks=Passed,failedChecks=Failed,
            assemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),gameInjected=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var failure in Failed)Console.WriteLine("FAIL: "+failure);
        Console.WriteLine($"Numeric notification: {Passed.Count}/{Passed.Count+Failed.Count}; {folder}");
        return Failed.Count==0?0:1;
    }
}
