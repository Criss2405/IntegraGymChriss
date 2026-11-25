using System.Drawing.Drawing2D;
using System.Globalization;
namespace IntegraGym;

internal static class Night
{
    public static readonly Color Canvas=Color.FromArgb(13,23,39), Surface=Color.FromArgb(23,36,56),
        Raised=Color.FromArgb(45,67,99), Line=Color.FromArgb(48,67,93), Muted=Color.FromArgb(182,195,219),
        Blue=Color.FromArgb(124,169,255), Selected=Color.FromArgb(42,61,99);
    public static Color Mix(Color a, Color b,float t) => Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
    public static void Label(Graphics g,string s,Rectangle r,float pt,Color c,bool bold=false, bool right=false)
    {
        using var font=new Font("Segoe UI",pt,bold?FontStyle.Bold:FontStyle.Regular);
        TextRenderer.DrawText(g,s,font,r,c,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|(right?TextFormatFlags.Right:TextFormatFlags.Left));
    }
}

// A single native button owns the hit area, keyboard activation and accessibility.
internal sealed class FairButton : Button
{
    readonly System.Windows.Forms.Timer animation=new(){Interval=16};
    float blend; bool over,active;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool SelectedItem {get=>active;set{active=value;Invalidate();}}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal DashboardMetric? Metric {get;init;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color Idle {get;init;}=Night.Surface;
    internal bool Soft;
    public FairButton()
    {
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;DoubleBuffered=true;
        MouseEnter+=(_,_)=>{over=true;animation.Start();};
        MouseLeave+=(_,_)=>{over=false;animation.Start();};
        animation.Tick+=(_,_)=>{blend=Math.Clamp(blend+(over?.14f:-.14f),0,1);Invalidate();if(blend==0||blend==1)animation.Stop();};
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Soft){PaintSoft(e.Graphics);return;}
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent is FairHero?DashboardArt.Panel:Parent?.BackColor??DashboardArt.Canvas);
        if(Width<6||Height<6)return;
        var box=new Rectangle(1,1,Width-3,Height-3);int d=LogicalToDeviceUnits(12);
        var focus=Focused&&ShowFocusCues;
        using var fill=new SolidBrush(Night.Mix(active?Color.FromArgb(12,59,96):Metric!=null?DashboardArt.Panel:Idle,Night.Raised,blend));
        using var pen=new Pen(focus?Color.White:Night.Mix(active?DashboardArt.Cyan:DashboardArt.Edge,Night.Blue,blend),focus?2:1);
        g.FillRoundedRectangle(fill,box,new Size(d,d));if(active||over||focus||Metric!=null||Text=="Actualizar"||Text=="Cerrar sesión")g.DrawRoundedRectangle(pen,box,new Size(d,d));
        if(active){using var mark=new SolidBrush(Night.Blue);g.FillRectangle(mark,3,Height/4,3,Height/2);}
        var content=Rectangle.Inflate(box,-d,0);
        if(Metric is {} m)
        {
            int h=content.Height;int badge=LogicalToDeviceUnits(52);var tile=new Rectangle(content.X+4,(h-badge)/2,badge,badge);
            using var block=new SolidBrush(Color.FromArgb(12,55,87));g.FillRoundedRectangle(block,tile,new Size(10,10));DashboardArt.Icon(g,m.Title,tile,21,Color.LightCyan);
            int x=tile.Right+LogicalToDeviceUnits(14),w=Width-x-8;
            Night.Label(g,m.Title,new(x,h/6,w,h/3),11,Color.White,true);
            Night.Label(g,m.Value+(m.Module=="pagos.gestionar"?" MXN":""),new(x,h*4/9,w,h*4/9),m.Value.Length>10?16:21,Color.White,true);
        }
        else {
            int side=LogicalToDeviceUnits(27);DashboardArt.Icon(g,Text,new(content.X,0,side,Height),14,active?DashboardArt.Cyan:Color.AliceBlue);
            Night.Label(g,Text,new(content.X+side+6,0,Width-content.X-side-9,Height),10,Enabled?Color.White:Night.Muted,active);
        }
    }
    void PaintSoft(Graphics g){
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var metric=Metric;bool nav=Tag!=null;g.Clear(nav?Idle:DashboardLook.Canvas);
        var box=new Rectangle(1,1,Width-3,Height-3);if(box.Width<4||box.Height<4)return;
        int d=LogicalToDeviceUnits(metric==null?12:18);
        var baseColor=active||Text=="Actualizar"?Color.FromArgb(12,74,121):nav?Idle:DashboardLook.Surface;
        using var fill=new SolidBrush(Night.Mix(baseColor,Night.Raised,blend));g.FillRoundedRectangle(fill,box,new Size(d,d));
        if(Focused&&ShowFocusCues){using var p=new Pen(Color.LightCyan,2);g.DrawRoundedRectangle(p,box,new Size(d,d));}
        int scale(int v)=>LogicalToDeviceUnits(v);
        if(metric is {} m){
            int s=scale(58);var tile=new Rectangle(scale(19),(Height-s)/2,s,s);using var block=new SolidBrush(Color.FromArgb(24,59,83));g.FillRoundedRectangle(block,tile,new Size(13,13));DashboardArt.Icon(g,m.Title,tile,22,Color.AliceBlue);
            int x=tile.Right+scale(18),w=Width-x-scale(10);DashboardLook.Text(g,m.Title,new(x,Height/8,w,Height/3),12,Color.White,true);
            bool mxn=m.Module=="pagos.gestionar";int suffix=mxn?TextRenderer.MeasureText(g,"MXN",DashboardLook.Face(13,1)).Width:0;
            float pt=28;int digits;do{digits=TextRenderer.MeasureText(g,m.Value,DashboardLook.Face(pt,2)).Width;if(digits+suffix+5<=w||pt<=12)break;pt--;}while(true);
            DashboardLook.Text(g,m.Value,new(x,Height*4/10,w-suffix,Height/2),pt,Color.White,true);
            if(mxn)DashboardLook.Text(g,"MXN",new(x+digits+2,Height*4/10+scale(7),suffix+3,Height/2),13,DashboardLook.Muted);
        }else{
            int side=scale(30);DashboardArt.Icon(g,Text,new(scale(10),0,side,Height),15,Color.AliceBlue);
            TextRenderer.DrawText(g,Text,DashboardLook.Face(11,1),new Rectangle(scale(13)+side,0,Width-side-scale(15),Height),Enabled?Color.White:DashboardLook.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
    }
    protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}

internal sealed class FairHero : Panel
{
    public FairHero(){DoubleBuffered=true;ResizeRedraw=true;AccessibleName="Tu gimnasio hoy";}
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(DashboardLook.Canvas);
    }
}

