using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HowToFishTrainer.Core;
namespace HowToFishTrainer.App;
public sealed class PointDecorationConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type type,object parameter,CultureInfo culture)
    {
        if(values.Length<3 || values[0] is not TrainerTeleportPoint point || values[2] is not MainViewModel vm)return parameter as string=="brush"?Brushes.Transparent:"";
        var meta=vm.GetPointDecoration(point);
        if(parameter as string=="brush")return meta?.Color switch{"蓝色"=>Brushes.SteelBlue,"紫色"=>Brushes.MediumPurple,"金色"=>Brushes.DarkGoldenrod,"绿色"=>Brushes.SeaGreen,_=>Brushes.Teal};
        if(parameter as string=="note")return string.IsNullOrWhiteSpace(meta?.Note)?point.Group:point.Group+" · "+meta.Note;
        return (meta?.Favorite==true?"★ ":(meta?.Icon??"⌖")+" ")+point.Name;
    }
    public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
