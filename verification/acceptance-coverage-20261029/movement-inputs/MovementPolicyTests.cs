using System;
using System.IO;
using System.Reflection;

using AbyssalCrab.Runtime;
using UnityEngine;
static class MovementPolicyTests {
  static int n; static void Check(bool b,string name) { if(!b) throw new Exception(name); n++; }
  public static void Main() {
    var slope=new Vector3(-0.6f,0.8f,0f); var flat=new Vector3(0f,1f,0f);
    var fast=new Vector3(40f,3f,0f); var uphill=MovementSafetyRuntime.LimitUphill(fast,slope,12f);
    Check(Math.Abs(uphill.x-12f)<.001f && uphill.y==3f,"uphill cap and preserve vertical velocity");
    var downhill=MovementSafetyRuntime.LimitUphill(new Vector3(-40f,3f,0f),slope,12f); Check(downhill.x==-40f && downhill.y==3f,"downhill unchanged");
    var across=MovementSafetyRuntime.LimitUphill(new Vector3(0f,3f,40f),slope,12f); Check(across.z==40f,"across slope unchanged");
    var level=MovementSafetyRuntime.LimitUphill(fast,flat,12f); Check(level.x==40f,"flat unchanged");
    var slow=MovementSafetyRuntime.LimitUphill(new Vector3(5f,7f,0f),slope,12f); Check(slow.x==5f && slow.y==7f,"moderate speed and jump velocity unchanged");
    var diagonal=MovementSafetyRuntime.LimitUphill(new Vector3(30f,-4f,40f),slope,12f); Check(Math.Abs(Math.Sqrt(diagonal.x*diagonal.x+diagonal.z*diagonal.z)-12f)<.001f && diagonal.y==-4f,"diagonal cap preserves fall velocity");
    Console.WriteLine("Movement policy: "+n+" checks passed");
  }
}
