using MonolithHarnessGui.Portable;
int passed=0;
void Check(bool result,string name) {if(!result)throw new Exception(name);passed++;}
var bars=new MobileInsets(0,32,0,30,0,995);
var normal=MobileLayout.Calculate(443,995,bars);
Check(normal.Top>=40 && normal.Bottom>=38,"System bars stay outside content");
Check(!normal.Compact && normal.ContentWidth==419,"Portrait navigation fits");
var keyboard=MobileLayout.Calculate(443,995,bars with {KeyboardBottom=350});
Check(keyboard.Compact && keyboard.Bottom==358,"Overlay keyboard is avoided");
var resized=MobileLayout.Calculate(443,645,bars with {KeyboardBottom=350});
Check(resized.Bottom==38,"Already resized viewport does not count IME twice");
var partial=MobileLayout.Calculate(443,795,bars with {KeyboardBottom=350});
Check(partial.Bottom==158,"Partially resized viewport avoids only remaining IME");
var wide=MobileLayout.Calculate(885,851,new(0,32,0,30,0,851));
Check(wide.ContentWidth==837 && !wide.Compact,"Unfolded layout uses a bounded reading column");
var landscape=MobileLayout.Calculate(995,443,new(32,0,30,0,0,443));
Check(landscape.Compact && landscape.Left>=56 && landscape.Right>=54,"Landscape respects side cutout and navigation");
foreach(var width in new double[]{280,320,360,443,600,885,1200})
foreach(var height in new double[]{240,400,645,995})
{
 var layout=MobileLayout.Calculate(width,height,new(0,24,0,24,0,height));
 Check(layout.ContentWidth<=width && layout.ContentWidth>=0,"No horizontal overflow");
 Check(layout.InputMaxHeight is >=48 and <=128,"Input is bounded");
 Check(layout.FormMaxHeight>=0 && layout.FormMaxHeight<=layout.UsableHeight,"Dialog scroll remains available");
 // Attach, send, gaps and composer padding never squeeze the model chip below a usable size.
 Check(layout.ContentWidth-2*MobileLayout.TouchTarget-16-16>=120 || width<320,"Normal toolbar remains usable");
}
var shortIme=MobileLayout.Calculate(995,443,new(32,0,30,0,290,443));
Check(shortIme.Minimal && shortIme.Compact,"Very short IME viewport keeps only the composer");
Check(shortIme.UsableHeight>=shortIme.InputMaxHeight+MobileLayout.TouchTarget+24,"Composer fits short IME viewport");
Check(shortIme.FormMaxHeight+72<=shortIme.UsableHeight,"Compact modal actions fit with the keyboard open");
Check(normal.ShowWelcomeIllustration,"Welcome illustration fits tall portrait");
Check(!landscape.ShowWelcomeIllustration,"Short landscape reserves space for welcome actions");
var invalid=MobileLayout.Calculate(-1,-1,new(-1,-1,-1,-1,-1,-1));
Check(invalid.ContentWidth==0 && invalid.UsableHeight==0,"Invalid transient dimensions are safe");
Console.WriteLine($"{passed} mobile responsive checks passed.");
