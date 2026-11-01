using System.Globalization;
using System.Text.Json.Serialization;

namespace RoweRC;

public sealed record CarDamage(string Run, long Revision, string Mesh, string Dents)
{
    [JsonIgnore] public bool Valid => Run is {Length:>0 and <=64} && Run.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')
        && Revision>=0 && Mesh is {Length:>0 and <240} && CarAppearance.Asset(Mesh)
        && (Mesh.StartsWith("/Game/Vehicle/Bodies/",StringComparison.Ordinal)
            || Mesh.StartsWith("/Game/Vehicle/RoweRC/Shells/",StringComparison.Ordinal)) && ValidDents(Dents);
    public static bool ValidDents(string? text)
    {
        if(text==null||text.Length>180)return false;
        if(text=="")return true;
        var marks=text.Split(';');if(marks.Length>4)return false;
        foreach(var mark in marks)
        {
            var p=mark.Split(':');if(p.Length!=3)return false;
            var v=new double[3];for(var i=0;i<3;i++)if(!double.TryParse(p[i],NumberStyles.Float,CultureInfo.InvariantCulture,out v[i])||!double.IsFinite(v[i]))return false;
            var n=v[0]*v[0]+v[1]*v[1];if(n<.9||n>1.1||Math.Abs(v[0])>1.001||Math.Abs(v[1])>1.001||v[2] is <=0 or >.8)return false;
        }
        return true;
    }
    public static CarDamage? Read(string text)
    {
        var p=text.TrimEnd('\r','\n').Split('\t');
        if(p.Length!=5||p[0]!="RRD1"||!long.TryParse(p[2],out var rev))return null;
        var value=new CarDamage(p[1],rev,p[3],p[4]);return value.Valid?value:null;
    }
}
