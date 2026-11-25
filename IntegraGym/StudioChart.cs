using System.Drawing.Drawing2D;
using System.Globalization;
namespace IntegraGym;

internal sealed record Bucket(string Label,decimal Value);

internal sealed class StudioChart(bool ring) : Control
{
    static readonly Color[] Accents={Color.FromArgb(38,150,255),Color.FromArgb(42,235,234),Color.FromArgb(160,151,255),Color.FromArgb(107,195,165)};
    static readonly CultureInfo Mexico=CultureInfo.GetCultureInfo("es-MX");
    readonly ToolTip hint=new();
    protected override void Dispose(bool disposing){if(disposing)hint.Dispose();base.Dispose(disposing);}
    Bucket[] data=[]; int active=-1;
    string Title=>ring?"Membresías por plan":"Ingresos por mes";
    string Value(decimal v)=>ring?v.ToString("N0"):v.ToString("C2",Mexico);
    public void Bind(Bucket[] values){
        DoubleBuffered=true;ResizeRedraw=true;TabStop=true;AccessibleName=Title;
        data=values;active=-1;AccessibleDescription=string.Join("; ",values.Select(x=>x.Label+": "+Value(x.Value)));Invalidate();
    }
    protected override bool IsInputKey(Keys key)=>key is Keys.Left or Keys.Right||base.IsInputKey(key);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(data.Length>0 && e.KeyCode is Keys.Left or Keys.Right){active=Math.Clamp(active+(e.KeyCode==Keys.Left?-1:1),0,data.Length-1);Invalidate();}}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);active=-1;Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);float diameter=Math.Min(Width*.33f,Height*.56f);
        float start=Height*(.59f-.145f*(data.Length-1)/2f)-Height/14f;
        int slot=ring?(e.X>=Width*.27f+diameter*.58f+20&&e.Y>=start?(int)((e.Y-start)/(Height*.145f)):-1):
        (e.Y>Height*.3f&&e.X>=Width*.07f?(int)((e.X-Width*.07f)/(Width*.86f/6)):-1);
        int next=slot>=0&&slot<data.Length?slot:-1;if(next!=active){active=next;hint.SetToolTip(this,next<0?null:data[next].Label+" · "+Value(data[next].Value));Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);active=-1;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        int w=Width,h=Height;if(w<80||h<60)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Night.Canvas);
        DashboardLook.Card(g,ClientRectangle,Focused&&ShowFocusCues);
        void txt(string s,int x,int y,int dx,int dy,float pt,Color color,bool bold=false)=>DashboardLook.Text(g,s,new(x,y,Math.Max(1,dx),Math.Max(1,dy)),pt,color,bold);
        int p=LogicalToDeviceUnits(16);
        txt(Title,p,4,w-2*p,h/6,16,Color.White,true);
        txt(active>=0?data[active].Label+"  ·  "+Value(data[active].Value):ring?"Distribución de membresías vigentes":"Pagos aplicados  /  MXN  /  6 meses",p,h/6,w-2*p,h/9,11,DashboardLook.Muted);
        decimal total=data.Sum(x=>x.Value);
        if(ring){
            float s=Math.Min(w*.33f,h*.56f);var r=new RectangleF(w*.27f-s/2,h*.60f-s/2,s,s);
            using var track=new Pen(Night.Line,s*.14f);g.DrawEllipse(track,r);
            float angle=-90;
            for(int i=0;i<data.Length;i++){
                float sweep=total>0?(float)(data[i].Value/total)*360:0;
                using var pen=new Pen(Accents[i%4],s*(active==i?.18f:.14f));
                if(sweep>0)g.DrawArc(pen,r,angle,Math.Max(.01f,sweep-(data.Length>1?2:0)));angle+=sweep;
                int x=(int)(w*.27f+s*.58f+20),y=(int)(h*(.59f+.145f*(i-(data.Length-1)/2f)))-h/14;using var dot=new SolidBrush(Accents[i%4]);
                g.FillEllipse(dot,x,y+h*.055f,8,8);
                int end=Math.Min(w-p,x+LogicalToDeviceUnits(300));var legend=new Rectangle(x+14,y,end-x-100,h/7);TextRenderer.DrawText(g,data[i].Label,DashboardLook.Face(11),legend,Color.AliceBlue,TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                DashboardLook.Text(g,$"{data[i].Value:N0} ({(total==0?0:data[i].Value/total*100):0.#}%)",new(end-90,y,90,h/7),11,Color.White,true,true);
            }
            var center=DashboardLook.Face(28,2);TextRenderer.DrawText(g,Value(total),center,Rectangle.Round(r),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(total==0)txt("Sin membresías vigentes",(int)(w*.47),h/2,w/2,h/4,10,Night.Muted);
        }else if(data.Length>0){
            decimal peak=Math.Max(1,data.Max(x=>x.Value));float step=w*.86f/data.Length,baseline=h*.82f;
            using var rule=new Pen(Night.Line);for(int j=0;j<4;j++){float y=baseline-h*.15f*j;g.DrawLine(rule,w*.07f,y,w*.93f,y);}
            for(int i=0;i<data.Length;i++){
                var item=data[i];float x=w*.07f+i*step;float size=(float)(item.Value/peak)*h*.44f;
                float bw=step*.56f;if(size>0){var rect=new RectangleF(x+(step-bw)/2,baseline-size,bw,size);using var b=new LinearGradientBrush(rect,active==i?Color.White:Accents[1],Accents[0],90);float rad=Math.Min(10,Math.Min(size/2,bw/2));g.FillRoundedRectangle(b,rect,new SizeF(rad,rad));g.FillRectangle(b,rect.X,rect.Y+size/2,bw,size/2);}
                txt(item.Label,(int)(x+step*.12f),(int)baseline,(int)step,h/6,11,DashboardLook.Muted);
                txt(item.Value.ToString("N0",Mexico),(int)(x+step*.06f),(int)(baseline-size)-20,(int)step,20,11,Color.White,true);
            }
            if(total==0)txt("Sin pagos aplicados en este periodo",p,h/2,w-2*p,h/6,10,Night.Muted);
        }
    }
}






