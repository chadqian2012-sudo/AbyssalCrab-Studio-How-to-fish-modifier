using System;
using System.Collections.Generic;
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
        var folder=Path.Combine(args[0],"verification/numeric-notification-review-20261030",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var dll=typeof(NumericInput).Assembly.Location;
        File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new {passed=Failed.Count==0,checks=Passed.Count+Failed.Count,passedChecks=Passed,failedChecks=Failed,
            assemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),gameInjected=false},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var failure in Failed)Console.WriteLine("FAIL: "+failure);
        Console.WriteLine($"Numeric notification: {Passed.Count}/{Passed.Count+Failed.Count}; {folder}");
        return Failed.Count==0?0:1;
    }
}
