using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace SealStudio {
public static class SelfTest {
 static List<string> lines=new List<string>();
 static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);lines.Add("PASS: "+name);}
 static byte[] Bytes(BitmapSource b){var p=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(p,b.PixelWidth*4,0);return p;}
 public static int Run(string dir){
  Directory.CreateDirectory(dir);var fonts=Studio.Discover(AppDomain.CurrentDomain.BaseDirectory);Check(fonts.Count>0,"system font discovery");
  var f=fonts.FirstOrDefault(x=>x.Name=="楷体")??fonts.First(x=>x.Glyph.CharacterToGlyphMap.ContainsKey(0x738B));
  var s=new SealSettings{Text="王小明AB123",WidthCm=3,HeightCm=4,Columns=2,Border=false,Dpi=600};SealCore.Validate(s,f);
  Check(SealCore.Pixels(3,600)==709&&SealCore.Pixels(4,600)==945,"cm to pixels: 3x4cm at 600DPI = 709x945");
  var mask=SealCore.Mask(s,f,709,945);var a=Bytes(SealCore.Map(mask,s,false,false));var b=Bytes(SealCore.Map(mask,s,false,true));
  bool mirror=true,different=false,binary=true;
  for(int y=0;y<945;y++)for(int x=0;x<709;x++){int i=(y*709+x)*4,j=(y*709+708-x)*4;if(a[i]!=b[j])mirror=false;if(a[i]!=b[i])different=true;if(a[i]!=0&&a[i]!=255)binary=false;}
  Check(mirror&&different,"horizontal mirror pixel equality and asymmetric content");Check(binary,"pure black-white engraving");
  s.BlackRemoves=false;var inv=Bytes(SealCore.Map(mask,s,false,false));Check(a.Where((v,i)=>i%4!=3).Select((v,i)=>v).Count()>0,"image nonempty");
  bool opposite=true;for(int i=0;i<a.Length;i+=4)if(a[i]+inv[i]!=255)opposite=false;Check(opposite,"black-removes / black-retains inverse");
  s.BlackRemoves=true;s.Mode=1;var yin=Bytes(SealCore.Map(SealCore.Mask(s,f,709,945),s,false,false));opposite=true;for(int i=0;i<a.Length;i+=4)if(a[i]+yin[i]!=255)opposite=false;Check(opposite,"yin and yang complement without border");
  var paper1=Bytes(SealCore.Map(mask,s,true,false));s.Mirror=false;var paper2=Bytes(SealCore.Map(mask,s,true,false));Check(paper1.SequenceEqual(paper2),"paper preview independent of engraving mirror");
  for(int layout=0;layout<4;layout++){s.Layout=layout;s.Text="王小\n明印";var image=SealCore.Mask(s,f,320,480);Check(image.PixelWidth==320&&image.PixelHeight==480,"layout "+layout+" renders rectangular seal");}
  s.Text="春风得意";s.Mode=0;s.Layout=0;s.Mirror=true;s.Border=true;s.WidthCm=3;s.HeightCm=3;s.OutputDir=dir;
  string export=SealCore.Export(s,f,true);var file=Directory.GetFiles(export,"雕刻图*.png").Single();BitmapFrame frame;
  using(var st=File.OpenRead(file))frame=BitmapFrame.Create(st,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
  Check(frame.PixelWidth==709&&frame.PixelHeight==709,"export exact dimensions");Check(Math.Abs(frame.DpiX-600)<.1&&Math.Abs(frame.DpiY-600)<.1,"PNG DPI metadata round trip");
  Check(Directory.GetFiles(export).Length==4,"export contains PNG BMP paper reference and instructions");Check(SealCore.Export(s,f,false)!=export,"unique export names");
  var latin=fonts.First(x=>x.Name=="Arial");Check(SealCore.Missing(s,latin).Length>0,"missing Chinese glyph detection without fallback");
  bool blocked=false;try{SealCore.Validate(s,latin);}catch(ArgumentException){blocked=true;}Check(blocked,"missing glyph blocks output");
  s.WidthCm=20;s.HeightCm=20;s.Dpi=1200;blocked=false;try{SealCore.Validate(s,f);}catch(ArgumentException){blocked=true;}Check(blocked,"oversized image memory guard");
  s.WidthCm=.3;s.HeightCm=.3;s.Dpi=600;blocked=false;try{SealCore.Validate(s,f);}catch(ArgumentException){blocked=true;}Check(blocked,"invalid border and margin blocked");
  MirrorTests.Run(dir,f,lines);File.WriteAllLines(Path.Combine(dir,"test-report.txt"),lines);return 0;
 }
}
}