internal sealed class AttendanceTrend : Control
{
    int[] counts=new int[7]; DateTime end; int selected=-1;
    readonly CultureInfo culture=CultureInfo.GetCultureInfo("es-MX");
    public AttendanceTrend(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Night.Surface;TabStop=true;AccessibleName="Asistencias de los últimos siete días"; }
    public void SetWeek(DateTime date,int[] values){end=date;counts=values;selected=-1;AccessibleDescription=string.Join("; ",values.Select((v,i)=>Caption(i)));Invalidate();}
    string Caption(int i)=>$"{end.AddDays(i-6).ToString("ddd dd MMM",culture)} · {counts[i]} {(counts[i]==1?"entrada":"entradas")}";
    int pad=>LogicalToDeviceUnits(32);
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int i=e.Y<Height*.32||e.X<pad-8||e.X>Width-pad+8?-1:Math.Clamp((int)Math.Round((e.X-pad)*6d/Math.Max(1,Width-2*pad)),0,6);if(i!=selected){selected=i;Invalidate();}}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);selected=-1;Invalidate();}
    protected override bool IsInputKey(Keys key)=>key is Keys.Left or Keys.Right||base.IsInputKey(key);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode is Keys.Left or Keys.Right){selected=Math.Clamp(selected+(e.KeyCode==Keys.Right?1:-1),0,6);Invalidate();e.Handled=true;}}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);selected=6;Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);selected=-1;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        int w=Width,h=Height;if(w<=2*pad)return;DashboardLook.Card(g,ClientRectangle,Focused&&ShowFocusCues);
        if(counts.All(n=>n==0)){
            DashboardLook.Text(g,"Asistencias · últimos 7 días",new(12,0,w/2,h),12,Color.White,true);
            DashboardLook.Text(g,"Sin entradas registradas",new(w/2,0,w/2-16,h),11,DashboardLook.Muted,false,true);return;
        }
        if(h<50)return;
        DashboardLook.Text(g,"Asistencias · últimos 7 días",new(12,0,w/2,h/4),12,Color.White,true);
        DashboardLook.Text(g,selected>=0?Caption(selected):counts.Max()==0?"Sin entradas esta semana":"Entradas por día",new(w/2,0,w/2-12,h/4),10,Night.Blue,false,true);
        float bottom=h*.78f,top=h*.34f;int max=Math.Max(1,counts.Max());
        PointF[] dots=counts.Select((v,i)=>new PointF(pad+(w-2*pad)*i/6f,bottom-(bottom-top)*((float)v/max))).ToArray();
        using var line=new Pen(Night.Line);for(int j=0;j<3;j++){float y=top+(bottom-top)*j/2;g.DrawLine(line,pad,y,w-pad,y);}
        using var area=new SolidBrush(Color.FromArgb(45,87,140,252));g.FillPolygon(area,dots.Concat(new[]{new PointF(w-pad,bottom+1),new PointF(pad,bottom+1)}).ToArray());
        using var stroke=new Pen(Night.Blue,2.5f);g.DrawLines(stroke,dots);
        for(int i=0;i<7;i++){var p=dots[i];int r=i==selected?6:3;g.FillEllipse(i==selected?Brushes.White:Brushes.LightSteelBlue,p.X-r,p.Y-r,2*r,2*r);
            DashboardLook.Text(g,end.AddDays(i-6).ToString("dd/MM",culture),new((int)p.X-24,(int)bottom+3, LogicalToDeviceUnits(50),h-(int)bottom-3),10,DashboardLook.Muted);}

    }
}










