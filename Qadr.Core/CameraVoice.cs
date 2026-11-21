namespace Qadr.Core;

/// <summary>Numbered commands deliberately avoid ambiguous device names.</summary>
public static class CameraVoice
{
    static readonly string[] Numbers=["one","two","three","four","five","six","seven","eight","nine","ten","eleven","twelve","thirteen","fourteen","fifteen","sixteen","seventeen","eighteen","nineteen","twenty"];
    public static string Number(int index)=>index>=0&&index<Numbers.Length?Numbers[index]:(index+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static string[] Phrases(int count)=>Enumerable.Range(0,Math.Clamp(count,0,Numbers.Length)).SelectMany(i=>new[]{"camera "+Number(i),"choose camera "+Number(i)}).Append("computer stop").ToArray();
    public static int Resolve(string text,int count){
        string normalized=string.Join(' ',text.Trim().ToLowerInvariant().Split(' ',StringSplitOptions.RemoveEmptyEntries));
        if(normalized.StartsWith("choose ",StringComparison.Ordinal))normalized=normalized[7..];
        if(!normalized.StartsWith("camera ",StringComparison.Ordinal))return -1;
        string number=normalized[7..];int index=Array.IndexOf(Numbers,number);
        if(index<0&&int.TryParse(number,out int numeric))index=numeric-1;
        return index>=0&&index<Math.Min(count,Numbers.Length)?index:-1;
    }
}

/// <summary>One startup chain. Emergency stop/failure permanently disarms it
/// for this session; only explicit Resume can subsequently enable control.</summary>
public sealed class StartupFlow
{
    readonly bool enabled;
    public StartupFlow(bool enabled){this.enabled=enabled;Pending=enabled;}
    public void Begin()=>Pending=enabled;
    public bool Pending {get;private set;}
    public void Cancel()=>Pending=false;
    public bool Advance()=>Pending;
    public void Started()=>Pending=false;
}
