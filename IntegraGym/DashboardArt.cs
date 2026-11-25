using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace IntegraGym;
internal static class DashboardArt
{
    internal static readonly Color Canvas=Color.FromArgb(4,16,33),Panel=Color.FromArgb(10,30,53),Edge=Color.FromArgb(42,98,135),Cyan=Color.FromArgb(44,220,237);
    internal static void Frame(Graphics g,Rectangle bounds,bool hero=false){
        if(bounds.Width<4||bounds.Height<4)return;
        g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Canvas);bounds.Inflate(-1,-1);
        using var shape=new GraphicsPath();shape.AddRoundedRectangle(bounds,new Size(11,11));
        using var bg=new LinearGradientBrush(bounds,hero?Color.FromArgb(10,32,59):Panel,hero?Color.FromArgb(4,57,91):Color.FromArgb(8,24,45),8f);
        g.FillPath(bg,shape);
        if(hero){var state=g.Save();g.SetClip(shape);
            for(int i=0;i<4;i++){float x=bounds.Width*.68f+i*52;using var stroke=new Pen(Color.FromArgb(96-i*12,15,199,242),1.5f);g.DrawBezier(stroke,x,bounds.Bottom+3,x+28,bounds.Height*.4f,x+72,8,x+140,-12);}
            g.Restore(state);}
        using var line=new Pen(Edge);g.DrawPath(line,shape);
    }
    static string Symbol(string name)=>name switch{
        "Dashboard"=>"\uE80F","Clientes" or "Membresías activas"=>"\uE716","Planes"=>"\uE81E","Membresías" or "Pagos"=>"\uE8C7",
        "Asistencias" or "Asistencias de hoy"=>"\uE787","Entrenadores"=>"\uE7FC","Reportes"=>"\uE9EC","Usuarios y permisos"=>"\uEA18",
        "Bitácora"=>"\uE8FD","Copia de seguridad" or "Ingresos del día"=>"\uEDA2","Configuración"=>"\uE713","Mi contraseña"=>"\uE72E",
        "Cerrar sesión"=>"\uE8BB","Actualizar"=>"\uE72C",_=>""};
    internal static void Icon(Graphics g,string name,Rectangle box,float size,Color color){
        if(name is "Ingresos del día" or "Copia de seguridad"){
            var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
            float u=Math.Min(box.Width,box.Height)*.53f,x=box.X+(box.Width-u)/2,y=box.Y+(box.Height-u)/2;
            using var p=new Pen(color,Math.Max(1.3f,u*.064f));g.DrawEllipse(p,x,y,u,u*.36f);
            g.DrawLine(p,x,y+u*.18f,x,y+u*.8f);g.DrawLine(p,x+u,y+u*.18f,x+u,y+u*.8f);
            g.DrawArc(p,x,y+u*.32f,u,u*.36f,0,180);g.DrawArc(p,x,y+u*.62f,u,u*.36f,0,180);g.Restore(state);return;
        }
        using var font=new Font("Segoe MDL2 Assets",size);TextRenderer.DrawText(g,Symbol(name),font,box,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    }
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr h,string theme,string? classes);
    internal static void DarkScroll(Control c){c.HandleCreated+=(_,_)=>SetWindowTheme(c.Handle,"DarkMode_Explorer",null);if(c.IsHandleCreated)SetWindowTheme(c.Handle,"DarkMode_Explorer",null);}
    internal static void Badge(object? sender,DataGridViewCellPaintingEventArgs e){
        if(sender is not DataGridView grid||e.RowIndex<0||e.ColumnIndex<0||e.Graphics is not {} g)return;
        var column=grid.Columns[e.ColumnIndex];
        if(!(grid.ReadOnly||column.ReadOnly))return;
        string label=Convert.ToString(e.FormattedValue)??"";
        switch(column.Name){
            case "Activo": if(e.Value is null or DBNull)return;
                if(bool.TryParse(Convert.ToString(e.Value),out var active))label=active?"Activo":"Inactivo";
                else if(Convert.ToString(e.Value) is "1" or "0")label=Convert.ToString(e.Value)=="1"?"Activo":"Inactivo";else return;
                break;
            case "Estado":case "EstadoPago":case "Movimiento":break;
            default:return;
        }
        if(string.IsNullOrWhiteSpace(label))return;
        var color=label switch {
            "Inactivo" or "Inactiva" or "Cancelada" or "Cancelado" or "Vencida"=>Color.FromArgb(250,166,160),
            "Pendiente" or "Parcial"=>Color.FromArgb(250,203,126),
            "Activo" or "Activa" or "Aplicado" or "Pagada"=>Color.FromArgb(97,227,181),
            _ when label.StartsWith("Pago")=>Color.FromArgb(97,227,181),
            _ when label.StartsWith("Entrada")=>Color.FromArgb(127,196,255),
            _=>Cyan};
        
        int width=Math.Min(e.CellBounds.Width-18,TextRenderer.MeasureText(label,grid.Font).Width+18);if(width<=0)return;
        if(e.CellBounds.Height<15)return;e.PaintBackground(e.ClipBounds,true);
        int h=Math.Min(e.CellBounds.Height-3,grid.Font.Height+5);var rect=new Rectangle(e.CellBounds.X+8,e.CellBounds.Y+(e.CellBounds.Height-h)/2,width,h);
        using var fill=new SolidBrush(Night.Mix(Panel,color, .30f));g.SmoothingMode=SmoothingMode.AntiAlias;

        int rad=rect.Height/2;g.FillRoundedRectangle(fill,rect,new Size(rad,rad));
        TextRenderer.DrawText(g,label,grid.Font,rect,color,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);e.Handled=true;
    }
}
internal sealed class DashboardFrame : Panel
{
    public DashboardFrame(){DoubleBuffered=true;ResizeRedraw=true;BackColor=DashboardArt.Panel;}
    protected override void OnPaintBackground(PaintEventArgs e)=>DashboardLook.Card(e.Graphics,ClientRectangle);
}









