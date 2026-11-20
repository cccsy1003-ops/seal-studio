using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Windows.Media.Imaging;
namespace SealStudio {
public static class MirrorTests {
 static byte[] Bytes(BitmapSource b){var data=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(data,b.PixelWidth*4,0);return data;}
 static void Assert(bool condition,string message){if(!condition)throw new Exception("Mirror test failed: "+message);}
 static BitmapSource Cut(SealSettings s,FontEntry f,int w,int h){BitmapSource cut,paper;SealCore.Render(s,f,w,h,out cut,out paper);return cut;}
 public static void Run(string dir,FontEntry f,List<string> report){
  Assert(Studio.ReadSavedReversal(XElement.Parse("<s><Mirror>true</Mirror></s>"))==ReversalMode.Whole,"old true");
  Assert(Studio.ReadSavedReversal(XElement.Parse("<s><Mirror>false</Mirror></s>"))==ReversalMode.None,"old false");
  Assert(Studio.ReadSavedReversal(new XElement("s"))==ReversalMode.Whole,"new default");
  Assert(Studio.ReadSavedReversal(XElement.Parse("<s><Mirror>true</Mirror><MirrorMode>2</MirrorMode></s>"))==ReversalMode.Each,"new mode priority");
  Assert(Studio.ReadSavedCharacters(XElement.Parse("<s><ReversedCharacters>1,3,3,-1,x</ReversedCharacters></s>")).SetEquals(new[]{1,3}),"saved chosen characters");
  Assert(SealCore.Codepoints("王\U00020000明").Count==3,"non-BMP character index");
  report.Add("PASS: legacy settings migration, mode persistence and Unicode character indexing");

  // Cell extents are fixed here independently of the layout code: a 3x2 grid,
  // zero margin, 240x180 pixels per cell. Distinct asymmetric glyphs detect swaps.
  foreach(int layout in new[]{0,1,2,3})foreach(int mode in new[]{0,1})foreach(bool polarity in new[]{false,true}){
   var s=new SealSettings{Text=layout==2?"王B乙\n田R丙":layout==3?"王B\n乙田\nR丙":"王B乙田R丙",Columns=3,Layout=layout,Mode=mode,BlackRemoves=polarity,Border=false,MarginCm=0,Reversal=ReversalMode.None};
   int w=720,h=360;var normal=Bytes(Cut(s,f,w,h));
   s.Reversal=ReversalMode.Whole;var whole=Bytes(Cut(s,f,w,h));
   s.Reversal=ReversalMode.Each;var each=Bytes(Cut(s,f,w,h));
   s.Reversal=ReversalMode.Selected;s.ReversedCharacters.Add(2);var selected=Bytes(Cut(s,f,w,h));
   int changed=0;int differences=0;int targetColumn=layout==0||layout==3?2:1,targetRow=layout==0||layout==3?1:0;
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    int at=(y*w+x)*4;
    Assert(whole[at]==normal[(y*w+w-1-x)*4],"whole flip preserves exact pixels");
    int cellX=x/240;int mirrorX=cellX*240+239-x%240;
    // WPF vector antialiasing can differ at the threshold by 1 pixel.
    if(each[at]!=normal[(y*w+mirrorX)*4]){differences++;bool edge=false;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=mirrorX+dx,yy=y+dy;if(xx>=cellX*240&&xx<(cellX+1)*240&&yy>=0&&yy<h&&normal[(yy*w+xx)*4]==each[at])edge=true;}Assert(edge,"per-character reflection mismatch beyond a one-pixel raster edge");}
    bool target=cellX==targetColumn&&y/180==targetRow;
    if(!target)Assert(selected[at]==normal[at],"unselected character or region changed");
    else if(selected[at]!=normal[at])changed++;
   }
   Assert(differences<720*360/200,"per-character reflection: "+differences+" differences; layout "+layout+" mode "+mode+" polarity "+polarity);
   Assert(changed>100,"selected asymmetric glyph must actually flip");
   Assert(!whole.SequenceEqual(each),"whole and each must differ in multi-column layout");
   foreach(ReversalMode rev in Enum.GetValues(typeof(ReversalMode))){
    s.Reversal=rev;BitmapSource cut,paper;SealCore.Render(s,f,w,h,out cut,out paper);var c=Bytes(cut);var p=Bytes(paper);
    for(int y=0;y<h;y++)for(int x=0;x<w;x++){
     int at=(y*w+x)*4;bool keep=c[(y*w+w-1-x)*4]==(polarity?255:0);
     Assert((p[at]<128)==keep,"actual paper preview must reflect retained surface");
    }
   }
  }
  report.Add("PASS: four layouts, both seal types and polarities; whole, each and chosen-character reflection; actual paper preview");

  foreach(bool stretch in new[]{false,true}){
   var s=new SealSettings{Text="王 B\n田R丙",Columns=3,Layout=1,WidthCm=3,HeightCm=4,Border=true,MarginCm=.18,Stretch=stretch,Reversal=ReversalMode.None};
   var normal=Bytes(Cut(s,f,709,945));s.Reversal=ReversalMode.Selected;s.ReversedCharacters.Add(2);var selected=Bytes(Cut(s,f,709,945));
   // Visible character 2 is B in the last cell of row 1; the space is skipped.
   int changes=0;
   for(int y=0;y<945;y++)for(int x=0;x<709;x++)if(normal[(y*709+x)*4]!=selected[(y*709+x)*4]){changes++;Assert(x>=453&&x<653&&y>=56&&y<473,"space/newline indexing or border changed");}
   Assert(changes>100,"chosen B must flip");
  }
  report.Add("PASS: spacing, newline indexing, rectangular dimensions, border preservation and proportional glyphs");

  var exportSettings=new SealSettings{Text="王B乙田",Columns=2,Reversal=ReversalMode.Selected,OutputDir=dir};exportSettings.ReversedCharacters.Add(2);
  string folder=SealCore.Export(exportSettings,f,true);
  Assert(Directory.GetFiles(folder,"*指定字左右反转.png").Length==1,"PNG mode filename");
  Assert(Directory.GetFiles(folder,"*指定字左右反转.bmp").Length==1,"BMP mode filename");
  Assert(File.Exists(Path.Combine(folder,"盖印效果_实际方向参考.png")),"actual paper filename");
  Assert(File.ReadAllText(Path.Combine(folder,"尺寸与使用说明.txt")).Contains("第 2 字"),"chosen characters recorded");
  BitmapSource e,paperRef;SealCore.Render(exportSettings,f,709,709,out e,out paperRef);
  using(var st=File.OpenRead(Directory.GetFiles(folder,"雕刻图*.png")[0])){
   var decoded=BitmapFrame.Create(st,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
   Assert(decoded.PixelWidth==709&&Math.Abs(decoded.DpiX-600)<.1,"PNG size and DPI");
   Assert(Bytes(decoded).SequenceEqual(Bytes(e)),"export matches preview rendering");
  }
  exportSettings.ReversedCharacters.Clear();bool rejected=false;try{SealCore.Validate(exportSettings,f);}catch(ArgumentException){rejected=true;}Assert(rejected,"empty selection rejected");
  report.Add("PASS: PNG/BMP filenames, chosen characters, DPI, export pixel equality and empty selection guard");
 }
}
}
