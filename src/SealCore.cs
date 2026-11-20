using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SealStudio {
public enum ReversalMode { None=0, Whole=1, Each=2, Selected=3 }
public sealed class SealSettings {
 public string Text="春风得意";
 public double WidthCm=3, HeightCm=3, Dpi=600, MarginCm=.18, BorderCm=.06, SpacingPercent=6, SizePercent=92;
 public int Columns=2, Layout=0, Mode=0;
 public ReversalMode Reversal=ReversalMode.Whole;
 public HashSet<int> ReversedCharacters=new HashSet<int>();
 // Compatibility for callers and settings from the first release.
 public bool Mirror { get { return Reversal==ReversalMode.Whole; } set { Reversal=value?ReversalMode.Whole:ReversalMode.None; } }
 public bool BlackRemoves=true, Border=true, Stretch=true;
 public string OutputDir=@"D:\印章2\生成图片";
}
public sealed class FontEntry {
 public string Name; public Typeface Face; public GlyphTypeface Glyph; public bool Imported;
 public override string ToString(){return Name+(Imported?"  [导入]":"");}
}
public static class SealCore {
 public const int MaxPixels=16000000;
 public static int Pixels(double cm,double dpi) {return (int)Math.Round(cm/2.54*dpi,MidpointRounding.AwayFromZero);}
 public static List<int> Codepoints(string s) {
  var r=new List<int>(); for(int i=0;i<s.Length;i++){if(char.IsHighSurrogate(s[i])&&i+1<s.Length&&char.IsLowSurrogate(s[i+1])){r.Add(char.ConvertToUtf32(s[i],s[++i]));}else r.Add(s[i]);} return r;
 }
 public static bool White(int cp){return cp<=0xFFFF&&char.IsWhiteSpace((char)cp);}
 public static string ReversalName(SealSettings s){
  switch(s.Reversal){
   case ReversalMode.None:return "不反转";
   case ReversalMode.Whole:return "整章左右反转";
   case ReversalMode.Each:return "每个字单独左右反转";
   case ReversalMode.Selected:return "指定字左右反转";
   default:throw new ArgumentException("请选择有效的反转方式。");
  }
 }
 public static string CharacterNumbers(SealSettings s){return string.Join(",",s.ReversedCharacters.OrderBy(x=>x).Select(x=>x.ToString(CultureInfo.InvariantCulture)).ToArray());}
 public static string ReversalHelp(SealSettings s){
  switch(s.Reversal){
   case ReversalMode.Whole:return "整章反转：字形和左右位置一起翻转，盖印后与输入排版一致。导入雕刻软件后不要再次镜像。";
   case ReversalMode.Each:return "逐字反转：每个字原位左右翻转，排列不变。整块印章盖印后字形正向，但左右列会交换；左侧显示实际结果。";
   case ReversalMode.Selected:return "指定字反转：只翻转勾选的字，其余不变。整块盖印仍会左右翻转；请核对左侧实际盖印结果。";
   default:return "不反转：雕刻图保持输入方向，整块盖印后字形和左右位置会翻转；左侧显示实际结果。";
  }
 }
 public static string Missing(SealSettings s,FontEntry f){
  return string.Join(" ",Codepoints(s.Text).Where(c=>!White(c)&&(!f.Glyph.CharacterToGlyphMap.ContainsKey(c)||f.Glyph.CharacterToGlyphMap[c]==0)).Distinct().Select(c=>char.ConvertFromUtf32(c)).ToArray());
 }
 public static void Validate(SealSettings s, FontEntry f){
  if(f==null)throw new ArgumentException("请选择一种字体。");
  if(string.IsNullOrWhiteSpace(s.Text))throw new ArgumentException("请先输入印章文字。");
  if(Codepoints(s.Text).Count(c=>!White(c))>144)throw new ArgumentException("一枚印章最多排 144 个字，请减少文字。");
  if(!Enum.IsDefined(typeof(ReversalMode),s.Reversal))throw new ArgumentException("请选择有效的反转方式。");
  if(s.Reversal==ReversalMode.Selected){
   int count=Codepoints(s.Text).Count(c=>!White(c));
   if(s.ReversedCharacters.Count==0)throw new ArgumentException("请在反转方式下方勾选要反转的字。");
   if(s.ReversedCharacters.Any(i=>i<1||i>count))throw new ArgumentException("文字已改变，请重新选择要反转的字。");
  }
  if(s.WidthCm<.3||s.HeightCm<.3||s.WidthCm>20||s.HeightCm>20)throw new ArgumentException("宽和高应在 0.3～20 cm 之间。");
  if(s.Dpi<150||s.Dpi>1200)throw new ArgumentException("DPI 应在 150～1200 之间。");
  int w=Pixels(s.WidthCm,s.Dpi),h=Pixels(s.HeightCm,s.Dpi);
  if((long)w*h>MaxPixels||w>8000||h>8000)throw new ArgumentException("图片超过 1600 万像素或单边 8000 像素，请降低 DPI 或尺寸。");
  double pad=s.MarginCm+(s.Border?s.BorderCm:0);
  if(pad*2>=Math.Min(s.WidthCm,s.HeightCm))throw new ArgumentException("边距与边框过大，没有留下可排文字的空间。");
  if(s.Columns<1||s.Columns>12||s.SizePercent<20||s.SizePercent>100||s.SpacingPercent<0||s.SpacingPercent>40)throw new ArgumentException("排版参数超出允许范围。");
  string missing=Missing(s,f); if(missing.Length>0)throw new ArgumentException("当前字体缺少这些字："+missing+"。请先试“简 → 繁”，或换用包含这些字的字库。");
 }
 static List<List<int>> Lines(string text){return text.Replace("\r","").Split('\n').Select(x=>Codepoints(x)).ToList();}
 static Geometry TextGeometry(SealSettings s,FontEntry f,double width,double height,bool reverseCharacters){
  var cells=new List<Tuple<int,int,int>>(); int cols=1,rows=1;
  if(s.Layout<=1){
   var cps=Codepoints(s.Text).Where(c=>c!='\r'&&c!='\n').ToList();
   cols=Math.Max(1,s.Columns); rows=Math.Max(1,(int)Math.Ceiling(cps.Count/(double)cols));
   for(int i=0;i<cps.Count;i++){int col=s.Layout==0?cols-1-i/rows:i%cols;int row=s.Layout==0?i%rows:i/cols;cells.Add(Tuple.Create(cps[i],col,row));}
  }else{
   var lines=Lines(s.Text);
   if(s.Layout==2){rows=Math.Max(1,lines.Count);cols=Math.Max(1,lines.Max(l=>l.Count));for(int y=0;y<lines.Count;y++){int offset=0;for(int x=0;x<lines[y].Count;x++)cells.Add(Tuple.Create(lines[y][x],x+offset,y));}}
   else{cols=Math.Max(1,lines.Count);rows=Math.Max(1,lines.Max(l=>l.Count));for(int x=0;x<lines.Count;x++)for(int y=0;y<lines[x].Count;y++)cells.Add(Tuple.Create(lines[x][y],cols-1-x,y));}
  }
  double pxX=width/s.WidthCm,pxY=height/s.HeightCm;
  double pad=s.MarginCm+(s.Border?s.BorderCm:0),left=pad*pxX,top=pad*pxY;
  double cw=(width-2*left)/cols,ch=(height-2*top)/rows;
  if(cw<1||ch<1)throw new ArgumentException("文字区域过小，请减少行列或边距。");
  double factor=(1-s.SpacingPercent/100)*s.SizePercent/100;
  var group=new GeometryGroup{FillRule=FillRule.Nonzero};
  int characterNumber=0;
  foreach(var cell in cells){
   if(White(cell.Item1))continue;
   characterNumber++;
   ushort glyph=f.Glyph.CharacterToGlyphMap[cell.Item1];
   Geometry g=f.Glyph.GetGlyphOutline(glyph,100,100).Clone();Rect b=g.Bounds;
   if(b.IsEmpty||b.Width<=0||b.Height<=0)continue;
   double sx=cw*factor/b.Width,sy=ch*factor/b.Height;
   if(!s.Stretch)sx=sy=Math.Min(sx,sy);
   var matrix=new Matrix(sx,0,0,sy,left+cell.Item2*cw+(cw-b.Width*sx)/2-b.X*sx,top+cell.Item3*ch+(ch-b.Height*sy)/2-b.Y*sy);
   if(reverseCharacters&&(s.Reversal==ReversalMode.Each||(s.Reversal==ReversalMode.Selected&&s.ReversedCharacters.Contains(characterNumber)))){
    double center=left+(cell.Item2+.5)*cw;
    matrix.Append(new Matrix(-1,0,0,1,2*center,0));
   }
   g.Transform=new MatrixTransform(matrix);group.Children.Add(g);
  }
  group.Freeze();return group;
 }
 // Mask is the ink-bearing / retained surface, with normal readable orientation.
 public static BitmapSource Mask(SealSettings s,FontEntry f,int w,int h,bool reverseCharacters=false){
  var v=new DrawingVisual();using(var dc=v.RenderOpen()){
   dc.DrawRectangle(s.Mode==0?Brushes.White:Brushes.Black,null,new Rect(0,0,w,h));
   var text=TextGeometry(s,f,w,h,reverseCharacters);
   dc.DrawGeometry(s.Mode==0?Brushes.Black:Brushes.White,null,text);
   if(s.Mode==0&&s.Border&&s.BorderCm>0){
    double bx=s.BorderCm*w/s.WidthCm,by=s.BorderCm*h/s.HeightCm;
    dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,w,by));dc.DrawRectangle(Brushes.Black,null,new Rect(0,h-by,w,by));
    dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,bx,h));dc.DrawRectangle(Brushes.Black,null,new Rect(w-bx,0,bx,h));
   }
  }
  var bmp=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bmp.Render(v);bmp.Freeze();return bmp;
 }
 public static BitmapSource Map(BitmapSource mask,SealSettings s,bool paper,bool mirror){
  int w=mask.PixelWidth,h=mask.PixelHeight,stride=w*4;byte[] src=new byte[stride*h],dst=new byte[stride*h];mask.CopyPixels(src,stride,0);
  for(int y=0;y<h;y++)for(int x=0;x<w;x++){
   int a=y*stride+x*4,b=y*stride+(mirror?w-1-x:x)*4;bool retained=src[a]<128;
   if(paper){dst[b]=retained?(byte)44:(byte)255;dst[b+1]=retained?(byte)49:(byte)255;dst[b+2]=retained?(byte)180:(byte)255;}
   else{byte val=(s.BlackRemoves?!retained:retained)?(byte)0:(byte)255;dst[b]=dst[b+1]=dst[b+2]=val;} dst[b+3]=255;
  }
  var r=BitmapSource.Create(w,h,s.Dpi,s.Dpi,PixelFormats.Bgra32,null,dst,stride);r.Freeze();return r;
 }
 public static void Render(SealSettings s,FontEntry f,int w,int h,out BitmapSource engraving,out BitmapSource paper){
  var mask=Mask(s,f,w,h,true);
  bool whole=s.Reversal==ReversalMode.Whole;
  engraving=Map(mask,s,false,whole);
  // Pressing the stamp flips the actual retained surface, regardless of machine polarity.
  paper=Map(mask,s,true,!whole);
 }
 public static void SavePng(BitmapSource source,string path){var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(source));using(var st=new FileStream(path,FileMode.CreateNew,FileAccess.Write))e.Save(st);}
 public static void SaveBmp(BitmapSource source,string path){var e=new BmpBitmapEncoder();e.Frames.Add(BitmapFrame.Create(source));using(var st=new FileStream(path,FileMode.CreateNew,FileAccess.Write))e.Save(st);}
 public static string Export(SealSettings s,FontEntry f,bool bmp){
  Validate(s,f); if(!Path.IsPathRooted(s.OutputDir))throw new ArgumentException("保存位置请填写完整路径，例如 D:\\印章输出。");Directory.CreateDirectory(s.OutputDir);
  string sample=new string(s.Text.Where(c=>!char.IsWhiteSpace(c)&&!Path.GetInvalidFileNameChars().Contains(c)).Take(12).ToArray()).TrimEnd('.');if(sample.Length==0)sample="印章";
  string stem=sample+"_"+(s.Mode==0?"阳刻":"阴刻")+"_"+ReversalName(s)+"_"+s.WidthCm.ToString("0.##",CultureInfo.InvariantCulture)+"x"+s.HeightCm.ToString("0.##",CultureInfo.InvariantCulture)+"cm_"+s.Dpi+"dpi_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,4);
  string dir=Path.Combine(s.OutputDir,stem);Directory.CreateDirectory(dir);
  BitmapSource engraving,paper;Render(s,f,Pixels(s.WidthCm,s.Dpi),Pixels(s.HeightCm,s.Dpi),out engraving,out paper);
  SavePng(engraving,Path.Combine(dir,"雕刻图_"+(s.BlackRemoves?"黑色去除":"黑色保留")+"_"+ReversalName(s)+".png"));
  if(bmp)SaveBmp(engraving,Path.Combine(dir,"雕刻图_"+(s.BlackRemoves?"黑色去除":"黑色保留")+"_"+ReversalName(s)+".bmp"));
  SavePng(paper,Path.Combine(dir,"盖印效果_实际方向参考.png"));
  File.WriteAllText(Path.Combine(dir,"尺寸与使用说明.txt"),"印章2\r\n文字："+s.Text+"\r\n字体："+f.Name+"\r\n效果："+(s.Mode==0?"阳刻（朱文，字凸起、纸上字红）":"阴刻（白文，字凹下、纸上字白）")+"\r\n宽 × 高："+s.WidthCm+" × "+s.HeightCm+" cm\r\n像素："+engraving.PixelWidth+" × "+engraving.PixelHeight+" px\r\nDPI："+s.Dpi+"\r\n反转方式："+ReversalName(s)+(s.Reversal==ReversalMode.Selected?"（第 "+CharacterNumbers(s)+" 字）":"")+"\r\n黑色含义："+(s.BlackRemoves?"雕刻去除区域":"保留凸起区域")+"\r\n\r\n雕刻时导入“雕刻图”。\r\n"+ReversalHelp(s)+"\r\n“盖印效果”模拟整块印章盖到纸上的实际方向，不用于雕刻。\r\n部分雕刻软件忽略 PNG 的 DPI，请在导入后手动设为上面的厘米尺寸。\r\n仅提供黑白位图，不包含刀路、深度、功率等机器参数。请依据设备的黑白约定选择黑色含义。\r\n字体字形以字库为准，简繁转换后请核对姓名与多义字，软件不做古文字考证。\r\n",new UTF8Encoding(true));
  if(f.Name.Contains("崇羲")||f.Glyph.FamilyNames.Values.Any(x=>x.Contains("Chong Xi Small Seal")))File.WriteAllText(Path.Combine(dir,"字体来源.txt"),"崇羲篆体：王心怡、季旭昇等；Academia Sinica 2022。\r\n来源：https://xiaoxue.iis.sinica.edu.tw/chongxi/\r\n许可：CC-BY-ND-3.0-TW-or-later；保留署名，不修改字库。\r\nhttps://creativecommons.org/licenses/by-nd/3.0/tw/",new UTF8Encoding(true));
  if(f.Name.Contains("印篆")||f.Glyph.FamilyNames.Values.Any(x=>x.Contains("HanWangInJang")))File.WriteAllText(Path.Combine(dir,"字体来源.txt"),"王汉宗印篆体繁：王汉宗，2002。\r\n用作汉白文风格参考，不代表抖音同款。\r\n来源：https://fonts.mobanwang.com/200909/7019.html\r\n下载页限定个人学习参考，商用需自行向版权方核实授权。",new UTF8Encoding(true));
  return dir;
 }
}
}
