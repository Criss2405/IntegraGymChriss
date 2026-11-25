using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace IntegraGym;
internal static class DashboardLook
{
    internal static readonly Color Canvas=Color.FromArgb(11,20,36), Surface=Color.FromArgb(20,35,56), Muted=Color.FromArgb(194,209,228), Divider=Color.FromArgb(39,59,79);
    static readonly string[] Weights={"Regular","Medium","SemiBold"};
    static readonly PrivateFontCollection[] Faces=Weights.Select(weight=>{
        var fonts=new PrivateFontCollection();string path=Path.Combine(AppContext.BaseDirectory,"Assets","Inter","Inter-"+weight+".ttf");
        if(File.Exists(path)&&AddFontResourceEx(path,16,IntPtr.Zero)>0)fonts.AddFontFile(path);
        return fonts;
    }).ToArray();
    [DllImport("gdi32",CharSet=CharSet.Unicode)]static extern int AddFontResourceEx(string file,uint flags,IntPtr reserved);
    static readonly Dictionary<(float,int),Font> cache=new();
    internal static Font Face(float pt,int weight=0){
        if(!cache.TryGetValue((pt,weight),out var face)){var family=Faces[weight].Families.FirstOrDefault();
            cache[(pt,weight)]=face=family==null?new Font(weight==2?"Segoe UI Semibold":"Segoe UI",pt):new Font(family,pt,FontStyle.Regular);}
        return face;
    }
    internal static void Text(Graphics g,string s,Rectangle r,float pt,Color color,bool bold=false,bool right=false)=>
        TextRenderer.DrawText(g,s,Face(pt,bold?2:0),r,color,TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|(right?TextFormatFlags.Right:0));
    internal static void Card(Graphics g,Rectangle rect,bool focus=false){
        g.Clear(Canvas);if(rect.Width<6||rect.Height<6)return;g.SmoothingMode=SmoothingMode.AntiAlias;rect.Inflate(-1,-1);
        using var fill=new LinearGradientBrush(rect,Color.FromArgb(21,42,63),Surface,17f);
        g.FillRoundedRectangle(fill,rect,new Size(18,18));
        if(focus){using var p=new Pen(Color.LightCyan,2);g.DrawRoundedRectangle(p,rect,new Size(18,18));}
    }
    internal static void ApplyGrid(DataGridView g){
        g.Font=Face(11);g.DefaultCellStyle.Font=g.Font;g.ColumnHeadersDefaultCellStyle.Font=Face(11,1);
        g.BackgroundColor=g.DefaultCellStyle.BackColor=g.AlternatingRowsDefaultCellStyle.BackColor=Surface;
        g.GridColor=Divider;g.ColumnHeadersDefaultCellStyle.BackColor=g.ColumnHeadersDefaultCellStyle.SelectionBackColor=Color.FromArgb(24,48,72);
        g.ColumnHeadersDefaultCellStyle.ForeColor=g.ColumnHeadersDefaultCellStyle.SelectionForeColor=Color.AliceBlue;
        g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(33,57,82);g.RowTemplate.Height=g.RowTemplate.MinimumHeight=g.LogicalToDeviceUnits(38);
        g.DefaultCellStyle.WrapMode=DataGridViewTriState.True;g.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
    }
}


